#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
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
            private readonly VegetationRenderPassMode passMode;
            private readonly List<VegetationRuntimeManager> managers = new List<VegetationRuntimeManager>();
            private Camera? camera;
            private string lastSetupDiagnostics = string.Empty;
            private static string lastDepthExecutionDiagnostics = string.Empty;
            private static string lastColorExecutionDiagnostics = string.Empty;

            public VegetationRenderPass(VegetationRenderPassMode passMode)
            {
                this.passMode = passMode;
            }

            public bool HasWork => camera != null && managers.Count > 0;

            public void Setup(Camera targetCamera)
            {
                camera = targetCamera;
                VegetationRuntimeManager.GetActiveManagers(managers);

                string summary = $"VegetationRenderPass setup pass={passMode} camera={targetCamera.name} managers={managers.Count}";
                if (summary != lastSetupDiagnostics)
                {
                    lastSetupDiagnostics = summary;
                    UnityEngine.Debug.Log(summary);
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
                    DrawManagers(managers, camera, passMode, commandBuffer);
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
                    passData.Managers = managers.ToArray();

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
                        DrawManagers(data.Managers, data.Camera, data.PassMode, context.cmd);
                    });
                }
            }

            private static void DrawManagers(
                IReadOnlyList<VegetationRuntimeManager> managers,
                Camera camera,
                VegetationRenderPassMode passMode,
                CommandBuffer commandBuffer)
            {
                int activeManagerCount = 0;
                int preparedManagerCount = 0;
                int renderedManagerCount = 0;
                int missingRendererCount = 0;
                for (int managerIndex = 0; managerIndex < managers.Count; managerIndex++)
                {
                    VegetationRuntimeManager manager = managers[managerIndex];
                    if (manager == null || !manager.isActiveAndEnabled)
                    {
                        continue;
                    }

                    activeManagerCount++;
                    if (!manager.PrepareFrameForCamera(camera))
                    {
                        continue;
                    }

                    preparedManagerCount++;
                    if (manager.IndirectRenderer == null)
                    {
                        missingRendererCount++;
                        continue;
                    }

                    manager.IndirectRenderer.Render(commandBuffer, camera, passMode);
                    renderedManagerCount++;
                }

                LogExecutionDiagnostics(camera, passMode, activeManagerCount, preparedManagerCount, renderedManagerCount, missingRendererCount);
            }

            private static void DrawManagers(
                IReadOnlyList<VegetationRuntimeManager> managers,
                Camera camera,
                VegetationRenderPassMode passMode,
                IRasterCommandBuffer commandBuffer)
            {
                int activeManagerCount = 0;
                int preparedManagerCount = 0;
                int renderedManagerCount = 0;
                int missingRendererCount = 0;
                for (int managerIndex = 0; managerIndex < managers.Count; managerIndex++)
                {
                    VegetationRuntimeManager manager = managers[managerIndex];
                    if (manager == null || !manager.isActiveAndEnabled)
                    {
                        continue;
                    }

                    activeManagerCount++;
                    if (!manager.PrepareFrameForCamera(camera))
                    {
                        continue;
                    }

                    preparedManagerCount++;
                    if (manager.IndirectRenderer == null)
                    {
                        missingRendererCount++;
                        continue;
                    }

                    manager.IndirectRenderer.Render(commandBuffer, camera, passMode);
                    renderedManagerCount++;
                }

                LogExecutionDiagnostics(camera, passMode, activeManagerCount, preparedManagerCount, renderedManagerCount, missingRendererCount);
            }

            private static void LogExecutionDiagnostics(
                Camera camera,
                VegetationRenderPassMode passMode,
                int activeManagerCount,
                int preparedManagerCount,
                int renderedManagerCount,
                int missingRendererCount)
            {
                string summary =
                    $"VegetationRenderPass execute pass={passMode} camera={camera.name} activeManagers={activeManagerCount} preparedManagers={preparedManagerCount} renderedManagers={renderedManagerCount} missingRenderers={missingRendererCount}";
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

                if (renderedManagerCount == 0)
                {
                    UnityEngine.Debug.LogWarning(summary);
                }
                else
                {
                    UnityEngine.Debug.Log(summary);
                }
            }

            private sealed class PassData
            {
                public Camera Camera = null!;
                public VegetationRenderPassMode PassMode;
                public VegetationRuntimeManager[] Managers = Array.Empty<VegetationRuntimeManager>();
            }
        }
    }
}
