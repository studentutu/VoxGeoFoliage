#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] Owns Phase E per-slot instance buffers, indirect args, runtime materials, and draw submission.
    /// </summary>
    public sealed class VegetationIndirectRenderer : IDisposable
    {
        private readonly bool diagnosticsEnabled;
        private readonly SlotResources[] slotResources;
        private readonly List<int> activeSlotIndices = new List<int>();
        private string lastUploadDiagnostics = string.Empty;
        private string lastDepthRenderDiagnostics = string.Empty;
        private string lastColorRenderDiagnostics = string.Empty;
        private bool disposed;

        public VegetationIndirectRenderer(VegetationRuntimeRegistry registry, int renderLayer, bool diagnosticsEnabled = false)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            this.diagnosticsEnabled = diagnosticsEnabled;
            slotResources = new SlotResources[registry.DrawSlots.Count];
            for (int i = 0; i < slotResources.Length; i++)
            {
                slotResources[i] = new SlotResources(registry.DrawSlots[i]);
            }

            if (diagnosticsEnabled)
            {
                UnityEngine.Debug.Log($"VegetationIndirectRenderer created renderLayer={renderLayer} slotCount={slotResources.Length}");
            }
        }

        public IReadOnlyList<int> ActiveSlotIndices => activeSlotIndices;

        public bool HasUploadedFrame => activeSlotIndices.Count > 0;

        /// <summary>
        /// [INTEGRATION] Uploads the latest Phase D visible-slot outputs into exact Phase E draw-slot GPU resources.
        /// </summary>
        public void UploadFrameOutput(VegetationFrameOutput frameOutput)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(VegetationIndirectRenderer));
            }

            if (frameOutput == null)
            {
                throw new ArgumentNullException(nameof(frameOutput));
            }

            activeSlotIndices.Clear();
            for (int activeSlotOffset = 0; activeSlotOffset < frameOutput.ActiveSlotIndices.Count; activeSlotOffset++)
            {
                int slotIndex = frameOutput.ActiveSlotIndices[activeSlotOffset];
                VegetationVisibleSlotOutput slotOutput = frameOutput.SlotOutputs[slotIndex];
                slotResources[slotIndex].Upload(slotOutput);
                activeSlotIndices.Add(slotIndex);
            }

            LogUploadDiagnostics(frameOutput);
        }

        /// <summary>
        /// [INTEGRATION] Renders the uploaded slot batches through raster command-buffer indirect draws for one camera/pass pair.
        /// </summary>
        internal void Render(IRasterCommandBuffer commandBuffer, Camera camera, VegetationRenderPassMode passMode)
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
                commandBuffer.DrawMeshInstancedIndirect(slot.DrawSlot.Mesh, 0, material, 0, slot.ArgsBuffer);
            });
        }

        /// <summary>
        /// [INTEGRATION] Renders the uploaded slot batches through compatibility command-buffer indirect draws for one camera/pass pair.
        /// </summary>
        internal void Render(CommandBuffer commandBuffer, Camera camera, VegetationRenderPassMode passMode)
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
                commandBuffer.DrawMeshInstancedIndirect(slot.DrawSlot.Mesh, 0, material, 0, slot.ArgsBuffer);
            });
        }

        private void RenderInternal(Camera camera, VegetationRenderPassMode passMode, Action<SlotResources, Material> issueDraw)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            int renderedSlotCount = 0;
            int renderedInstanceCount = 0;
            for (int activeSlotOffset = 0; activeSlotOffset < activeSlotIndices.Count; activeSlotOffset++)
            {
                SlotResources slot = slotResources[activeSlotIndices[activeSlotOffset]];
                if (!slot.HasVisibleData)
                {
                    continue;
                }

                Material material = passMode == VegetationRenderPassMode.Depth ? slot.DepthMaterial : slot.ColorMaterial;
                issueDraw(slot, material);
                renderedSlotCount++;
                renderedInstanceCount += slot.InstanceCount;
            }

            LogRenderDiagnostics(camera, passMode, renderedSlotCount, renderedInstanceCount);
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
                if (!slot.HasVisibleData)
                {
                    continue;
                }

                target.Add(new VegetationIndirectDrawBatchSnapshot
                {
                    SlotIndex = slot.DrawSlot.SlotIndex,
                    DebugLabel = slot.DrawSlot.DebugLabel,
                    MaterialKind = slot.DrawSlot.MaterialKind,
                    InstanceCount = slot.InstanceCount,
                    WorldBounds = slot.WorldBounds
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

        private sealed class SlotResources : IDisposable
        {
            private static readonly int InstanceBufferId = Shader.PropertyToID("_VegetationInstanceData");
            private GraphicsBuffer? instanceBuffer;
            private GraphicsBuffer? argsBuffer;
            private VegetationIndirectInstanceData[] uploadCache = Array.Empty<VegetationIndirectInstanceData>();
            private readonly GraphicsBuffer.IndirectDrawIndexedArgs[] argsUpload = new GraphicsBuffer.IndirectDrawIndexedArgs[1];

            public SlotResources(VegetationDrawSlot drawSlot)
            {
                DrawSlot = drawSlot;
                ColorMaterial = VegetationIndirectMaterialFactory.CreateColorMaterial(drawSlot);
                DepthMaterial = VegetationIndirectMaterialFactory.CreateDepthMaterial(drawSlot);
            }

            public VegetationDrawSlot DrawSlot { get; }

            public Material ColorMaterial { get; }

            public Material DepthMaterial { get; }

            public GraphicsBuffer ArgsBuffer => argsBuffer ?? throw new InvalidOperationException("Indirect args buffer has not been created.");

            public int InstanceCount { get; private set; }

            public Bounds WorldBounds { get; private set; }

            public bool HasVisibleData { get; private set; }

            public void Upload(VegetationVisibleSlotOutput slotOutput)
            {
                if (slotOutput == null)
                {
                    throw new ArgumentNullException(nameof(slotOutput));
                }

                if (slotOutput.InstanceCount <= 0 || !slotOutput.HasVisibleBounds)
                {
                    HasVisibleData = false;
                    InstanceCount = 0;
                    return;
                }

                EnsureCapacity(slotOutput.InstanceCount);
                for (int instanceIndex = 0; instanceIndex < slotOutput.InstanceCount; instanceIndex++)
                {
                    VegetationVisibleInstance instance = slotOutput.Instances[instanceIndex];
                    uploadCache[instanceIndex] = new VegetationIndirectInstanceData
                    {
                        ObjectToWorld = instance.LocalToWorld,
                        WorldToObject = instance.WorldToObject,
                        PackedLeafTint = instance.PackedLeafTint
                    };
                }

                instanceBuffer!.SetData(uploadCache, 0, 0, slotOutput.InstanceCount);
                ColorMaterial.SetBuffer(InstanceBufferId, instanceBuffer);
                DepthMaterial.SetBuffer(InstanceBufferId, instanceBuffer);

                VegetationIndirectArgsSeed seed = slotOutput.BuildIndirectArgsSeed();
                argsUpload[0] = new GraphicsBuffer.IndirectDrawIndexedArgs
                {
                    indexCountPerInstance = seed.IndexCountPerInstance,
                    instanceCount = seed.InstanceCount,
                    startIndex = seed.StartIndexLocation,
                    baseVertexIndex = checked((uint)seed.BaseVertexLocation),
                    startInstance = seed.StartInstanceLocation
                };
                argsBuffer!.SetData(argsUpload);

                InstanceCount = slotOutput.InstanceCount;
                WorldBounds = slotOutput.VisibleBounds;
                HasVisibleData = true;
            }

            public void Dispose()
            {
                instanceBuffer?.Release();
                instanceBuffer = null;
                argsBuffer?.Release();
                argsBuffer = null;
                VegetationIndirectMaterialFactory.DestroyRuntimeMaterial(ColorMaterial);
                VegetationIndirectMaterialFactory.DestroyRuntimeMaterial(DepthMaterial);
            }

            private void EnsureCapacity(int requiredCount)
            {
                int currentCapacity = uploadCache.Length;
                if (currentCapacity >= requiredCount && instanceBuffer != null && argsBuffer != null)
                {
                    return;
                }

                int newCapacity = Mathf.Max(1, currentCapacity);
                while (newCapacity < requiredCount)
                {
                    newCapacity <<= 1;
                }

                instanceBuffer?.Release();
                argsBuffer?.Release();
                instanceBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    newCapacity,
                    Marshal.SizeOf<VegetationIndirectInstanceData>());
                argsBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.IndirectArguments,
                    1,
                    GraphicsBuffer.IndirectDrawIndexedArgs.size);
                uploadCache = new VegetationIndirectInstanceData[newCapacity];
            }
        }

        private void LogUploadDiagnostics(VegetationFrameOutput frameOutput)
        {
            if (!diagnosticsEnabled)
            {
                return;
            }

            int totalVisibleInstances = 0;
            StringBuilder builder = new StringBuilder(256);
            int slotsToLog = Mathf.Min(activeSlotIndices.Count, 6);
            for (int activeSlotOffset = 0; activeSlotOffset < activeSlotIndices.Count; activeSlotOffset++)
            {
                VegetationVisibleSlotOutput slotOutput = frameOutput.SlotOutputs[activeSlotIndices[activeSlotOffset]];
                totalVisibleInstances += slotOutput.InstanceCount;

                if (activeSlotOffset < slotsToLog)
                {
                    if (builder.Length > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(slotOutput.DrawSlot.DebugLabel);
                    builder.Append('x');
                    builder.Append(slotOutput.InstanceCount);
                }
            }

            string summary =
                $"VegetationIndirectRenderer upload activeSlots={activeSlotIndices.Count} visibleInstances={totalVisibleInstances} slots=[{builder}]";
            if (summary == lastUploadDiagnostics)
            {
                return;
            }

            lastUploadDiagnostics = summary;
            if (activeSlotIndices.Count == 0)
            {
                UnityEngine.Debug.LogWarning(summary);
            }
            else
            {
                UnityEngine.Debug.Log(summary);
            }
        }

        private void LogRenderDiagnostics(Camera camera, VegetationRenderPassMode passMode, int renderedSlotCount, int renderedInstanceCount)
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
                builder.Append('x');
                builder.Append(slot.InstanceCount);
            }

            string summary =
                $"VegetationIndirectRenderer render camera={camera.name} pass={passMode} uploadedSlots={activeSlotIndices.Count} renderedSlots={renderedSlotCount} renderedInstances={renderedInstanceCount} slots=[{builder}]";
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
