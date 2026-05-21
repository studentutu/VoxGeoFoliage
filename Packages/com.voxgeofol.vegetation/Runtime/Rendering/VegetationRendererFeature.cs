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
                if (camera == null || !ShouldRenderCamera(camera.cameraType) || !VegetationRenderWorld.Shared.HasProviders)
                {
                    return;
                }

                shadowPass.Setup(camera, settings);
                depthPass.Setup(camera, settings);
                colorPass.Setup(camera, settings);

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

            private static readonly ProfilerMarker DrawCommandBufferMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.DrawCommandBuffer");

            private static readonly ProfilerMarker DrawShadowMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.DrawShadow");

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

            public bool HasWork => !passFaulted && camera != null && settings != null && VegetationRenderWorld.Shared.HasProviders;

            public void Setup(Camera targetCamera, VegetationFoliageFeatureSettings targetSettings)
            {
                camera = targetCamera;
                settings = targetSettings;
            }

#if !UNITY_6000_2_OR_NEWER
#pragma warning disable CS0672
#pragma warning disable CS0618
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (!HasWork || camera == null || settings == null || passMode == VegetationRenderPassMode.Shadow)
                {
                    return;
                }

                CommandBuffer commandBuffer = CommandBufferPool.Get(passMode == VegetationRenderPassMode.Depth
                    ? "Vegetation Depth Pass"
                    : "Vegetation Color Pass");
                try
                {
                    DrawWorld(camera, settings, passMode, commandBuffer);
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

                if (settings == null ||
                    settings.ShadowMode == VegetationShadowMode.Off ||
                    cameraData.xrRendering ||
                    !shadowData.supportsMainLightShadows ||
                    lightData.mainLightIndex < 0 ||
                    !resourceData.mainShadowsTexture.IsValid())
                {
                    return;
                }

                using (var builder = renderGraph.AddRasterRenderPass<ShadowPassData>(
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
                    passData.Settings = settings;

                    builder.SetRenderAttachmentDepth(resourceData.mainShadowsTexture, AccessFlags.ReadWrite);
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc<ShadowPassData>(ExecuteShadowPass);
                }
            }

            private static void ExecuteRasterPass(PassData data, RasterGraphContext context)
            {
                try
                {
                    data.RenderPass.DrawWorld(data.Camera, data.Settings, data.PassMode, context.cmd);
                }
                catch (Exception exception)
                {
                    data.RenderPass.MarkPassFault(exception);
                }
            }

            private static void ExecuteShadowPass(ShadowPassData data, RasterGraphContext context)
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

            private void DrawWorld(
                Camera targetCamera,
                VegetationFoliageFeatureSettings targetSettings,
                VegetationRenderPassMode targetPassMode,
                CommandBuffer commandBuffer)
            {
                using (DrawCommandBufferMarker.Auto())
                {
                    if (VegetationRenderWorld.Shared.PrepareForCamera(targetCamera, targetPassMode, targetSettings))
                    {
                        VegetationRenderWorld.Shared.Render(
                            commandBuffer,
                            targetCamera,
                            targetPassMode,
                            targetSettings.EnableDiagnostics);
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
                    if (VegetationRenderWorld.Shared.PrepareForCamera(targetCamera, targetPassMode, targetSettings))
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

                    RasterCommandBuffer commandBuffer = context.cmd;

                    try
                    {
                        ApplyCameraPosition(commandBuffer, data.CameraData);
                        commandBuffer.DisableShaderKeyword(CastingPunctualLightShadowKeyword);
                        int preparedCascadeCount = 0;
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
                            CopyFrustumPlanes(shadowFrustumPlanes, shadowCascadeFrustumPlanes, preparedCascadeCount * 6);
                            shadowCascadeSlices[preparedCascadeCount] = shadowSliceData;
                            preparedCascadeCount++;
                        }

                        if (preparedCascadeCount <= 0 ||
                            !VegetationRenderWorld.Shared.PrepareForFrustums(
                                data.CameraData.worldSpaceCameraPos,
                                shadowCascadeFrustumPlanes,
                                preparedCascadeCount,
                                data.Settings))
                        {
                            return;
                        }

                        for (int cascadeIndex = 0; cascadeIndex < preparedCascadeCount; cascadeIndex++)
                        {
                            if (!VegetationRenderWorld.Shared.HasPreparedShadowFrustum(cascadeIndex))
                            {
                                continue;
                            }

                            ShadowSliceData shadowSliceData = shadowCascadeSlices[cascadeIndex];
                            Vector4 shadowBias = ShadowUtils.GetShadowBias(
                                ref shadowLight,
                                data.LightData.mainLightIndex,
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
                    finally
                    {
                        commandBuffer.SetGlobalDepthBias(0f, 0f);
                        RestoreCameraMatrices(commandBuffer, data.CameraData);
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
            }

            private sealed class ShadowPassData
            {
                public VegetationRenderPass RenderPass = null!;
                public Camera Camera = null!;
                public UniversalCameraData CameraData = null!;
                public UniversalRenderingData RenderingData = null!;
                public UniversalLightData LightData = null!;
                public UniversalShadowData ShadowData = null!;
                public VegetationFoliageFeatureSettings Settings = null!;
            }
        }
    }
}
