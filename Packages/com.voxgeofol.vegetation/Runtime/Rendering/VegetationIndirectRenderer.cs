#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Rendering;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] Owns Phase E per-slot instance buffers, indirect args, runtime materials, and draw submission.
    /// </summary>
    public sealed class VegetationIndirectRenderer : IDisposable
    {
        private static readonly ProfilerMarker BindGpuResidentFrameMarker = new ProfilerMarker("VoxGeoFol.VegetationIndirectRenderer.BindGpuResidentFrame");
        private static readonly ProfilerMarker RenderMarker = new ProfilerMarker("VoxGeoFol.VegetationIndirectRenderer.Render");
        private readonly SlotResources[] slotResources;
        private readonly List<int> activeSlotIndices = new List<int>();
        private string lastDepthRenderDiagnostics = string.Empty;
        private string lastColorRenderDiagnostics = string.Empty;
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
            slotResources = new SlotResources[registry.DrawSlots.Count];
            int sharedStartInstance = 0;
            for (int i = 0; i < slotResources.Length; i++)
            {
                slotResources[i] = new SlotResources(
                    registry.DrawSlots[i],
                    registry.DrawSlotMaxInstanceCounts[i],
                    registry.DrawSlotConservativeWorldBounds[i],
                    sharedStartInstance);
                sharedStartInstance += registry.DrawSlotMaxInstanceCounts[i];
            }
        }

        public IReadOnlyList<int> ActiveSlotIndices => activeSlotIndices;

        public bool HasUploadedFrame => hasGpuResidentFrame;

        /// <summary>
        /// [INTEGRATION] Binds GPU-resident indirect resources prepared by the compute classification/decode path.
        /// </summary>
        public void BindGpuResidentFrame(GraphicsBuffer instanceBuffer, GraphicsBuffer argsBuffer)
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

                gpuResidentArgsBuffer = argsBuffer;
                hasGpuResidentFrame = true;
                activeSlotIndices.Clear();
                for (int slotIndex = 0; slotIndex < slotResources.Length; slotIndex++)
                {
                    SlotResources slot = slotResources[slotIndex];
                    if (slot.MaxInstanceCapacity <= 0)
                    {
                        continue;
                    }

                    slot.BindSharedInstanceBuffer(instanceBuffer);
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
                SlotResources slot = slotResources[activeSlotIndices[activeSlotOffset]];
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
        }

        private GraphicsBuffer ResolveArgsBuffer(SlotResources slot)
        {
            return gpuResidentArgsBuffer ?? throw new InvalidOperationException("GPU-resident args buffer has not been bound.");
        }

        private sealed class SlotResources : IDisposable
        {
            private static readonly int InstanceBufferId = Shader.PropertyToID("_VegetationInstanceData");
            private static readonly int InstanceStartId = Shader.PropertyToID("_VegetationInstanceStart");

            public SlotResources(
                VegetationDrawSlot drawSlot,
                int maxInstanceCapacity,
                Bounds conservativeWorldBounds,
                int sharedStartInstance)
            {
                DrawSlot = drawSlot;
                MaxInstanceCapacity = maxInstanceCapacity;
                ConservativeWorldBounds = conservativeWorldBounds;
                SharedStartInstance = checked((uint)Mathf.Max(0, sharedStartInstance));
                SharedArgsBufferOffset = checked(GraphicsBuffer.IndirectDrawIndexedArgs.size * drawSlot.SlotIndex);
                ColorMaterial = VegetationIndirectMaterialFactory.CreateColorMaterial(drawSlot);
                DepthMaterial = VegetationIndirectMaterialFactory.CreateDepthMaterial(drawSlot);
                ColorMaterial.SetInteger(InstanceStartId, 0);
                DepthMaterial.SetInteger(InstanceStartId, 0);
            }

            public VegetationDrawSlot DrawSlot { get; }

            public int MaxInstanceCapacity { get; }

            public Bounds ConservativeWorldBounds { get; }

            public Material ColorMaterial { get; }

            public Material DepthMaterial { get; }

            public int SharedArgsBufferOffset { get; }

            public uint SharedStartInstance { get; }

            public void BindSharedInstanceBuffer(GraphicsBuffer sharedInstanceBuffer)
            {
                ColorMaterial.SetBuffer(InstanceBufferId, sharedInstanceBuffer);
                DepthMaterial.SetBuffer(InstanceBufferId, sharedInstanceBuffer);
                ColorMaterial.SetInteger(InstanceStartId, checked((int)SharedStartInstance));
                DepthMaterial.SetInteger(InstanceStartId, checked((int)SharedStartInstance));
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

            StringBuilder builder = new StringBuilder(256);
            int slotsToLog = Mathf.Min(activeSlotIndices.Count, 6);
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
                $"VegetationIndirectRenderer render camera={camera.name} pass={passMode} uploadedSlots={activeSlotIndices.Count} renderedSlots={renderedSlotCount} renderedInstances=unknown slots=[{builder}]";
            bool isDepthPass = passMode == VegetationRenderPassMode.Depth;
            string previousSummary = isDepthPass ? lastDepthRenderDiagnostics : lastColorRenderDiagnostics;
            if (summary == previousSummary)
            {
                return;
            }

            if (isDepthPass)
            {
                lastDepthRenderDiagnostics = summary;
            }
            else
            {
                lastColorRenderDiagnostics = summary;
            }

            if (renderedSlotCount == 0)
            {
                UnityEngine.Debug.LogWarning(summary);
            }
            else
            {
                UnityEngine.Debug.Log(summary);
            }
        }
    }
}
