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
    /// [INTEGRATION] Owns draw-slot submission metadata, indirect args, and final draw submission.
    /// Current shipped safety path filters active slots from the emitted-count buffer before final submission.
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
        private uint[] slotEmittedCountsReadback = Array.Empty<uint>();
        private int lastDepthRenderCameraInstanceId = -1;
        private int lastDepthRenderUploadedSlotCount = -1;
        private int lastDepthRenderRenderedSlotCount = -1;
        private int lastColorRenderCameraInstanceId = -1;
        private int lastColorRenderUploadedSlotCount = -1;
        private int lastColorRenderRenderedSlotCount = -1;
        private int lastShadowRenderCameraInstanceId = -1;
        private int lastShadowRenderUploadedSlotCount = -1;
        private int lastShadowRenderRenderedSlotCount = -1;
        private GraphicsBuffer? gpuResidentArgsBuffer;
        private GraphicsBuffer? lastBoundInstanceBuffer;
        private ComputeBuffer? lastBoundSlotPackedStartsBuffer;
        private long lastBoundSubmittedInstanceCount;
        private bool hasGpuResidentFrame;
        private bool disposed;

        public VegetationIndirectRenderer(VegetationRuntimeRegistry registry, int renderLayer)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            _ = renderLayer;
            slotResources = new SlotResources[registry.DrawSlots.Count];
            for (int i = 0; i < slotResources.Length; i++)
            {
                slotResources[i] = new SlotResources(
                    registry.DrawSlots[i],
                    registry.DrawSlotConservativeWorldBounds[i]);
            }
        }

        public IReadOnlyList<int> ActiveSlotIndices => activeSlotIndices;

        public bool HasUploadedFrame => hasGpuResidentFrame;

        public int RegisteredDrawSlotCount => slotResources.Length;

        public int RuntimeMaterialCopyCount => 0;

        /// <summary>
        /// [INTEGRATION] Binds GPU-resident indirect resources prepared by the compute classification/decode path.
        /// One args record exists per draw slot. Shared GPU buffers are rebound per draw through command-buffer global state.
        /// </summary>
        public void BindGpuResidentFrame(
            GraphicsBuffer instanceBuffer,
            GraphicsBuffer argsBuffer,
            ComputeBuffer slotPackedStartsBuffer,
            ComputeBuffer slotEmittedInstanceCountsBuffer)
        {
            using (BindGpuResidentFrameMarker.Auto())
            {
                if (disposed)
                {
                    return;
                }

                if (instanceBuffer == null)
                {
                   return;
                }

                if (argsBuffer == null)
                {
                    return;
                }

                if (slotPackedStartsBuffer == null)
                {
                    return;
                }

                if (slotEmittedInstanceCountsBuffer == null)
                {
                    return;
                }

                RefreshActiveSlots(slotEmittedInstanceCountsBuffer);
                if (activeSlotIndices.Count == 0)
                {
                    gpuResidentArgsBuffer = null;
                    hasGpuResidentFrame = false;
                    lastBoundInstanceBuffer = null;
                    lastBoundSlotPackedStartsBuffer = null;
                    return;
                }

                gpuResidentArgsBuffer = argsBuffer;
                hasGpuResidentFrame = true;

                lastBoundInstanceBuffer = instanceBuffer;
                lastBoundSlotPackedStartsBuffer = slotPackedStartsBuffer;
            }
        }

        /// <summary>
        /// [INTEGRATION] Renders the uploaded slot batches through raster command-buffer indirect draws for one camera/pass pair.
        /// </summary>
        internal void Render(IRasterCommandBuffer commandBuffer, Camera camera, VegetationRenderPassMode passMode, bool diagnosticsEnabled)
        {
            if (disposed)
            {
                return;
            }

            if (commandBuffer == null)
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
            if (disposed)
            {
                return;
            }

            if (commandBuffer == null)
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
                if (camera == null)
                {
                    return;
                }

                if (drawWrapper == null)
                {
                    return;
                }

                if(gpuResidentArgsBuffer == null)
                {
                    return;
                }

                if (lastBoundInstanceBuffer == null || lastBoundSlotPackedStartsBuffer == null)
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

                    drawWrapper.SetGlobalBuffer(InstanceBufferId, lastBoundInstanceBuffer);
                    drawWrapper.SetGlobalBuffer(SlotPackedStartsId, lastBoundSlotPackedStartsBuffer);
                    drawWrapper.SetGlobalInt(SlotIndexId, slot.DrawSlot.SlotIndex);
                    drawWrapper.DrawMeshInstancedIndirect(
                        slot.DrawSlot.Mesh,
                        material,
                        argsBuffer,
                        slot.ResolveArgsBufferOffset(),
                        shaderPass);
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
            activeSlotIndices.Clear();
            slotEmittedCountsReadback = Array.Empty<uint>();
            lastBoundSubmittedInstanceCount = 0L;
            gpuResidentArgsBuffer = null;
            hasGpuResidentFrame = false;
            lastBoundInstanceBuffer = null;
            lastBoundSlotPackedStartsBuffer = null;
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

            public SlotResources(
                VegetationDrawSlot drawSlot,
                Bounds conservativeWorldBounds)
            {
                DrawSlot = drawSlot;
                ConservativeWorldBounds = conservativeWorldBounds;
                SharedArgsBufferOffset = checked(GraphicsBuffer.IndirectDrawIndexedArgs.size * drawSlot.SlotIndex);
                forwardShaderPass = drawSlot.Material.FindPass(ForwardLitPassName);
                depthShaderPass = drawSlot.Material.FindPass(DepthOnlyPassName);
                shadowShaderPass = drawSlot.Material.FindPass(ShadowCasterPassName);
            }

            public VegetationDrawSlot DrawSlot { get; }

            public Bounds ConservativeWorldBounds { get; }

            public int SharedArgsBufferOffset { get; }

            public bool TryResolveDrawSubmission(
                VegetationRenderPassMode passMode,
                out Material material,
                out int shaderPass)
            {
                if (passMode == VegetationRenderPassMode.Depth)
                {
                    material = DrawSlot.Material;
                    shaderPass = depthShaderPass;
                    return shaderPass >= 0;
                }

                material = DrawSlot.Material;
                shaderPass = passMode == VegetationRenderPassMode.Shadow
                    ? shadowShaderPass
                    : forwardShaderPass;
                return shaderPass >= 0;
            }

            public int ResolveArgsBufferOffset()
            {
                return SharedArgsBufferOffset;
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
            bool isDepthPass = passMode == VegetationRenderPassMode.Depth;
            if (isDepthPass)
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
            int slotsToLog = Mathf.Min(uploadedSlotCount, 4);
            for (int activeSlotOffset = 0; activeSlotOffset < slotsToLog; activeSlotOffset++)
            {
                SlotResources slot = slotResources[activeSlotIndices[activeSlotOffset]];
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                uint emittedInstanceCount = slotEmittedCountsReadback[slot.DrawSlot.SlotIndex];
                slot.TryResolveDrawSubmission(passMode, out Material material, out int shaderPass);
                builder.Append(slot.DrawSlot.DebugLabel);
                builder.Append("{mesh=");
                builder.Append(slot.DrawSlot.Mesh.name);
                builder.Append(",kind=");
                builder.Append(slot.DrawSlot.MaterialKind);
                builder.Append(",instances=");
                builder.Append(emittedInstanceCount);
                builder.Append(",indexCount=");
                builder.Append(slot.DrawSlot.IndexCountPerInstance);
                builder.Append(",startIndex=");
                builder.Append(slot.DrawSlot.StartIndexLocation);
                builder.Append(",baseVertex=");
                builder.Append(slot.DrawSlot.BaseVertexLocation);
                builder.Append(",vertices=");
                builder.Append(slot.DrawSlot.Mesh.vertexCount);
                builder.Append(",subMeshes=");
                builder.Append(slot.DrawSlot.Mesh.subMeshCount);
                builder.Append(",topology=");
                builder.Append(slot.DrawSlot.Mesh.GetTopology(0));
                builder.Append(",indexFormat=");
                builder.Append(slot.DrawSlot.Mesh.indexFormat);
                builder.Append(",shaderPass=");
                builder.Append(shaderPass);
                builder.Append(",shader=");
                builder.Append(material != null ? material.shader.name : "missing");
                builder.Append(",sourceMaterial=");
                builder.Append(slot.DrawSlot.Material.name);
                builder.Append('}');
            }

            string summary =
                $"VegetationIndirectRenderer render camera={camera.name} pass={passMode} uploadedSlots={uploadedSlotCount} renderedSlots={renderedSlotCount} renderedInstances={lastBoundSubmittedInstanceCount} slots=[{builder}]";

            if (renderedSlotCount == 0)
            {
                UnityEngine.Debug.LogWarning(summary);
            }
            else
            {
                UnityEngine.Debug.Log(summary);
            }
        }

        private void RefreshActiveSlots(ComputeBuffer slotEmittedInstanceCountsBuffer)
        {
            EnsureSlotEmittedCountsReadbackCapacity(slotResources.Length);
            activeSlotIndices.Clear();
            lastBoundSubmittedInstanceCount = 0L;
            if (slotResources.Length <= 0)
            {
                return;
            }

            slotEmittedInstanceCountsBuffer.GetData(slotEmittedCountsReadback, 0, 0, slotResources.Length);
            for (int slotIndex = 0; slotIndex < slotResources.Length; slotIndex++)
            {
                uint emittedInstanceCount = slotEmittedCountsReadback[slotIndex];
                if (emittedInstanceCount == 0u)
                {
                    continue;
                }

                activeSlotIndices.Add(slotIndex);
                lastBoundSubmittedInstanceCount += emittedInstanceCount;
            }
        }

        private void EnsureSlotEmittedCountsReadbackCapacity(int requiredSlotCount)
        {
            if (slotEmittedCountsReadback.Length >= requiredSlotCount)
            {
                return;
            }

            slotEmittedCountsReadback = new uint[requiredSlotCount];
        }

    }
}
