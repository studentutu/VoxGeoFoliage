#nullable enable

using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] URP feature that schedules the vegetation shadow, depth, and color indirect passes.
    /// </summary>
    public sealed class VegetationRendererFeature : ScriptableRendererFeature
    {
        [Tooltip("Shared runtime settings for all vegetation containers rendered by this URP renderer feature.")]
        [SerializeField] private VegetationFoliageFeatureSettings settings = new VegetationFoliageFeatureSettings();

        private VegetationRenderPass? depthPass;
        private VegetationRenderPass? colorPass;
        private VegetationRenderPass? shadowPass;
        private bool featureFaulted;

        public override void Create()
        {
            try
            {
                featureFaulted = false;
                shadowPass = new VegetationRenderPass(VegetationRenderPassMode.Shadow)
                {
                    renderPassEvent = settings.ShadowPassEvent
                };
                depthPass = new VegetationRenderPass(VegetationRenderPassMode.Depth)
                {
                    renderPassEvent = settings.DepthPassEvent
                };
                colorPass = new VegetationRenderPass(VegetationRenderPassMode.Color)
                {
                    renderPassEvent = settings.ColorPassEvent
                };
            }
            catch (Exception exception)
            {
                shadowPass = null;
                depthPass = null;
                colorPass = null;
                MarkFeatureFault("create", exception);
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (featureFaulted || shadowPass == null || depthPass == null || colorPass == null)
            {
                return;
            }

            try
            {
                Camera camera = renderingData.cameraData.camera;
                if (camera == null || !ShouldRenderCamera(camera.cameraType))
                {
                    return;
                }

                shadowPass.Setup(
                    camera,
                    settings.ClassifyShader,
                    settings.EnableDiagnostics,
                    settings.AllowExpandedTreePromotionInShadows);
                depthPass.Setup(
                    camera,
                    settings.ClassifyShader,
                    settings.EnableDiagnostics,
                    settings.AllowExpandedTreePromotionInShadows);
                colorPass.Setup(
                    camera,
                    settings.ClassifyShader,
                    settings.EnableDiagnostics,
                    settings.AllowExpandedTreePromotionInShadows);
                if (settings.RenderMainLightShadows &&
                    shadowPass.HasWork &&
                    ShouldRenderMainLightShadows(ref renderingData))
                {
                    renderer.EnqueuePass(shadowPass);
                }

                if (depthPass.HasWork)
                {
                    renderer.EnqueuePass(depthPass);
                }

                if (colorPass.HasWork)
                {
                    renderer.EnqueuePass(colorPass);
                }
            }
            catch (Exception exception)
            {
                MarkFeatureFault("add-render-passes", exception);
            }
        }

        private bool ShouldRenderCamera(CameraType cameraType)
        {
            return cameraType switch
            {
                CameraType.Game => settings.RenderGameCameras,
                CameraType.SceneView => settings.RenderSceneViewCameras,
                _ => false
            };
        }

        private static bool ShouldRenderMainLightShadows(ref RenderingData renderingData)
        {
            int mainLightIndex = renderingData.lightData.mainLightIndex;
            if (mainLightIndex < 0)
            {
                return false;
            }

            NativeArray<VisibleLight> visibleLights = renderingData.lightData.visibleLights;
            if (!visibleLights.IsCreated || mainLightIndex >= visibleLights.Length)
            {
                return false;
            }

            VisibleLight shadowLight = visibleLights[mainLightIndex];
            Light? light = shadowLight.light;
            return !renderingData.cameraData.xrRendering &&
                   renderingData.shadowData.supportsMainLightShadows &&
                   shadowLight.lightType == LightType.Directional &&
                   light != null &&
                   light.shadows != LightShadows.None &&
                   !Mathf.Approximately(light.shadowStrength, 0f);
        }

        private void MarkFeatureFault(string stage, Exception exception)
        {
            if (featureFaulted)
            {
                return;
            }

            featureFaulted = true;
            Debug.LogError(
                $"VegetationRendererFeature disabled stage={stage} reason={exception.GetType().Name}: {exception.Message}");
            Debug.LogException(exception);
        }

        private sealed class VegetationRenderPass : ScriptableRenderPass
        {
            private static readonly ProfilingSampler DepthPassSampler =
                new ProfilingSampler("VoxGeoFol.Vegetation.DepthPass");

            private static readonly ProfilingSampler ColorPassSampler =
                new ProfilingSampler("VoxGeoFol.Vegetation.ColorPass");

            private static readonly ProfilingSampler ShadowPassSampler =
                new ProfilingSampler("VoxGeoFol.Vegetation.ShadowPass");

            private static readonly ProfilerMarker SetupMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.Setup");

            private static readonly ProfilerMarker DrawContainersCommandBufferMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.DrawContainers.CommandBuffer");

            private static readonly ProfilerMarker DrawContainersRasterMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.DrawContainers.Raster");

            private static readonly ProfilerMarker DrawShadowCommandBufferMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.DrawShadow.CommandBuffer");

            private static readonly int UnityWorldToCameraId = Shader.PropertyToID("unity_WorldToCamera");
            private static readonly int UnityCameraToWorldId = Shader.PropertyToID("unity_CameraToWorld");
            private static readonly int WorldSpaceCameraPosId = Shader.PropertyToID("_WorldSpaceCameraPos");
            private const string CastingPunctualLightShadowKeyword = "_CASTING_PUNCTUAL_LIGHT_SHADOW";

            private readonly VegetationRenderPassMode passMode;
            private readonly List<AuthoringContainerRuntime> containers = new List<AuthoringContainerRuntime>();
            private AuthoringContainerRuntime[] containerSnapshot = Array.Empty<AuthoringContainerRuntime>();
            private bool[] preparedContainerMask = Array.Empty<bool>();
            private bool[] renderedContainerMask = Array.Empty<bool>();
            private readonly Plane[] shadowFrustumPlanes = new Plane[6];
            private Camera? camera;
            private ComputeShader? classifyShader;
            private bool diagnosticsEnabled;
            private bool allowExpandedTreePromotionInShadows;
            private int containerSnapshotCount;
            private int lastSetupCameraInstanceId = -1;
            private int lastSetupContainerCount = -1;
            private int lastSetupShaderInstanceId = -1;
            private int lastExecutionCameraInstanceId = -1;
            private int lastExecutionActiveContainerCount = -1;
            private int lastExecutionPreparedContainerCount = -1;
            private int lastExecutionRenderedContainerCount = -1;
            private int lastExecutionMissingRendererCount = -1;
            private bool passFaulted;

            public VegetationRenderPass(VegetationRenderPassMode passMode)
            {
                this.passMode = passMode;
                profilingSampler = passMode switch
                {
                    VegetationRenderPassMode.Depth => DepthPassSampler,
                    VegetationRenderPassMode.Shadow => ShadowPassSampler,
                    _ => ColorPassSampler
                };
            }

            public bool HasWork => !passFaulted && camera != null && containerSnapshotCount > 0;

            public void Setup(
                Camera targetCamera,
                ComputeShader? targetClassifyShader,
                bool targetDiagnosticsEnabled,
                bool targetAllowExpandedTreePromotionInShadows)
            {
                if (passFaulted)
                {
                    return;
                }

                try
                {
                    using (SetupMarker.Auto())
                    {
                        camera = targetCamera;
                        classifyShader = targetClassifyShader;
                        diagnosticsEnabled = targetDiagnosticsEnabled;
                        allowExpandedTreePromotionInShadows = targetAllowExpandedTreePromotionInShadows;
                        VegetationActiveAuthoringContainerRuntimes.GetActive(containers);
                        EnsureContainerSnapshotCapacity(containers.Count);
                        int previousSnapshotCount = containerSnapshotCount;
                        containerSnapshotCount = containers.Count;
                        for (int i = 0; i < containerSnapshotCount; i++)
                        {
                            containerSnapshot[i] = containers[i];
                        }

                        for (int i = containerSnapshotCount; i < previousSnapshotCount; i++)
                        {
                            containerSnapshot[i] = null!;
                        }

                        if (!diagnosticsEnabled)
                        {
                            return;
                        }

                        int cameraInstanceId = targetCamera.GetInstanceID();
                        int shaderInstanceId = targetClassifyShader != null ? targetClassifyShader.GetInstanceID() : 0;
                        if (cameraInstanceId == lastSetupCameraInstanceId &&
                            containers.Count == lastSetupContainerCount &&
                            shaderInstanceId == lastSetupShaderInstanceId)
                        {
                            return;
                        }

                        lastSetupCameraInstanceId = cameraInstanceId;
                        lastSetupContainerCount = containers.Count;
                        lastSetupShaderInstanceId = shaderInstanceId;
                        UnityEngine.Debug.Log(
                            $"VegetationRenderPass setup pass={passMode} camera={targetCamera.name} containers={containers.Count} classifyShader={(targetClassifyShader != null ? targetClassifyShader.name : "<none>")} diagnostics={targetDiagnosticsEnabled}");
                    }
                }
                catch (Exception exception)
                {
                    MarkPassFault(exception);
                }
            }

#if !UNITY_6000_2_OR_NEWER
#pragma warning disable CS0672
#pragma warning disable CS0618
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (!HasWork || camera == null || passMode == VegetationRenderPassMode.Shadow)
                {
                    return;
                }

                CommandBuffer commandBuffer = CommandBufferPool.Get(passMode == VegetationRenderPassMode.Depth
                    ? "Vegetation Depth Pass"
                    : "Vegetation Color Pass");
                try
                {
                    DrawContainers(containerSnapshot, containerSnapshotCount, camera, classifyShader, diagnosticsEnabled, passMode, commandBuffer);
                    context.ExecuteCommandBuffer(commandBuffer);
                }
                catch (Exception exception)
                {
                    MarkPassFault(exception);
                }
                finally
                {
                    commandBuffer.Clear();
                    CommandBufferPool.Release(commandBuffer);
                }
            }
#pragma warning restore CS0618
#pragma warning restore CS0672
#endif

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (!HasWork || camera == null)
                {
                    return;
                }

                try
                {
                    if (passMode == VegetationRenderPassMode.Shadow)
                    {
                        RecordShadowRenderGraph(renderGraph, frameData);
                        return;
                    }

                    UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                    using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                               passMode == VegetationRenderPassMode.Depth
                                   ? "Vegetation Depth Pass"
                                   : "Vegetation Color Pass",
                               out PassData passData))
                    {
                        passData.RenderPass = this;
                        passData.Camera = camera;
                        passData.ClassifyShader = classifyShader;
                        passData.DiagnosticsEnabled = diagnosticsEnabled;
                        passData.PassMode = passMode;
                        passData.Containers = containerSnapshot;
                        passData.ContainerCount = containerSnapshotCount;

                        if (passMode == VegetationRenderPassMode.Depth)
                        {
                            builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Write);
                        }
                        else
                        {
                            builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                            builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
                            if (resourceData.mainShadowsTexture.IsValid())
                            {
                                builder.UseTexture(resourceData.mainShadowsTexture, AccessFlags.Read);
                            }
                        }

                        builder.AllowPassCulling(false);
                        builder.AllowGlobalStateModification(true);
                        builder.SetRenderFunc<PassData>(ExecuteRasterPass);
                    }
                }
                catch (Exception exception)
                {
                    MarkPassFault(exception);
                }
            }

            private void RecordShadowRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                UniversalShadowData shadowData = frameData.Get<UniversalShadowData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();

                if (cameraData.xrRendering ||
                    !shadowData.supportsMainLightShadows ||
                    lightData.mainLightIndex < 0 ||
                    !resourceData.mainShadowsTexture.IsValid())
                {
                    return;
                }

                using (var builder = renderGraph.AddUnsafePass<ShadowPassData>(
                           "Vegetation Shadow Pass",
                           out ShadowPassData passData,
                           ShadowPassSampler))
                {
                    passData.RenderPass = this;
                    passData.Camera = camera!;
                    passData.CameraData = cameraData;
                    passData.RenderingData = renderingData;
                    passData.LightData = lightData;
                    passData.ShadowData = shadowData;
                    passData.ClassifyShader = classifyShader;
                    passData.DiagnosticsEnabled = diagnosticsEnabled;
                    passData.AllowExpandedTreePromotionInShadows = allowExpandedTreePromotionInShadows;
                    passData.Containers = containerSnapshot;
                    passData.ContainerCount = containerSnapshotCount;
                    passData.MainShadowTexture = resourceData.mainShadowsTexture;

                    builder.UseTexture(resourceData.mainShadowsTexture, AccessFlags.Write);
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc<ShadowPassData>(ExecuteShadowPass);
                }
            }

            private static void ExecuteRasterPass(PassData data, RasterGraphContext context)
            {
                try
                {
                    data.RenderPass.DrawContainers(
                        data.Containers,
                        data.ContainerCount,
                        data.Camera,
                        data.ClassifyShader,
                        data.DiagnosticsEnabled,
                        data.PassMode,
                        context.cmd);
                }
                catch (Exception exception)
                {
                    data.RenderPass.MarkPassFault(exception);
                }
            }

            private static void ExecuteShadowPass(ShadowPassData data, UnsafeGraphContext context)
            {
                try
                {
                    data.RenderPass.DrawMainLightShadowAtlas(data, context);
                }
                catch (Exception exception)
                {
                    data.RenderPass.MarkPassFault(exception);
                }
            }

            private void DrawContainers(
                IReadOnlyList<AuthoringContainerRuntime> containers,
                int containerCount,
                Camera camera,
                ComputeShader? classifyShader,
                bool diagnosticsEnabled,
                VegetationRenderPassMode passMode,
                CommandBuffer commandBuffer)
            {
                using (DrawContainersCommandBufferMarker.Auto())
                {
                    int activeContainerCount = 0;
                    int preparedContainerCount = 0;
                    int renderedContainerCount = 0;
                    int missingRendererCount = 0;
                    for (int containerIndex = 0; containerIndex < containerCount; containerIndex++)
                    {
                        AuthoringContainerRuntime container = containers[containerIndex];
                        if (container == null)
                        {
                            continue;
                        }

                        activeContainerCount++;
                        try
                        {
                            if (!container.PrepareFrameForCamera(camera, classifyShader, diagnosticsEnabled))
                            {
                                continue;
                            }

                            preparedContainerCount++;
                            if (container.IndirectRenderer == null)
                            {
                                missingRendererCount++;
                                continue;
                            }

                            container.IndirectRenderer.Render(commandBuffer, camera, passMode, diagnosticsEnabled);
                            renderedContainerCount++;
                        }
                        catch (Exception exception)
                        {
                            container.MarkRenderRuntimeFault($"{passMode.ToString().ToLowerInvariant()}-render", exception);
                        }
                    }

                    LogExecutionDiagnostics(camera, passMode, activeContainerCount, preparedContainerCount,
                        renderedContainerCount, missingRendererCount, diagnosticsEnabled);
                }
            }

            private void DrawMainLightShadowAtlas(ShadowPassData data, UnsafeGraphContext context)
            {
                using (DrawShadowCommandBufferMarker.Auto())
                {
                    NativeArray<VisibleLight> visibleLights = data.LightData.visibleLights;
                    if (!visibleLights.IsCreated ||
                        data.LightData.mainLightIndex < 0 ||
                        data.LightData.mainLightIndex >= visibleLights.Length)
                    {
                        return;
                    }

                    VisibleLight shadowLight = visibleLights[data.LightData.mainLightIndex];
                    Light? light = shadowLight.light;
                    if (light == null ||
                        shadowLight.lightType != LightType.Directional ||
                        light.shadows == LightShadows.None ||
                        Mathf.Approximately(light.shadowStrength, 0f))
                    {
                        return;
                    }

                    EnsurePreparedContainerMaskCapacity(data.ContainerCount);
                    int activeContainerCount = 0;
                    int preparedContainerCount = 0;
                    int renderedContainerCount = 0;
                    int missingRendererCount = 0;
                    for (int containerIndex = 0; containerIndex < data.ContainerCount; containerIndex++)
                    {
                        preparedContainerMask[containerIndex] = false;
                        renderedContainerMask[containerIndex] = false;
                        if (data.Containers[containerIndex] != null)
                        {
                            activeContainerCount++;
                        }
                    }

                    if (activeContainerCount == 0)
                    {
                        LogExecutionDiagnostics(
                            data.Camera,
                            VegetationRenderPassMode.Shadow,
                            activeContainerCount,
                            preparedContainerCount,
                            renderedContainerCount,
                            missingRendererCount,
                            data.DiagnosticsEnabled);
                        return;
                    }

                    CullingResults cullResults = data.RenderingData.cullResults;
                    int cascadeCount = Mathf.Clamp(data.ShadowData.mainLightShadowCascadesCount, 1, 4);
                    int renderTargetWidth = data.ShadowData.mainLightShadowmapWidth;
                    int renderTargetHeight = cascadeCount == 2
                        ? data.ShadowData.mainLightShadowmapHeight >> 1
                        : data.ShadowData.mainLightShadowmapHeight;
                    int shadowResolution = ShadowUtils.GetMaxTileResolutionInAtlas(
                        renderTargetWidth,
                        renderTargetHeight,
                        cascadeCount);

                    UnsafeCommandBuffer unsafeCommandBuffer = context.cmd;
                    CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    unsafeCommandBuffer.SetRenderTarget(data.MainShadowTexture, 0, CubemapFace.Unknown, -1);

                    try
                    {
                        ApplyCameraGlobals(nativeCommandBuffer, data.CameraData);
                        unsafeCommandBuffer.DisableShaderKeyword(CastingPunctualLightShadowKeyword);
                        for (int cascadeIndex = 0; cascadeIndex < cascadeCount; cascadeIndex++)
                        {
                            if (!ShadowUtils.ExtractDirectionalLightMatrix(
                                    ref cullResults,
                                    data.ShadowData,
                                    data.LightData.mainLightIndex,
                                    cascadeIndex,
                                    renderTargetWidth,
                                    renderTargetHeight,
                                    shadowResolution,
                                    light.shadowNearPlane,
                                    out Vector4 _,
                                    out ShadowSliceData shadowSliceData))
                            {
                                continue;
                            }

                            GeometryUtility.CalculateFrustumPlanes(
                                shadowSliceData.projectionMatrix * shadowSliceData.viewMatrix,
                                shadowFrustumPlanes);
                            Vector4 shadowBias = ShadowUtils.GetShadowBias(
                                ref shadowLight,
                                data.LightData.mainLightIndex,
                                data.ShadowData,
                                shadowSliceData.projectionMatrix,
                                shadowSliceData.resolution);
                            ShadowUtils.SetupShadowCasterConstantBuffer(nativeCommandBuffer, ref shadowLight, shadowBias);
                            nativeCommandBuffer.SetGlobalDepthBias(1.0f, 2.5f);
                            nativeCommandBuffer.SetViewport(new Rect(
                                shadowSliceData.offsetX,
                                shadowSliceData.offsetY,
                                shadowSliceData.resolution,
                                shadowSliceData.resolution));
                            nativeCommandBuffer.SetViewProjectionMatrices(
                                shadowSliceData.viewMatrix,
                                shadowSliceData.projectionMatrix);

                            for (int containerIndex = 0; containerIndex < data.ContainerCount; containerIndex++)
                            {
                                AuthoringContainerRuntime container = data.Containers[containerIndex];
                                if (container == null)
                                {
                                    continue;
                                }

                                try
                                {
                                    if (!container.PrepareFrameForFrustum(
                                            data.CameraData.worldSpaceCameraPos,
                                            shadowFrustumPlanes,
                                            data.ClassifyShader,
                                            data.DiagnosticsEnabled,
                                            data.AllowExpandedTreePromotionInShadows))
                                    {
                                        continue;
                                    }

                                    if (!preparedContainerMask[containerIndex])
                                    {
                                        preparedContainerMask[containerIndex] = true;
                                        preparedContainerCount++;
                                    }

                                    if (container.IndirectRenderer == null)
                                    {
                                        missingRendererCount++;
                                        continue;
                                    }

                                    container.IndirectRenderer!.Render(
                                        nativeCommandBuffer,
                                        data.Camera,
                                        VegetationRenderPassMode.Shadow,
                                        data.DiagnosticsEnabled);
                                    if (!renderedContainerMask[containerIndex])
                                    {
                                        renderedContainerMask[containerIndex] = true;
                                        renderedContainerCount++;
                                    }
                                }
                                catch (Exception exception)
                                {
                                    container.MarkRenderRuntimeFault("shadow-render", exception);
                                }
                            }

                            nativeCommandBuffer.DisableScissorRect();
                            nativeCommandBuffer.SetGlobalDepthBias(0f, 0f);
                        }
                    }
                    finally
                    {
                        RestoreCameraMatrices(nativeCommandBuffer, data.CameraData);
                    }

                    LogExecutionDiagnostics(
                        data.Camera,
                        VegetationRenderPassMode.Shadow,
                        activeContainerCount,
                        preparedContainerCount,
                        renderedContainerCount,
                        missingRendererCount,
                        data.DiagnosticsEnabled);
                }
            }

            private void RestoreCameraMatrices(CommandBuffer commandBuffer, UniversalCameraData cameraData)
            {
                Matrix4x4 viewMatrix = cameraData.GetViewMatrix();
                Matrix4x4 projectionMatrix = cameraData.GetProjectionMatrix();
                commandBuffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);

                commandBuffer.SetGlobalVector(WorldSpaceCameraPosId, cameraData.worldSpaceCameraPos);
                Matrix4x4 worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * viewMatrix;
                commandBuffer.SetGlobalMatrix(UnityWorldToCameraId, worldToCameraMatrix);
                commandBuffer.SetGlobalMatrix(UnityCameraToWorldId, worldToCameraMatrix.inverse);
            }

            private void ApplyCameraGlobals(CommandBuffer commandBuffer, UniversalCameraData cameraData)
            {
                commandBuffer.SetGlobalVector(WorldSpaceCameraPosId, cameraData.worldSpaceCameraPos);

                Matrix4x4 viewMatrix = cameraData.GetViewMatrix();
                Matrix4x4 worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * viewMatrix;
                commandBuffer.SetGlobalMatrix(UnityWorldToCameraId, worldToCameraMatrix);
                commandBuffer.SetGlobalMatrix(UnityCameraToWorldId, worldToCameraMatrix.inverse);
            }

            private void DrawContainers(
                IReadOnlyList<AuthoringContainerRuntime> containers,
                int containerCount,
                Camera camera,
                ComputeShader? classifyShader,
                bool diagnosticsEnabled,
                VegetationRenderPassMode passMode,
                IRasterCommandBuffer commandBuffer)
            {
                using (DrawContainersRasterMarker.Auto())
                {
                    int activeContainerCount = 0;
                    int preparedContainerCount = 0;
                    int renderedContainerCount = 0;
                    int missingRendererCount = 0;
                    for (int containerIndex = 0; containerIndex < containerCount; containerIndex++)
                    {
                        AuthoringContainerRuntime container = containers[containerIndex];
                        if (container == null)
                        {
                            continue;
                        }

                        activeContainerCount++;
                        try
                        {
                            if (!container.PrepareFrameForCamera(camera, classifyShader, diagnosticsEnabled))
                            {
                                continue;
                            }

                            preparedContainerCount++;
                            if (container.IndirectRenderer == null)
                            {
                                missingRendererCount++;
                                continue;
                            }

                            container.IndirectRenderer.Render(commandBuffer, camera, passMode, diagnosticsEnabled);
                            renderedContainerCount++;
                        }
                        catch (Exception exception)
                        {
                            container.MarkRenderRuntimeFault($"{passMode.ToString().ToLowerInvariant()}-render", exception);
                        }
                    }

                    LogExecutionDiagnostics(camera, passMode, activeContainerCount, preparedContainerCount,
                        renderedContainerCount, missingRendererCount, diagnosticsEnabled);
                }
            }

            private void LogExecutionDiagnostics(
                Camera camera,
                VegetationRenderPassMode passMode,
                int activeContainerCount,
                int preparedContainerCount,
                int renderedContainerCount,
                int missingRendererCount,
                bool diagnosticsEnabled)
            {
                if (!diagnosticsEnabled)
                {
                    return;
                }

                int cameraInstanceId = camera.GetInstanceID();
                if (cameraInstanceId == lastExecutionCameraInstanceId &&
                    activeContainerCount == lastExecutionActiveContainerCount &&
                    preparedContainerCount == lastExecutionPreparedContainerCount &&
                    renderedContainerCount == lastExecutionRenderedContainerCount &&
                    missingRendererCount == lastExecutionMissingRendererCount)
                {
                    return;
                }

                lastExecutionCameraInstanceId = cameraInstanceId;
                lastExecutionActiveContainerCount = activeContainerCount;
                lastExecutionPreparedContainerCount = preparedContainerCount;
                lastExecutionRenderedContainerCount = renderedContainerCount;
                lastExecutionMissingRendererCount = missingRendererCount;

                string summary =
                    $"VegetationRenderPass execute pass={passMode} camera={camera.name} activeContainers={activeContainerCount} preparedContainers={preparedContainerCount} renderedContainers={renderedContainerCount} missingRenderers={missingRendererCount}";

                if (renderedContainerCount == 0)
                {
                    UnityEngine.Debug.LogWarning(summary);
                }
                else
                {
                    UnityEngine.Debug.Log(summary);
                }
            }

            private void EnsureContainerSnapshotCapacity(int requiredCount)
            {
                if (containerSnapshot.Length >= requiredCount)
                {
                    return;
                }

                int newCapacity = Mathf.Max(1, containerSnapshot.Length);
                while (newCapacity < requiredCount)
                {
                    newCapacity <<= 1;
                }

                containerSnapshot = new AuthoringContainerRuntime[newCapacity];
            }

            private void EnsurePreparedContainerMaskCapacity(int requiredCount)
            {
                if (preparedContainerMask.Length >= requiredCount)
                {
                    EnsureRenderedContainerMaskCapacity(requiredCount);
                    return;
                }

                int newCapacity = Mathf.Max(1, preparedContainerMask.Length);
                while (newCapacity < requiredCount)
                {
                    newCapacity <<= 1;
                }

                preparedContainerMask = new bool[newCapacity];
                EnsureRenderedContainerMaskCapacity(requiredCount);
            }

            private void EnsureRenderedContainerMaskCapacity(int requiredCount)
            {
                if (renderedContainerMask.Length >= requiredCount)
                {
                    return;
                }

                int newCapacity = Mathf.Max(1, renderedContainerMask.Length);
                while (newCapacity < requiredCount)
                {
                    newCapacity <<= 1;
                }

                renderedContainerMask = new bool[newCapacity];
            }

            private void MarkPassFault(Exception exception)
            {
                if (passFaulted)
                {
                    return;
                }

                passFaulted = true;
                Debug.LogError(
                    $"VegetationRenderPass disabled pass={passMode} reason={exception.GetType().Name}: {exception.Message}");
                Debug.LogException(exception);
            }

            private sealed class PassData
            {
                public VegetationRenderPass RenderPass = null!;
                public Camera Camera = null!;
                public ComputeShader? ClassifyShader;
                public bool DiagnosticsEnabled;
                public VegetationRenderPassMode PassMode;
                public AuthoringContainerRuntime[] Containers = Array.Empty<AuthoringContainerRuntime>();
                public int ContainerCount;
            }

            private sealed class ShadowPassData
            {
                public VegetationRenderPass RenderPass = null!;
                public Camera Camera = null!;
                public UniversalCameraData CameraData = null!;
                public UniversalRenderingData RenderingData = null!;
                public UniversalLightData LightData = null!;
                public UniversalShadowData ShadowData = null!;
                public ComputeShader? ClassifyShader;
                public bool DiagnosticsEnabled;
                public bool AllowExpandedTreePromotionInShadows;
                public AuthoringContainerRuntime[] Containers = Array.Empty<AuthoringContainerRuntime>();
                public int ContainerCount;
                public TextureHandle MainShadowTexture;
            }
        }
    }
}
