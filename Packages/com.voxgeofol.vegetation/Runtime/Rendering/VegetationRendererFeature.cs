#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] URP feature that schedules the Phase E vegetation depth and color indirect passes.
    /// </summary>
    public sealed class VegetationRendererFeature : ScriptableRendererFeature
    {
        [Serializable]
        private sealed class FeatureSettings
        {
            public RenderPassEvent DepthPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            public RenderPassEvent ColorPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingOpaques + 1);
            public bool RenderGameCameras = true;
            public bool RenderSceneViewCameras = true;
        }

        [SerializeField] private FeatureSettings settings = new FeatureSettings();

        private VegetationRenderPass? depthPass;
        private VegetationRenderPass? colorPass;

        public override void Create()
        {
            depthPass = new VegetationRenderPass(VegetationRenderPassMode.Depth)
            {
                renderPassEvent = settings.DepthPassEvent
            };
            colorPass = new VegetationRenderPass(VegetationRenderPassMode.Color)
            {
                renderPassEvent = settings.ColorPassEvent
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (depthPass == null || colorPass == null)
            {
                return;
            }

            Camera camera = renderingData.cameraData.camera;
            if (!ShouldRenderCamera(camera.cameraType))
            {
                return;
            }

            depthPass.Setup(camera);
            colorPass.Setup(camera);
            if (depthPass.HasWork)
            {
                renderer.EnqueuePass(depthPass);
            }

            if (colorPass.HasWork)
            {
                renderer.EnqueuePass(colorPass);
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

        private sealed class VegetationRenderPass : ScriptableRenderPass
        {
            private static readonly ProfilingSampler DepthPassSampler = new ProfilingSampler("VoxGeoFol.Vegetation.DepthPass");
            private static readonly ProfilingSampler ColorPassSampler = new ProfilingSampler("VoxGeoFol.Vegetation.ColorPass");
            private static readonly ProfilerMarker SetupMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderPass.Setup");
            private static readonly ProfilerMarker DrawContainersCommandBufferMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderPass.DrawContainers.CommandBuffer");
            private static readonly ProfilerMarker DrawContainersRasterMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderPass.DrawContainers.Raster");
            private readonly VegetationRenderPassMode passMode;
            private readonly List<VegetationRuntimeContainer> containers = new List<VegetationRuntimeContainer>();
            private VegetationRuntimeContainer[] containerSnapshot = Array.Empty<VegetationRuntimeContainer>();
            private Camera? camera;
            private int containerSnapshotCount;
            private string lastSetupDiagnostics = string.Empty;
            private static string lastDepthExecutionDiagnostics = string.Empty;
            private static string lastColorExecutionDiagnostics = string.Empty;

            public VegetationRenderPass(VegetationRenderPassMode passMode)
            {
                this.passMode = passMode;
                profilingSampler = passMode == VegetationRenderPassMode.Depth ? DepthPassSampler : ColorPassSampler;
            }

            public bool HasWork => camera != null && containerSnapshotCount > 0;

            public void Setup(Camera targetCamera)
            {
                using (SetupMarker.Auto())
                {
                    camera = targetCamera;
                    VegetationRuntimeContainer.GetActiveContainers(containers);
                    EnsureContainerSnapshotCapacity(containers.Count);
                    containerSnapshotCount = containers.Count;
                    for (int i = 0; i < containerSnapshotCount; i++)
                    {
                        containerSnapshot[i] = containers[i];
                    }

                    if (!ShouldLogDiagnostics(containers, containers.Count))
                    {
                        return;
                    }

                    string summary = $"VegetationRenderPass setup pass={passMode} camera={targetCamera.name} containers={containers.Count}";
                    if (summary != lastSetupDiagnostics)
                    {
                        lastSetupDiagnostics = summary;
                        UnityEngine.Debug.Log(summary);
                    }
                }
            }

#if !UNITY_6000_2_OR_NEWER
#pragma warning disable CS0672
#pragma warning disable CS0618
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (!HasWork || camera == null)
                {
                    return;
                }

                CommandBuffer commandBuffer = CommandBufferPool.Get(passMode == VegetationRenderPassMode.Depth
                    ? "Vegetation Depth Pass"
                    : "Vegetation Color Pass");
                try
                {
                    DrawContainers(containerSnapshot, containerSnapshotCount, camera, passMode, commandBuffer);
                    context.ExecuteCommandBuffer(commandBuffer);
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

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                    passMode == VegetationRenderPassMode.Depth ? "Vegetation Depth Pass" : "Vegetation Color Pass",
                    out PassData passData))
                {
                    passData.Camera = camera;
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
                    }

                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        DrawContainers(data.Containers, data.ContainerCount, data.Camera, data.PassMode, context.cmd);
                    });
                }
            }

            private static void DrawContainers(
                IReadOnlyList<VegetationRuntimeContainer> containers,
                int containerCount,
                Camera camera,
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
                        VegetationRuntimeContainer container = containers[containerIndex];
                        if (container == null || !container.isActiveAndEnabled)
                        {
                            continue;
                        }

                        activeContainerCount++;
                        if (!container.PrepareFrameForCamera(camera))
                        {
                            continue;
                        }

                        preparedContainerCount++;
                        if (container.IndirectRenderer == null)
                        {
                            missingRendererCount++;
                            continue;
                        }

                        container.IndirectRenderer.Render(commandBuffer, camera, passMode);
                        renderedContainerCount++;
                    }

                    LogExecutionDiagnostics(camera, passMode, activeContainerCount, preparedContainerCount, renderedContainerCount, missingRendererCount, containers, containerCount);
                }
            }

            private static void DrawContainers(
                IReadOnlyList<VegetationRuntimeContainer> containers,
                int containerCount,
                Camera camera,
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
                        VegetationRuntimeContainer container = containers[containerIndex];
                        if (container == null || !container.isActiveAndEnabled)
                        {
                            continue;
                        }

                        activeContainerCount++;
                        if (!container.PrepareFrameForCamera(camera))
                        {
                            continue;
                        }

                        preparedContainerCount++;
                        if (container.IndirectRenderer == null)
                        {
                            missingRendererCount++;
                            continue;
                        }

                        container.IndirectRenderer.Render(commandBuffer, camera, passMode);
                        renderedContainerCount++;
                    }

                    LogExecutionDiagnostics(camera, passMode, activeContainerCount, preparedContainerCount, renderedContainerCount, missingRendererCount, containers, containerCount);
                }
            }

            private static void LogExecutionDiagnostics(
                Camera camera,
                VegetationRenderPassMode passMode,
                int activeContainerCount,
                int preparedContainerCount,
                int renderedContainerCount,
                int missingRendererCount,
                IReadOnlyList<VegetationRuntimeContainer>? containers = null,
                int containerCount = 0)
            {
                if (containers != null && !ShouldLogDiagnostics(containers, containerCount))
                {
                    return;
                }

                string summary =
                    $"VegetationRenderPass execute pass={passMode} camera={camera.name} activeContainers={activeContainerCount} preparedContainers={preparedContainerCount} renderedContainers={renderedContainerCount} missingRenderers={missingRendererCount}";
                bool isDepthPass = passMode == VegetationRenderPassMode.Depth;
                string previousSummary = isDepthPass ? lastDepthExecutionDiagnostics : lastColorExecutionDiagnostics;
                if (summary == previousSummary)
                {
                    return;
                }

                if (isDepthPass)
                {
                    lastDepthExecutionDiagnostics = summary;
                }
                else
                {
                    lastColorExecutionDiagnostics = summary;
                }

                if (renderedContainerCount == 0)
                {
                    UnityEngine.Debug.LogWarning(summary);
                }
                else
                {
                    UnityEngine.Debug.Log(summary);
                }
            }

            private static bool ShouldLogDiagnostics(IReadOnlyList<VegetationRuntimeContainer> containers, int containerCount)
            {
                for (int i = 0; i < containerCount; i++)
                {
                    VegetationRuntimeContainer container = containers[i];
                    if (container != null && container.DiagnosticsEnabled)
                    {
                        return true;
                    }
                }

                return false;
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

                containerSnapshot = new VegetationRuntimeContainer[newCapacity];
            }

            private sealed class PassData
            {
                public Camera Camera = null!;
                public VegetationRenderPassMode PassMode;
                public VegetationRuntimeContainer[] Containers = Array.Empty<VegetationRuntimeContainer>();
                public int ContainerCount;
            }
        }
    }
}
