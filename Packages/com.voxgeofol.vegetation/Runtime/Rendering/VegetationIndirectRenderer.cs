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
        private readonly int[] allRegisteredSlotIndices;
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
            allRegisteredSlotIndices = BuildAllRegisteredSlotIndices(createdSlotResources.Length);
        }

        public int RegisteredDrawSlotCount => slotResources.Length;

        public int RuntimeMaterialCopyCount => 0;

        /// <summary>
        /// [INTEGRATION] Immutable prepared-view handle that owns one prepared renderer binding surface.
        /// </summary>
        public sealed class PreparedViewHandle
        {
            private readonly VegetationIndirectRenderer owner;
            private readonly IReadOnlyList<int> activeSlotIndices;

            internal PreparedViewHandle(
                VegetationIndirectRenderer owner,
                GraphicsBuffer instanceBuffer,
                GraphicsBuffer argsBuffer,
                ComputeBuffer slotPackedStartsBuffer,
                IReadOnlyList<int>? finalizedActiveSlotIndices)
            {
                this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
                InstanceBuffer = instanceBuffer ?? throw new ArgumentNullException(nameof(instanceBuffer));
                ArgsBuffer = argsBuffer ?? throw new ArgumentNullException(nameof(argsBuffer));
                SlotPackedStartsBuffer = slotPackedStartsBuffer ?? throw new ArgumentNullException(nameof(slotPackedStartsBuffer));
                activeSlotIndices = finalizedActiveSlotIndices == null
                    ? owner.allRegisteredSlotIndices
                    : owner.CopyValidActiveSlotIndices(finalizedActiveSlotIndices);
                UsesRegisteredSlotFallback = finalizedActiveSlotIndices == null;
                HasUploadedFrame = true;
            }

            public GraphicsBuffer InstanceBuffer { get; }

            public GraphicsBuffer ArgsBuffer { get; }

            public ComputeBuffer SlotPackedStartsBuffer { get; }

            public IReadOnlyList<int> ActiveSlotIndices => activeSlotIndices;

            public bool UsesRegisteredSlotFallback { get; }

            public bool HasUploadedFrame { get; }

            internal bool IsOwnedBy(VegetationIndirectRenderer renderer)
            {
                return ReferenceEquals(owner, renderer);
            }
        }

        /// <summary>
        /// [INTEGRATION] Creates a prepared-view handle for GPU-resident indirect resources prepared by the compute classification/decode path.
        /// </summary>
        public PreparedViewHandle? BindGpuResidentFrame(GraphicsBuffer instanceBuffer, GraphicsBuffer argsBuffer, ComputeBuffer slotPackedStartsBuffer)
        {
            using (BindGpuResidentFrameMarker.Auto())
            {
                if (disposed || instanceBuffer == null || argsBuffer == null || slotPackedStartsBuffer == null)
                {
                    return null;
                }

                return new PreparedViewHandle(this, instanceBuffer, argsBuffer, slotPackedStartsBuffer, null);
            }
        }

        /// <summary>
        /// [INTEGRATION] Creates a prepared-view handle for GPU-resident indirect resources and an explicit finalized active-slot list.
        /// </summary>
        public PreparedViewHandle? BindGpuResidentFrame(
            GraphicsBuffer instanceBuffer,
            GraphicsBuffer argsBuffer,
            ComputeBuffer slotPackedStartsBuffer,
            IReadOnlyList<int> finalizedActiveSlotIndices)
        {
            using (BindGpuResidentFrameMarker.Auto())
            {
                if (disposed || instanceBuffer == null || argsBuffer == null || slotPackedStartsBuffer == null)
                {
                    return null;
                }

                return new PreparedViewHandle(this, instanceBuffer, argsBuffer, slotPackedStartsBuffer, finalizedActiveSlotIndices);
            }
        }

        /// <summary>
        /// [INTEGRATION] Compatibility overload retained while emitted-slot readback stays disabled on the render path.
        /// </summary>
        public PreparedViewHandle? BindGpuResidentFrame(
            GraphicsBuffer instanceBuffer,
            GraphicsBuffer argsBuffer,
            ComputeBuffer slotPackedStartsBuffer,
            ComputeBuffer slotEmittedInstanceCountsBuffer)
        {
            _ = slotEmittedInstanceCountsBuffer;
            return BindGpuResidentFrame(instanceBuffer, argsBuffer, slotPackedStartsBuffer);
        }

        /// <summary>
        /// [INTEGRATION] Renders the uploaded slot batches through raster command-buffer indirect draws for one camera/pass pair.
        /// </summary>
        internal void Render(
            IRasterCommandBuffer commandBuffer,
            Camera camera,
            PreparedViewHandle preparedView,
            VegetationRenderPassMode passMode,
            bool diagnosticsEnabled)
        {
            if (disposed || commandBuffer == null)
            {
                return;
            }

            rasterCommandBufferDrawWrapper.RefreshCommandBuffer(commandBuffer);
            try
            {
                RenderInternal(camera, passMode, preparedView, rasterCommandBufferDrawWrapper, diagnosticsEnabled);
            }
            finally
            {
                rasterCommandBufferDrawWrapper.ClearCommandBuffer();
            }
        }

        /// <summary>
        /// [INTEGRATION] Renders the uploaded slot batches through compatibility command-buffer indirect draws for one camera/pass pair.
        /// </summary>
        internal void Render(
            CommandBuffer commandBuffer,
            Camera camera,
            PreparedViewHandle preparedView,
            VegetationRenderPassMode passMode,
            bool diagnosticsEnabled)
        {
            if (disposed || commandBuffer == null)
            {
                return;
            }

            commandBufferDrawWrapper.RefreshCommandBuffer(commandBuffer);
            try
            {
                RenderInternal(camera, passMode, preparedView, commandBufferDrawWrapper, diagnosticsEnabled);
            }
            finally
            {
                commandBufferDrawWrapper.ClearCommandBuffer();
            }
        }

        private void RenderInternal(
            Camera camera,
            VegetationRenderPassMode passMode,
            PreparedViewHandle preparedView,
            IVegetationIndirectDrawWrapper drawWrapper,
            bool diagnosticsEnabled)
        {
            using (RenderMarker.Auto())
            {
                if (camera == null ||
                    drawWrapper == null ||
                    preparedView == null ||
                    !preparedView.IsOwnedBy(this) ||
                    !preparedView.HasUploadedFrame)
                {
                    return;
                }

                GraphicsBuffer argsBuffer = preparedView.ArgsBuffer;
                drawWrapper.SetGlobalBuffer(InstanceBufferId, preparedView.InstanceBuffer);
                drawWrapper.SetGlobalBuffer(SlotPackedStartsId, preparedView.SlotPackedStartsBuffer);
                int renderedSlotCount = 0;
                for (int activeSlotOffset = 0; activeSlotOffset < preparedView.ActiveSlotIndices.Count; activeSlotOffset++)
                {
                    int slotIndex = preparedView.ActiveSlotIndices[activeSlotOffset];
                    if (slotIndex < 0 || slotIndex >= slotResources.Length)
                    {
                        continue;
                    }

                    SlotResources slot = slotResources[slotIndex];
                    if (!slot.TryResolveDrawSubmission(passMode, out Material material, out int shaderPass))
                    {
                        continue;
                    }

                    drawWrapper.SetGlobalInt(SlotIndexId, slot.DrawSlot.SlotIndex);
                    drawWrapper.DrawMeshInstancedIndirect(
                        slot.DrawSlot.Mesh,
                        material,
                        argsBuffer,
                        slot.ResolveArgsBufferOffset(),
                        shaderPass,
                        null);
                    renderedSlotCount++;
                }

                LogRenderDiagnostics(camera, passMode, preparedView, renderedSlotCount, diagnosticsEnabled);
            }
        }

        /// <summary>
        /// [INTEGRATION] Collects the current uploaded draw-batch state for debug gizmos and EditMode verification.
        /// </summary>
        public void GetDebugSnapshots(PreparedViewHandle preparedView, List<VegetationIndirectDrawBatchSnapshot> target)
        {
            if (target == null)
            {
                return;
            }

            target.Clear();
            if (preparedView == null || !preparedView.IsOwnedBy(this) || !preparedView.HasUploadedFrame)
            {
                return;
            }

            for (int activeSlotOffset = 0; activeSlotOffset < preparedView.ActiveSlotIndices.Count; activeSlotOffset++)
            {
                int slotIndex = preparedView.ActiveSlotIndices[activeSlotOffset];
                if (slotIndex < 0 || slotIndex >= slotResources.Length)
                {
                    continue;
                }

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

            commandBufferDrawWrapper.ClearCommandBuffer();
            rasterCommandBufferDrawWrapper.ClearCommandBuffer();
        }

        private int[] CopyValidActiveSlotIndices(IReadOnlyList<int> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<int>();
            }

            int validCount = 0;
            for (int i = 0; i < source.Count; i++)
            {
                int slotIndex = source[i];
                if (slotIndex >= 0 && slotIndex < slotResources.Length)
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                return Array.Empty<int>();
            }

            int[] result = new int[validCount];
            int writeIndex = 0;
            for (int i = 0; i < source.Count; i++)
            {
                int slotIndex = source[i];
                if (slotIndex < 0 || slotIndex >= slotResources.Length)
                {
                    continue;
                }

                result[writeIndex] = slotIndex;
                writeIndex++;
            }

            return result;
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

                // Source materials are authoritative; renderer binds per-frame buffers globally and sets slot index per draw.
                if (!drawSlot.Material.enableInstancing)
                {
                    drawSlot.Material.enableInstancing = true;
                }

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
            PreparedViewHandle preparedView,
            int renderedSlotCount,
            bool diagnosticsEnabled)
        {
            if (!diagnosticsEnabled)
            {
                return;
            }

            if (preparedView == null)
            {
                return;
            }

            IReadOnlyList<int> activeSlotIndices = preparedView.ActiveSlotIndices;
            int uploadedSlotCount = activeSlotIndices.Count;
            int cameraInstanceId = camera.GetInstanceID();
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
                int slotIndex = activeSlotIndices[activeSlotOffset];
                if (slotIndex < 0 || slotIndex >= slotResources.Length)
                {
                    continue;
                }

                SlotResources slot = slotResources[slotIndex];
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(slot.DrawSlot.DebugLabel);
            }

            string summary =
                $"VegetationIndirectRenderer render camera={camera.name} pass={passMode} uploadedSlots={uploadedSlotCount} renderedSlots={renderedSlotCount} activeSlotSurface={(preparedView.UsesRegisteredSlotFallback ? "registered-slot-fallback" : "latest-async-emitted-slots")} renderedInstances=unknown slots=[{builder}]";

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

        private static int[] BuildAllRegisteredSlotIndices(int count)
        {
            int[] indices = new int[Mathf.Max(0, count)];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            return indices;
        }
    }
}
