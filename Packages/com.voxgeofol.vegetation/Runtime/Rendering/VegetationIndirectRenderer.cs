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
    /// [INTEGRATION] Owns draw-slot-scoped runtime materials, indirect args, and final draw submission.
    /// The urgent path compacts submission down to draw slots with non-zero emitted instance counts only.
    /// </summary>
    public sealed class VegetationIndirectRenderer : IDisposable
    {
        private static readonly ProfilerMarker BindGpuResidentFrameMarker = new ProfilerMarker("VoxGeoFol.VegetationIndirectRenderer.BindGpuResidentFrame");
        private static readonly ProfilerMarker RenderMarker = new ProfilerMarker("VoxGeoFol.VegetationIndirectRenderer.Render");
        private readonly SlotResources[] slotResources;
        private readonly List<int> activeSlotIndices = new List<int>();
        private uint[] slotEmittedInstanceCounts = Array.Empty<uint>();
        private int lastDepthRenderCameraInstanceId = -1;
        private int lastDepthRenderUploadedSlotCount = -1;
        private int lastDepthRenderRenderedSlotCount = -1;
        private int lastColorRenderCameraInstanceId = -1;
        private int lastColorRenderUploadedSlotCount = -1;
        private int lastColorRenderRenderedSlotCount = -1;
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

        /// <summary>
        /// [INTEGRATION] Binds GPU-resident indirect resources prepared by the compute classification/decode path.
        /// One args record exists per draw slot, but only non-zero emitted slots stay active for final submission.
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
                    throw new ObjectDisposedException(nameof(VegetationIndirectRenderer));
                }

                if (instanceBuffer == null)
                {
                    throw new ArgumentNullException(nameof(instanceBuffer));
                }

                if (argsBuffer == null)
                {
                    throw new ArgumentNullException(nameof(argsBuffer));
                }

                if (slotPackedStartsBuffer == null)
                {
                    throw new ArgumentNullException(nameof(slotPackedStartsBuffer));
                }

                if (slotEmittedInstanceCountsBuffer == null)
                {
                    throw new ArgumentNullException(nameof(slotEmittedInstanceCountsBuffer));
                }

                gpuResidentArgsBuffer = argsBuffer;
                hasGpuResidentFrame = true;
                activeSlotIndices.Clear();
                EnsureSlotCountReadbackCapacity(slotResources.Length);
                if (slotResources.Length > 0)
                {
                    slotEmittedInstanceCountsBuffer.GetData(slotEmittedInstanceCounts, 0, 0, slotResources.Length);
                }

                for (int slotIndex = 0; slotIndex < slotResources.Length; slotIndex++)
                {
                    uint emittedInstanceCount = slotEmittedInstanceCounts[slotIndex];
                    if (emittedInstanceCount == 0u)
                    {
                        continue;
                    }

                    SlotResources slot = slotResources[slotIndex];
                    slot.BindSharedBuffers(instanceBuffer, slotPackedStartsBuffer);
                    activeSlotIndices.Add(slotIndex);
                }
            }
        }

        /// <summary>
        /// [INTEGRATION] Renders the uploaded slot batches through raster command-buffer indirect draws for one camera/pass pair.
        /// </summary>
        internal void Render(IRasterCommandBuffer commandBuffer, Camera camera, VegetationRenderPassMode passMode, bool diagnosticsEnabled)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(VegetationIndirectRenderer));
            }

            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            RenderInternal(camera, passMode, (slot, material) =>
            {
                commandBuffer.DrawMeshInstancedIndirect(
                    slot.DrawSlot.Mesh,
                    0,
                    material,
                    0,
                    ResolveArgsBuffer(slot),
                    slot.ResolveArgsBufferOffset());
            }, diagnosticsEnabled);
        }

        /// <summary>
        /// [INTEGRATION] Renders the uploaded slot batches through compatibility command-buffer indirect draws for one camera/pass pair.
        /// </summary>
        internal void Render(CommandBuffer commandBuffer, Camera camera, VegetationRenderPassMode passMode, bool diagnosticsEnabled)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(VegetationIndirectRenderer));
            }

            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            RenderInternal(camera, passMode, (slot, material) =>
            {
                commandBuffer.DrawMeshInstancedIndirect(
                    slot.DrawSlot.Mesh,
                    0,
                    material,
                    0,
                    ResolveArgsBuffer(slot),
                    slot.ResolveArgsBufferOffset());
            }, diagnosticsEnabled);
        }

        private void RenderInternal(
            Camera camera,
            VegetationRenderPassMode passMode,
            Action<SlotResources, Material> issueDraw,
            bool diagnosticsEnabled)
        {
            using (RenderMarker.Auto())
            {
                if (camera == null)
                {
                    throw new ArgumentNullException(nameof(camera));
                }

                int renderedSlotCount = 0;
                for (int activeSlotOffset = 0; activeSlotOffset < activeSlotIndices.Count; activeSlotOffset++)
                {
                    SlotResources slot = slotResources[activeSlotIndices[activeSlotOffset]];
                    Material material = passMode == VegetationRenderPassMode.Depth ? slot.DepthMaterial : slot.ColorMaterial;
                    issueDraw(slot, material);
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
                throw new ArgumentNullException(nameof(target));
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
                    InstanceCount = checked((int)slotEmittedInstanceCounts[slotIndex]),
                    HasExactInstanceCount = hasGpuResidentFrame,
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
        }

        private GraphicsBuffer ResolveArgsBuffer(SlotResources slot)
        {
            return gpuResidentArgsBuffer ?? throw new InvalidOperationException("GPU-resident args buffer has not been bound.");
        }

        private void EnsureSlotCountReadbackCapacity(int requiredSlotCount)
        {
            if (slotEmittedInstanceCounts.Length >= requiredSlotCount)
            {
                return;
            }

            slotEmittedInstanceCounts = new uint[requiredSlotCount];
        }

        private sealed class SlotResources : IDisposable
        {
            private static readonly int InstanceBufferId = Shader.PropertyToID("_VegetationInstanceData");
            private static readonly int SlotPackedStartsId = Shader.PropertyToID("_VegetationSlotPackedStarts");
            private static readonly int SlotIndexId = Shader.PropertyToID("_VegetationSlotIndex");

            public SlotResources(
                VegetationDrawSlot drawSlot,
                Bounds conservativeWorldBounds)
            {
                DrawSlot = drawSlot;
                ConservativeWorldBounds = conservativeWorldBounds;
                SharedArgsBufferOffset = checked(GraphicsBuffer.IndirectDrawIndexedArgs.size * drawSlot.SlotIndex);
                ColorMaterial = VegetationIndirectMaterialFactory.CreateColorMaterial(drawSlot);
                DepthMaterial = VegetationIndirectMaterialFactory.CreateDepthMaterial(drawSlot);
                ColorMaterial.SetInteger(SlotIndexId, drawSlot.SlotIndex);
                DepthMaterial.SetInteger(SlotIndexId, drawSlot.SlotIndex);
            }

            public VegetationDrawSlot DrawSlot { get; }

            public Bounds ConservativeWorldBounds { get; }

            public Material ColorMaterial { get; }

            public Material DepthMaterial { get; }

            public int SharedArgsBufferOffset { get; }

            public void BindSharedBuffers(GraphicsBuffer sharedInstanceBuffer, ComputeBuffer slotPackedStartsBuffer)
            {
                ColorMaterial.SetBuffer(InstanceBufferId, sharedInstanceBuffer);
                DepthMaterial.SetBuffer(InstanceBufferId, sharedInstanceBuffer);
                ColorMaterial.SetBuffer(SlotPackedStartsId, slotPackedStartsBuffer);
                DepthMaterial.SetBuffer(SlotPackedStartsId, slotPackedStartsBuffer);
            }

            public void Dispose()
            {
                VegetationIndirectMaterialFactory.DestroyRuntimeMaterial(ColorMaterial);
                VegetationIndirectMaterialFactory.DestroyRuntimeMaterial(DepthMaterial);
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
            else
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
            for (int i = slotResourceCount - 1; i >= 0; i--)
            {
                slotResources[i]?.Dispose();
            }
        }
    }
}
