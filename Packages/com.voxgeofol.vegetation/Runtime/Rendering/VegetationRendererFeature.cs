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
    /// [INTEGRATION] URP feature that schedules the vegetation depth and color indirect passes.
    /// </summary>
    public sealed class VegetationRendererFeature : ScriptableRendererFeature
    {
        [Tooltip("Shared runtime settings for all vegetation containers rendered by this URP renderer feature.")]
        [SerializeField] private VegetationFoliageFeatureSettings settings = new VegetationFoliageFeatureSettings();

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

            depthPass.Setup(camera, settings.ClassifyShader, settings.EnableDiagnostics);
            colorPass.Setup(camera, settings.ClassifyShader, settings.EnableDiagnostics);
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
            private static readonly ProfilingSampler DepthPassSampler =
                new ProfilingSampler("VoxGeoFol.Vegetation.DepthPass");

            private static readonly ProfilingSampler ColorPassSampler =
                new ProfilingSampler("VoxGeoFol.Vegetation.ColorPass");

            private static readonly ProfilerMarker SetupMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.Setup");

            private static readonly ProfilerMarker DrawContainersCommandBufferMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.DrawContainers.CommandBuffer");

            private static readonly ProfilerMarker DrawContainersRasterMarker =
                new ProfilerMarker("VoxGeoFol.VegetationRenderPass.DrawContainers.Raster");

            private readonly VegetationRenderPassMode passMode;
            private readonly List<VegetationRuntimeContainer> containers = new List<VegetationRuntimeContainer>();
            private VegetationRuntimeContainer[] containerSnapshot = Array.Empty<VegetationRuntimeContainer>();
            private Camera? camera;
            private ComputeShader? classifyShader;
            private bool diagnosticsEnabled;
            private int containerSnapshotCount;
            private int lastSetupCameraInstanceId = -1;
            private int lastSetupContainerCount = -1;
            private int lastSetupShaderInstanceId = -1;
            private int lastExecutionCameraInstanceId = -1;
            private int lastExecutionActiveContainerCount = -1;
            private int lastExecutionPreparedContainerCount = -1;
            private int lastExecutionRenderedContainerCount = -1;
            private int lastExecutionMissingRendererCount = -1;

            public VegetationRenderPass(VegetationRenderPassMode passMode)
            {
                this.passMode = passMode;
                profilingSampler = passMode == VegetationRenderPassMode.Depth ? DepthPassSampler : ColorPassSampler;
            }

            public bool HasWork => camera != null && containerSnapshotCount > 0;

            public void Setup(Camera targetCamera, ComputeShader? targetClassifyShader, bool targetDiagnosticsEnabled)
            {
                using (SetupMarker.Auto())
                {
                    camera = targetCamera;
                    classifyShader = targetClassifyShader;
                    diagnosticsEnabled = targetDiagnosticsEnabled;
                    VegetationRuntimeContainer.GetActiveContainers(containers);
                    EnsureContainerSnapshotCapacity(containers.Count);
                    containerSnapshotCount = containers.Count;
                    for (int i = 0; i < containerSnapshotCount; i++)
                    {
                        containerSnapshot[i] = containers[i];
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
                    DrawContainers(containerSnapshot, containerSnapshotCount, camera, classifyShader, diagnosticsEnabled, passMode, commandBuffer);
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
                    }

                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        data.RenderPass.DrawContainers(data.Containers, data.ContainerCount, data.Camera, data.ClassifyShader,
                            data.DiagnosticsEnabled, data.PassMode, context.cmd);
                    });
                }
            }

            private void DrawContainers(
                IReadOnlyList<VegetationRuntimeContainer> containers,
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
                        VegetationRuntimeContainer container = containers[containerIndex];
                        if (container == null || !container.isActiveAndEnabled)
                        {
                            continue;
                        }

                        activeContainerCount++;
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

                    LogExecutionDiagnostics(camera, passMode, activeContainerCount, preparedContainerCount,
                        renderedContainerCount, missingRendererCount, diagnosticsEnabled);
                }
            }

            private void DrawContainers(
                IReadOnlyList<VegetationRuntimeContainer> containers,
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
                        VegetationRuntimeContainer container = containers[containerIndex];
                        if (container == null || !container.isActiveAndEnabled)
                        {
                            continue;
                        }

                        activeContainerCount++;
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

                containerSnapshot = new VegetationRuntimeContainer[newCapacity];
            }

            private sealed class PassData
            {
                public VegetationRenderPass RenderPass = null!;
                public Camera Camera = null!;
                public ComputeShader? ClassifyShader;
                public bool DiagnosticsEnabled;
                public VegetationRenderPassMode PassMode;
                public VegetationRuntimeContainer[] Containers = Array.Empty<VegetationRuntimeContainer>();
                public int ContainerCount;
            }
        }
    }
}
