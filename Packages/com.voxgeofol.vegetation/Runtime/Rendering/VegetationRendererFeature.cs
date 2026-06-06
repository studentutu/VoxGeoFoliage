#nullable enable

using System;
using Unity.Collections;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] URP feature that renders the global compiled-page vegetation world.
    /// </summary>
    public sealed class VegetationRendererFeature : ScriptableRendererFeature
    {
        [Tooltip("Shared runtime settings for the compiled vegetation render world.")]
        [SerializeField] private VegetationFoliageFeatureSettings settings = new VegetationFoliageFeatureSettings();

        [Tooltip("Compute shader that records the RenderGraph vegetation frame contract before raster vegetation passes.")]
        [SerializeField] private ComputeShader? renderGraphPreparationComputeShader;

        private VegetationRenderPass? depthPass;
        private VegetationRenderPass? colorPass;
        private VegetationRenderPass? shadowPass;
        private ComputeShader? resolvedRenderGraphPreparationComputeShader;
        private bool featureFaulted;

        private static readonly ProfilerMarker AddRenderPassesMarker =
            new ProfilerMarker("VoxGeoFol.VegetationRendererFeature.AddRenderPasses");

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
            using (AddRenderPassesMarker.Auto())
            {
                if (featureFaulted || shadowPass == null || depthPass == null || colorPass == null)
                {
                    return;
                }

                try
                {
                    Camera camera = renderingData.cameraData.camera;
                    if (camera == null || !ShouldRenderCamera(camera.cameraType) || !VegetationRenderWorld.Shared.HasProviders)
                    {
                        return;
                    }

                    ComputeShader? preparationComputeShader = ResolveRenderGraphPreparationComputeShader();
                    if (preparationComputeShader == null)
                    {
                        MarkFeatureFault(
                            "resolve-render-graph-prepare",
                            new InvalidOperationException(
                                $"Missing compute resource '{VegetationRenderGraphPreparationContract.DefaultComputeResourceName}'."));
                        return;
                    }

                    shadowPass.Setup(camera, settings, preparationComputeShader);
                    depthPass.Setup(camera, settings, preparationComputeShader);
                    colorPass.Setup(camera, settings, preparationComputeShader);

                    if (settings.ShadowMode == VegetationShadowMode.CheapTree &&
                        shadowPass.HasWork &&
                        HasMainLightShadowAtlas(ref renderingData))
                    {
                        renderer.EnqueuePass(shadowPass);
                    }

                    if (settings.EnableDepthPass && depthPass.HasWork)
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
        }

        private ComputeShader? ResolveRenderGraphPreparationComputeShader()
        {
            if (renderGraphPreparationComputeShader != null)
            {
                return renderGraphPreparationComputeShader;
            }

            if (resolvedRenderGraphPreparationComputeShader == null)
            {
                resolvedRenderGraphPreparationComputeShader =
                    Resources.Load<ComputeShader>(VegetationRenderGraphPreparationContract.DefaultComputeResourceName);
            }

            return resolvedRenderGraphPreparationComputeShader;
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

        private static bool HasMainLightShadowAtlas(ref RenderingData renderingData)
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

            private static readonly ProfilerMarker DrawRasterMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.DrawRaster");

            private static readonly ProfilerMarker DrawShadowMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.DrawShadow");

            private static readonly ProfilerMarker RecordRenderGraphMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.RecordRenderGraph");

            private static readonly ProfilerMarker RecordShadowRenderGraphMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.RecordShadowRenderGraph");

            private static readonly ProfilerMarker ExecuteRasterPassMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.ExecuteRasterPass");

            private static readonly ProfilerMarker ExecuteShadowPassMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.ExecuteShadowPass");

            private static readonly ProfilerMarker DrawPrepareCameraMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.Draw.PrepareCamera");

            private static readonly ProfilerMarker DrawSubmitMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.Draw.Submit");

            private static readonly ProfilerMarker ShadowValidateMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.Shadow.Validate");

            private static readonly ProfilerMarker ShadowExtractCascadesMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.Shadow.ExtractCascades");

            private static readonly ProfilerMarker ShadowPrepareRenderWorldMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.Shadow.PrepareRenderWorld");

            private static readonly ProfilerMarker ShadowSubmitCascadesMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.Shadow.SubmitCascades");

            private static readonly ProfilerMarker ShadowSubmitCascadeMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.Shadow.SubmitCascade");

            private static readonly ProfilerMarker ShadowRestoreCameraMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.Shadow.RestoreCamera");

            private static readonly BaseRenderFunc<PassData, RasterGraphContext> ExecuteRasterPassFunc =
                ExecuteRasterPass;

            private static readonly BaseRenderFunc<ShadowPassData, RasterGraphContext> ExecuteShadowPassFunc =
                ExecuteShadowPass;

            private static readonly int UnityWorldToCameraId = Shader.PropertyToID("unity_WorldToCamera");
            private static readonly int UnityCameraToWorldId = Shader.PropertyToID("unity_CameraToWorld");
            private static readonly int WorldSpaceCameraPosId = Shader.PropertyToID("_WorldSpaceCameraPos");
            private static readonly int ShadowBiasId = Shader.PropertyToID("_ShadowBias");
            private static readonly int LightDirectionId = Shader.PropertyToID("_LightDirection");
            private static readonly int LightPositionId = Shader.PropertyToID("_LightPosition");
            private const string CastingPunctualLightShadowKeyword = "_CASTING_PUNCTUAL_LIGHT_SHADOW";

            private readonly VegetationRenderPassMode passMode;
            private readonly Plane[] shadowFrustumPlanes = new Plane[6];
            private readonly Plane[] shadowCascadeFrustumPlanes = new Plane[24];
            private readonly ShadowSliceData[] shadowCascadeSlices = new ShadowSliceData[4];
            private Camera? camera;
            private VegetationFoliageFeatureSettings? settings;
            private ComputeShader? preparationComputeShader;
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

            public bool HasWork =>
                !passFaulted &&
                camera != null &&
                settings != null &&
                preparationComputeShader != null &&
                VegetationRenderWorld.Shared.HasProviders;

            public void Setup(
                Camera targetCamera,
                VegetationFoliageFeatureSettings targetSettings,
                ComputeShader targetPreparationComputeShader)
            {
                camera = targetCamera;
                settings = targetSettings;
                preparationComputeShader = targetPreparationComputeShader;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                using (RecordRenderGraphMarker.Auto())
                {
                    if (!HasWork || camera == null || settings == null)
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

                        bool prepared;
                        VegetationRenderGraphFrame frame = default;
                        using (DrawPrepareCameraMarker.Auto())
                        {
                            prepared = VegetationRenderWorld.Shared.ScheduleRenderGraphPrepareForCamera(
                                camera,
                                passMode,
                                settings,
                                out frame);
                        }

                        if (!prepared)
                        {
                            return;
                        }

                        ComputeShader computeShader = preparationComputeShader!;
                        BufferHandle instanceBuffer = renderGraph.ImportBuffer(frame.InstanceBuffer);
                        BufferHandle argsBuffer = renderGraph.ImportBuffer(frame.ArgsBuffer);
                        BufferHandle preparationContract = VegetationRenderGraphPreparationContract.Record(
                            renderGraph,
                            computeShader,
                            passMode,
                            frame,
                            instanceBuffer,
                            argsBuffer);
                        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                        using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                                   passMode == VegetationRenderPassMode.Depth
                                       ? "Vegetation Depth Pass"
                                       : "Vegetation Color Pass",
                                   out PassData passData))
                        {
                            passData.RenderPass = this;
                            passData.Camera = camera;
                            passData.Settings = settings;
                            passData.PassMode = passMode;
                            passData.Frame = frame;
                            passData.InstanceBuffer = instanceBuffer;
                            passData.ArgsBuffer = argsBuffer;
                            passData.PreparationContract = preparationContract;

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

                            builder.UseBuffer(passData.InstanceBuffer, AccessFlags.Read);
                            builder.UseBuffer(passData.ArgsBuffer, AccessFlags.Read);
                            builder.UseBuffer(passData.PreparationContract, AccessFlags.Read);
                            builder.AllowPassCulling(false);
                            builder.SetRenderFunc(ExecuteRasterPassFunc);
                        }
                    }
                    catch (Exception exception)
                    {
                        MarkPassFault(exception);
                    }
                }
            }

            private void RecordShadowRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                using (RecordShadowRenderGraphMarker.Auto())
                {
                    UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                    UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                    UniversalLightData lightData = frameData.Get<UniversalLightData>();
                    UniversalShadowData shadowData = frameData.Get<UniversalShadowData>();
                    UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();

                    if (settings == null ||
                        preparationComputeShader == null ||
                        settings.ShadowMode == VegetationShadowMode.Off ||
                        cameraData.xrRendering ||
                        !shadowData.supportsMainLightShadows ||
                        lightData.mainLightIndex < 0 ||
                        !resourceData.mainShadowsTexture.IsValid())
                    {
                        return;
                    }

                    VisibleLight shadowLight;
                    Light shadowSource;
                    using (ShadowValidateMarker.Auto())
                    {
                        NativeArray<VisibleLight> visibleLights = lightData.visibleLights;
                        if (!visibleLights.IsCreated ||
                            lightData.mainLightIndex < 0 ||
                            lightData.mainLightIndex >= visibleLights.Length)
                        {
                            return;
                        }

                        shadowLight = visibleLights[lightData.mainLightIndex];
                        Light? candidateLight = shadowLight.light;
                        if (candidateLight == null ||
                            shadowLight.lightType != LightType.Directional ||
                            candidateLight.shadows == LightShadows.None ||
                            Mathf.Approximately(candidateLight.shadowStrength, 0f))
                        {
                            return;
                        }

                        shadowSource = candidateLight;
                    }

                    CullingResults cullResults = renderingData.cullResults;
                    int cascadeCount = Mathf.Clamp(shadowData.mainLightShadowCascadesCount, 1, 4);
                    int renderTargetWidth = shadowData.mainLightShadowmapWidth;
                    int renderTargetHeight = cascadeCount == 2
                        ? shadowData.mainLightShadowmapHeight >> 1
                        : shadowData.mainLightShadowmapHeight;
                    int shadowResolution = ShadowUtils.GetMaxTileResolutionInAtlas(
                        renderTargetWidth,
                        renderTargetHeight,
                        cascadeCount);

                    int preparedCascadeCount = 0;
                    using (ShadowExtractCascadesMarker.Auto())
                    {
                        for (int cascadeIndex = 0; cascadeIndex < cascadeCount; cascadeIndex++)
                        {
                            if (!ShadowUtils.ExtractDirectionalLightMatrix(
                                    ref cullResults,
                                    shadowData,
                                    lightData.mainLightIndex,
                                    cascadeIndex,
                                    renderTargetWidth,
                                    renderTargetHeight,
                                    shadowResolution,
                                    shadowSource.shadowNearPlane,
                                    out Vector4 _,
                                    out ShadowSliceData shadowSliceData))
                            {
                                continue;
                            }

                            GeometryUtility.CalculateFrustumPlanes(
                                shadowSliceData.projectionMatrix * shadowSliceData.viewMatrix,
                                shadowFrustumPlanes);
                            CopyFrustumPlanes(shadowFrustumPlanes, shadowCascadeFrustumPlanes, preparedCascadeCount * 6);
                            shadowCascadeSlices[preparedCascadeCount] = shadowSliceData;
                            preparedCascadeCount++;
                        }
                    }

                    bool prepared;
                    VegetationRenderGraphFrame frame = default;
                    using (ShadowPrepareRenderWorldMarker.Auto())
                    {
                        prepared = preparedCascadeCount > 0 &&
                                   VegetationRenderWorld.Shared.ScheduleRenderGraphPrepareForFrustums(
                                       cameraData.worldSpaceCameraPos,
                                       shadowCascadeFrustumPlanes,
                                       preparedCascadeCount,
                                       settings,
                                       out frame);
                    }

                    if (!prepared)
                    {
                        return;
                    }

                    BufferHandle instanceBuffer = renderGraph.ImportBuffer(frame.InstanceBuffer);
                    BufferHandle argsBuffer = renderGraph.ImportBuffer(frame.ArgsBuffer);
                    BufferHandle preparationContract = VegetationRenderGraphPreparationContract.Record(
                        renderGraph,
                        preparationComputeShader,
                        VegetationRenderPassMode.Shadow,
                        frame,
                        instanceBuffer,
                        argsBuffer);
                    using (var builder = renderGraph.AddRasterRenderPass<ShadowPassData>(
                               "Vegetation Shadow Pass",
                               out ShadowPassData passData,
                               ShadowPassSampler))
                    {
                        passData.RenderPass = this;
                        passData.Camera = camera!;
                        passData.CameraData = cameraData;
                        passData.ShadowData = shadowData;
                        passData.Settings = settings;
                        passData.ShadowLight = shadowLight;
                        passData.MainLightIndex = lightData.mainLightIndex;
                        passData.PreparedCascadeCount = preparedCascadeCount;
                        passData.Frame = frame;
                        passData.InstanceBuffer = instanceBuffer;
                        passData.ArgsBuffer = argsBuffer;
                        passData.PreparationContract = preparationContract;
                        for (int i = 0; i < preparedCascadeCount; i++)
                        {
                            passData.CascadeSlices[i] = shadowCascadeSlices[i];
                        }

                        builder.SetRenderAttachmentDepth(resourceData.mainShadowsTexture, AccessFlags.ReadWrite);
                        builder.UseBuffer(passData.InstanceBuffer, AccessFlags.Read);
                        builder.UseBuffer(passData.ArgsBuffer, AccessFlags.Read);
                        builder.UseBuffer(passData.PreparationContract, AccessFlags.Read);
                        builder.AllowPassCulling(false);
                        builder.AllowGlobalStateModification(true);
                        builder.SetRenderFunc(ExecuteShadowPassFunc);
                    }
                }
            }

            private static void ExecuteRasterPass(PassData data, RasterGraphContext context)
            {
                using (ExecuteRasterPassMarker.Auto())
                {
                    try
                    {
                        if (!VegetationRenderWorld.Shared.ActivateRenderGraphPreparedFrame(data.Frame))
                        {
                            return;
                        }

                        data.RenderPass.DrawWorld(data.Camera, data.Settings, data.PassMode, context.cmd);
                    }
                    catch (Exception exception)
                    {
                        data.RenderPass.MarkPassFault(exception);
                    }
                }
            }

            private static void ExecuteShadowPass(ShadowPassData data, RasterGraphContext context)
            {
                using (ExecuteShadowPassMarker.Auto())
                {
                    try
                    {
                        if (!VegetationRenderWorld.Shared.ActivateRenderGraphPreparedFrame(data.Frame))
                        {
                            return;
                        }

                        data.RenderPass.DrawMainLightShadowAtlas(data, context);
                    }
                    catch (Exception exception)
                    {
                        data.RenderPass.MarkPassFault(exception);
                    }
                }
            }

            private void DrawWorld(
                Camera targetCamera,
                VegetationFoliageFeatureSettings targetSettings,
                VegetationRenderPassMode targetPassMode,
                IRasterCommandBuffer commandBuffer)
            {
                using (DrawRasterMarker.Auto())
                {
                    using (DrawSubmitMarker.Auto())
                    {
                        VegetationRenderWorld.Shared.Render(
                            commandBuffer,
                            targetCamera,
                            targetPassMode,
                            targetSettings.EnableDiagnostics);
                    }
                }
            }

            private void DrawMainLightShadowAtlas(ShadowPassData data, RasterGraphContext context)
            {
                using (DrawShadowMarker.Auto())
                {
                    if (data.PreparedCascadeCount <= 0)
                    {
                        return;
                    }

                    VisibleLight shadowLight = data.ShadowLight;
                    RasterCommandBuffer commandBuffer = context.cmd;

                    try
                    {
                        ApplyCameraPosition(commandBuffer, data.CameraData);
                        commandBuffer.DisableShaderKeyword(CastingPunctualLightShadowKeyword);
                        using (ShadowSubmitCascadesMarker.Auto())
                        {
                            for (int cascadeIndex = 0; cascadeIndex < data.PreparedCascadeCount; cascadeIndex++)
                            {
                                if (!VegetationRenderWorld.Shared.HasPreparedShadowFrustum(cascadeIndex))
                                {
                                    continue;
                                }

                                using (ShadowSubmitCascadeMarker.Auto())
                                {
                                    ShadowSliceData shadowSliceData = data.CascadeSlices[cascadeIndex];
                                    Vector4 shadowBias = ShadowUtils.GetShadowBias(
                                        ref shadowLight,
                                        data.MainLightIndex,
                                        data.ShadowData,
                                        shadowSliceData.projectionMatrix,
                                        shadowSliceData.resolution);
                                    SetupShadowCasterConstants(commandBuffer, ref shadowLight, shadowBias);
                                    commandBuffer.SetGlobalDepthBias(1.0f, 2.5f);
                                    commandBuffer.SetViewport(new Rect(
                                        shadowSliceData.offsetX,
                                        shadowSliceData.offsetY,
                                        shadowSliceData.resolution,
                                        shadowSliceData.resolution));
                                    ApplyShadowViewProjectionMatrices(
                                        commandBuffer,
                                        shadowSliceData.viewMatrix,
                                        shadowSliceData.projectionMatrix);

                                    VegetationRenderWorld.Shared.Render(
                                        commandBuffer,
                                        data.Camera,
                                        VegetationRenderPassMode.Shadow,
                                        data.Settings.EnableDiagnostics,
                                        cascadeIndex);

                                    commandBuffer.DisableScissorRect();
                                    commandBuffer.SetGlobalDepthBias(0f, 0f);
                                }
                            }
                        }
                    }
                    finally
                    {
                        using (ShadowRestoreCameraMarker.Auto())
                        {
                            commandBuffer.SetGlobalDepthBias(0f, 0f);
                            RestoreCameraMatrices(commandBuffer, data.CameraData);
                        }
                    }
                }
            }

            private static void CopyFrustumPlanes(Plane[] source, Plane[] destination, int destinationOffset)
            {
                for (int i = 0; i < 6; i++)
                {
                    destination[destinationOffset + i] = source[i];
                }
            }

            private static void SetupShadowCasterConstants(
                RasterCommandBuffer commandBuffer,
                ref VisibleLight shadowLight,
                Vector4 shadowBias)
            {
                commandBuffer.SetGlobalVector(ShadowBiasId, shadowBias);

                Vector3 lightDirection = -shadowLight.localToWorldMatrix.GetColumn(2);
                commandBuffer.SetGlobalVector(
                    LightDirectionId,
                    new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0f));

                Vector3 lightPosition = shadowLight.localToWorldMatrix.GetColumn(3);
                commandBuffer.SetGlobalVector(
                    LightPositionId,
                    new Vector4(lightPosition.x, lightPosition.y, lightPosition.z, 1f));
            }

            private static void RestoreCameraMatrices(RasterCommandBuffer commandBuffer, UniversalCameraData cameraData)
            {
                Matrix4x4 viewMatrix = cameraData.GetViewMatrix();
                Matrix4x4 projectionMatrix = cameraData.GetProjectionMatrix();
                commandBuffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);

                commandBuffer.SetGlobalVector(WorldSpaceCameraPosId, cameraData.worldSpaceCameraPos);
                Matrix4x4 worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * viewMatrix;
                commandBuffer.SetGlobalMatrix(UnityWorldToCameraId, worldToCameraMatrix);
                commandBuffer.SetGlobalMatrix(UnityCameraToWorldId, worldToCameraMatrix.inverse);
            }

            private static void ApplyCameraPosition(RasterCommandBuffer commandBuffer, UniversalCameraData cameraData)
            {
                commandBuffer.SetGlobalVector(WorldSpaceCameraPosId, cameraData.worldSpaceCameraPos);
            }

            private static void ApplyShadowViewProjectionMatrices(
                RasterCommandBuffer commandBuffer,
                Matrix4x4 viewMatrix,
                Matrix4x4 projectionMatrix)
            {
                commandBuffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
                // Match URP custom-camera convention: shader globals get the GPU-adjusted projection, not the raw projection.
                Matrix4x4 gpuProjectionMatrix = GL.GetGPUProjectionMatrix(projectionMatrix, true);
                RenderingUtils.SetViewAndProjectionMatrices(commandBuffer, viewMatrix, gpuProjectionMatrix, false);
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
                public VegetationFoliageFeatureSettings Settings = null!;
                public VegetationRenderPassMode PassMode;
                public VegetationRenderGraphFrame Frame;
                public BufferHandle InstanceBuffer;
                public BufferHandle ArgsBuffer;
                public BufferHandle PreparationContract;
            }

            private sealed class ShadowPassData
            {
                public VegetationRenderPass RenderPass = null!;
                public Camera Camera = null!;
                public UniversalCameraData CameraData = null!;
                public UniversalShadowData ShadowData = null!;
                public VegetationFoliageFeatureSettings Settings = null!;
                public VisibleLight ShadowLight;
                public int MainLightIndex;
                public int PreparedCascadeCount;
                public VegetationRenderGraphFrame Frame;
                public BufferHandle InstanceBuffer;
                public BufferHandle ArgsBuffer;
                public BufferHandle PreparationContract;
                public readonly ShadowSliceData[] CascadeSlices = new ShadowSliceData[4];
            }
        }
    }
}
