#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] Owns indirect args, shared instance bindings, and final draw submission.
    /// </summary>
    public sealed class VegetationIndirectRenderer : IDisposable
    {
        private static readonly ProfilerMarker BindGpuResidentFrameMarker = new ProfilerMarker("VoxGeoFol.VegetationIndirectRenderer.BindGpuResidentFrame");
        private static readonly ProfilerMarker RenderMarker = new ProfilerMarker("VoxGeoFol.VegetationIndirectRenderer.Render");
        private static readonly int InstanceBufferId = Shader.PropertyToID("_VegetationInstanceData");
        private static readonly int SlotPackedStartsId = Shader.PropertyToID("_VegetationSlotPackedStarts");
        private static readonly int SlotIndexId = Shader.PropertyToID("_VegetationSlotIndex");
        private readonly SlotResources[] slotResources;
        private readonly List<int> activeSlotIndices = new List<int>();
        private readonly VegetationCommandBufferIndirectDrawWrapper commandBufferDrawWrapper = new VegetationCommandBufferIndirectDrawWrapper();
        private readonly VegetationRasterCommandBufferIndirectDrawWrapper rasterCommandBufferDrawWrapper = new VegetationRasterCommandBufferIndirectDrawWrapper();
        private int lastDepthRenderCameraInstanceId = -1;
        private int lastDepthRenderUploadedSlotCount = -1;
        private int lastDepthRenderRenderedSlotCount = -1;
        private int lastColorRenderCameraInstanceId = -1;
        private int lastColorRenderUploadedSlotCount = -1;
        private int lastColorRenderRenderedSlotCount = -1;
        private int lastShadowRenderCameraInstanceId = -1;
        private int lastShadowRenderUploadedSlotCount = -1;
        private int lastShadowRenderRenderedSlotCount = -1;
        private GraphicsBuffer? lastBoundInstanceBuffer;
        private ComputeBuffer? lastBoundSlotPackedStartsBuffer;
        private GraphicsBuffer? gpuResidentArgsBuffer;
        private bool hasGpuResidentFrame;
        private bool disposed;

        public VegetationIndirectRenderer(VegetationRuntimeRegistry registry, int renderLayer)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            _ = renderLayer;
            SlotResources[] createdSlotResources = new SlotResources[registry.DrawSlots.Count];
            int createdSlotResourceCount = 0;
            try
            {
                for (int i = 0; i < createdSlotResources.Length; i++)
                {
                    createdSlotResources[i] = new SlotResources(
                        registry.DrawSlots[i],
                        registry.DrawSlotConservativeWorldBounds[i]);
                    createdSlotResourceCount++;
                }
            }
            catch
            {
                DisposeSlotResources(createdSlotResources, createdSlotResourceCount);
                throw;
            }

            slotResources = createdSlotResources;
        }

        public IReadOnlyList<int> ActiveSlotIndices => activeSlotIndices;

        public bool HasUploadedFrame => hasGpuResidentFrame;

        public int RegisteredDrawSlotCount => slotResources.Length;

        public int RuntimeMaterialCopyCount => 0;

        /// <summary>
        /// [INTEGRATION] Binds GPU-resident indirect resources prepared by the compute classification/decode path.
        /// </summary>
        public void BindGpuResidentFrame(GraphicsBuffer instanceBuffer, GraphicsBuffer argsBuffer, ComputeBuffer slotPackedStartsBuffer)
        {
            using (BindGpuResidentFrameMarker.Auto())
            {
                if (disposed || instanceBuffer == null || argsBuffer == null || slotPackedStartsBuffer == null)
                {
                    return;
                }

                lastBoundInstanceBuffer = instanceBuffer;
                lastBoundSlotPackedStartsBuffer = slotPackedStartsBuffer;
                gpuResidentArgsBuffer = argsBuffer;
                hasGpuResidentFrame = true;
                activeSlotIndices.Clear();
                for (int slotIndex = 0; slotIndex < slotResources.Length; slotIndex++)
                {
                    slotResources[slotIndex].BindSharedBuffers(instanceBuffer, slotPackedStartsBuffer);
                    activeSlotIndices.Add(slotIndex);
                }
            }
        }

        /// <summary>
        /// [INTEGRATION] Binds GPU-resident indirect resources and an explicit finalized active-slot list.
        /// </summary>
        public void BindGpuResidentFrame(
            GraphicsBuffer instanceBuffer,
            GraphicsBuffer argsBuffer,
            ComputeBuffer slotPackedStartsBuffer,
            IReadOnlyList<int> finalizedActiveSlotIndices)
        {
            using (BindGpuResidentFrameMarker.Auto())
            {
                if (disposed || instanceBuffer == null || argsBuffer == null || slotPackedStartsBuffer == null)
                {
                    return;
                }

                lastBoundInstanceBuffer = instanceBuffer;
                lastBoundSlotPackedStartsBuffer = slotPackedStartsBuffer;
                gpuResidentArgsBuffer = argsBuffer;
                hasGpuResidentFrame = true;
                activeSlotIndices.Clear();

                if (finalizedActiveSlotIndices == null)
                {
                    for (int slotIndex = 0; slotIndex < slotResources.Length; slotIndex++)
                    {
                        slotResources[slotIndex].BindSharedBuffers(instanceBuffer, slotPackedStartsBuffer);
                        activeSlotIndices.Add(slotIndex);
                    }

                    return;
                }

                for (int i = 0; i < finalizedActiveSlotIndices.Count; i++)
                {
                    int slotIndex = finalizedActiveSlotIndices[i];
                    if (slotIndex < 0 || slotIndex >= slotResources.Length)
                    {
                        continue;
                    }

                    slotResources[slotIndex].BindSharedBuffers(instanceBuffer, slotPackedStartsBuffer);
                    activeSlotIndices.Add(slotIndex);
                }
            }
        }

        /// <summary>
        /// [INTEGRATION] Compatibility overload retained while emitted-slot readback stays disabled on the render path.
        /// </summary>
        public void BindGpuResidentFrame(
            GraphicsBuffer instanceBuffer,
            GraphicsBuffer argsBuffer,
            ComputeBuffer slotPackedStartsBuffer,
            ComputeBuffer slotEmittedInstanceCountsBuffer)
        {
            _ = slotEmittedInstanceCountsBuffer;
            BindGpuResidentFrame(instanceBuffer, argsBuffer, slotPackedStartsBuffer);
        }

        /// <summary>
        /// [INTEGRATION] Renders the uploaded slot batches through raster command-buffer indirect draws for one camera/pass pair.
        /// </summary>
        internal void Render(IRasterCommandBuffer commandBuffer, Camera camera, VegetationRenderPassMode passMode, bool diagnosticsEnabled)
        {
            if (disposed || commandBuffer == null)
            {
                return;
            }

            rasterCommandBufferDrawWrapper.RefreshCommandBuffer(commandBuffer);
            try
            {
                RenderInternal(camera, passMode, rasterCommandBufferDrawWrapper, diagnosticsEnabled);
            }
            finally
            {
                rasterCommandBufferDrawWrapper.ClearCommandBuffer();
            }
        }

        /// <summary>
        /// [INTEGRATION] Renders the uploaded slot batches through compatibility command-buffer indirect draws for one camera/pass pair.
        /// </summary>
        internal void Render(CommandBuffer commandBuffer, Camera camera, VegetationRenderPassMode passMode, bool diagnosticsEnabled)
        {
            if (disposed || commandBuffer == null)
            {
                return;
            }

            commandBufferDrawWrapper.RefreshCommandBuffer(commandBuffer);
            try
            {
                RenderInternal(camera, passMode, commandBufferDrawWrapper, diagnosticsEnabled);
            }
            finally
            {
                commandBufferDrawWrapper.ClearCommandBuffer();
            }
        }

        private void RenderInternal(
            Camera camera,
            VegetationRenderPassMode passMode,
            IVegetationIndirectDrawWrapper drawWrapper,
            bool diagnosticsEnabled)
        {
            using (RenderMarker.Auto())
            {
                if (camera == null ||
                    drawWrapper == null ||
                    gpuResidentArgsBuffer == null)
                {
                    return;
                }

                GraphicsBuffer argsBuffer = gpuResidentArgsBuffer;
                int renderedSlotCount = 0;
                for (int activeSlotOffset = 0; activeSlotOffset < activeSlotIndices.Count; activeSlotOffset++)
                {
                    SlotResources slot = slotResources[activeSlotIndices[activeSlotOffset]];
                    if (!slot.TryResolveDrawSubmission(passMode, out Material material, out int shaderPass))
                    {
                        continue;
                    }

                    drawWrapper.DrawMeshInstancedIndirect(
                        slot.DrawSlot.Mesh,
                        material,
                        argsBuffer,
                        slot.ResolveArgsBufferOffset(),
                        shaderPass,
                        slot.DrawProperties);
                    renderedSlotCount++;
                }

                LogRenderDiagnostics(camera, passMode, renderedSlotCount, diagnosticsEnabled);
            }
        }

        /// <summary>
        /// [INTEGRATION] Collects the current uploaded draw-batch state for debug gizmos and EditMode verification.
        /// </summary>
        public void GetDebugSnapshots(List<VegetationIndirectDrawBatchSnapshot> target)
        {
            if (target == null)
            {
                return;
            }

            target.Clear();
            for (int activeSlotOffset = 0; activeSlotOffset < activeSlotIndices.Count; activeSlotOffset++)
            {
                int slotIndex = activeSlotIndices[activeSlotOffset];
                SlotResources slot = slotResources[slotIndex];
                target.Add(new VegetationIndirectDrawBatchSnapshot
                {
                    SlotIndex = slot.DrawSlot.SlotIndex,
                    DebugLabel = slot.DrawSlot.DebugLabel,
                    MaterialKind = slot.DrawSlot.MaterialKind,
                    InstanceCount = 0,
                    HasExactInstanceCount = false,
                    WorldBounds = slot.ConservativeWorldBounds
                });
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            for (int i = 0; i < slotResources.Length; i++)
            {
                slotResources[i].Dispose();
            }

            activeSlotIndices.Clear();
            lastBoundInstanceBuffer = null;
            lastBoundSlotPackedStartsBuffer = null;
            gpuResidentArgsBuffer = null;
            hasGpuResidentFrame = false;
            commandBufferDrawWrapper.ClearCommandBuffer();
            rasterCommandBufferDrawWrapper.ClearCommandBuffer();
        }

        private sealed class SlotResources
        {
            private const string ForwardLitPassName = "ForwardLit";
            private const string DepthOnlyPassName = "DepthOnly";
            private const string ShadowCasterPassName = "ShadowCaster";
            private readonly int forwardShaderPass;
            private readonly int depthShaderPass;
            private readonly int shadowShaderPass;
            private readonly MaterialPropertyBlock drawProperties = new MaterialPropertyBlock();

            public SlotResources(
                VegetationDrawSlot drawSlot,
                Bounds conservativeWorldBounds)
            {
                DrawSlot = drawSlot;
                ConservativeWorldBounds = conservativeWorldBounds;
                SharedArgsBufferOffset = checked(GraphicsBuffer.IndirectDrawIndexedArgs.size * drawSlot.SlotIndex);

                // Source materials are authoritative; renderer binds per-frame buffers globally and sets slot index per draw.
                if (!drawSlot.Material.enableInstancing)
                {
                    drawSlot.Material.enableInstancing = true;
                }

                drawProperties.SetInteger(SlotIndexId, drawSlot.SlotIndex);
                forwardShaderPass = drawSlot.Material.FindPass(ForwardLitPassName);
                depthShaderPass = drawSlot.Material.FindPass(DepthOnlyPassName);
                shadowShaderPass = drawSlot.Material.FindPass(ShadowCasterPassName);
            }

            public VegetationDrawSlot DrawSlot { get; }

            public Bounds ConservativeWorldBounds { get; }

            public int SharedArgsBufferOffset { get; }

            public MaterialPropertyBlock DrawProperties => drawProperties;

            public void BindSharedBuffers(GraphicsBuffer sharedInstanceBuffer, ComputeBuffer slotPackedStartsBuffer)
            {
                drawProperties.SetBuffer(InstanceBufferId, sharedInstanceBuffer);
                drawProperties.SetBuffer(SlotPackedStartsId, slotPackedStartsBuffer);
            }

            public bool TryResolveDrawSubmission(
                VegetationRenderPassMode passMode,
                out Material material,
                out int shaderPass)
            {
                material = DrawSlot.Material;

                if (passMode == VegetationRenderPassMode.Depth)
                {
                    shaderPass = depthShaderPass;
                    return shaderPass >= 0;
                }

                if (passMode == VegetationRenderPassMode.Shadow)
                {
                    shaderPass = shadowShaderPass;
                    return shaderPass >= 0;
                }

                shaderPass = forwardShaderPass >= 0 ? forwardShaderPass : 0;
                return true;
            }

            public int ResolveArgsBufferOffset()
            {
                return SharedArgsBufferOffset;
            }

            public void Dispose()
            {
            }
        }

        private void LogRenderDiagnostics(
            Camera camera,
            VegetationRenderPassMode passMode,
            int renderedSlotCount,
            bool diagnosticsEnabled)
        {
            if (!diagnosticsEnabled)
            {
                return;
            }

            int cameraInstanceId = camera.GetInstanceID();
            int uploadedSlotCount = activeSlotIndices.Count;
            if (passMode == VegetationRenderPassMode.Depth)
            {
                if (cameraInstanceId == lastDepthRenderCameraInstanceId &&
                    uploadedSlotCount == lastDepthRenderUploadedSlotCount &&
                    renderedSlotCount == lastDepthRenderRenderedSlotCount)
                {
                    return;
                }

                lastDepthRenderCameraInstanceId = cameraInstanceId;
                lastDepthRenderUploadedSlotCount = uploadedSlotCount;
                lastDepthRenderRenderedSlotCount = renderedSlotCount;
            }
            else if (passMode == VegetationRenderPassMode.Color)
            {
                if (cameraInstanceId == lastColorRenderCameraInstanceId &&
                    uploadedSlotCount == lastColorRenderUploadedSlotCount &&
                    renderedSlotCount == lastColorRenderRenderedSlotCount)
                {
                    return;
                }

                lastColorRenderCameraInstanceId = cameraInstanceId;
                lastColorRenderUploadedSlotCount = uploadedSlotCount;
                lastColorRenderRenderedSlotCount = renderedSlotCount;
            }
            else
            {
                if (cameraInstanceId == lastShadowRenderCameraInstanceId &&
                    uploadedSlotCount == lastShadowRenderUploadedSlotCount &&
                    renderedSlotCount == lastShadowRenderRenderedSlotCount)
                {
                    return;
                }

                lastShadowRenderCameraInstanceId = cameraInstanceId;
                lastShadowRenderUploadedSlotCount = uploadedSlotCount;
                lastShadowRenderRenderedSlotCount = renderedSlotCount;
            }

            StringBuilder builder = new StringBuilder(256);
            int slotsToLog = Mathf.Min(uploadedSlotCount, 6);
            for (int activeSlotOffset = 0; activeSlotOffset < slotsToLog; activeSlotOffset++)
            {
                SlotResources slot = slotResources[activeSlotIndices[activeSlotOffset]];
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(slot.DrawSlot.DebugLabel);
            }

            string summary =
                $"VegetationIndirectRenderer render camera={camera.name} pass={passMode} uploadedSlots={uploadedSlotCount} renderedSlots={renderedSlotCount} renderedInstances=unknown slots=[{builder}]";

            if (renderedSlotCount == 0)
            {
                UnityEngine.Debug.LogWarning(summary);
            }
            else
            {
                UnityEngine.Debug.Log(summary);
            }
        }

        private static void DisposeSlotResources(SlotResources[] slotResources, int slotResourceCount)
        {
            _ = slotResources;
            _ = slotResourceCount;
        }
    }
}
