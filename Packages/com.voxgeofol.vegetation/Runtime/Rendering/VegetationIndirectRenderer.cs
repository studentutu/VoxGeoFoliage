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
    /// Current shipped safety path keeps per-slot runtime materials and treats active-slot readback as diagnostics-only.
    /// </summary>
    public sealed class VegetationIndirectRenderer : IDisposable
    {
        private static readonly ProfilerMarker BindGpuResidentFrameMarker = new ProfilerMarker("VoxGeoFol.VegetationIndirectRenderer.BindGpuResidentFrame");
        private static readonly ProfilerMarker RenderMarker = new ProfilerMarker("VoxGeoFol.VegetationIndirectRenderer.Render");
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
        private ComputeBuffer? lastBoundSlotEmittedInstanceCountsBuffer;
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

        public int RuntimeMaterialCopyCount => slotResources.Length;

        /// <summary>
        /// [INTEGRATION] Binds GPU-resident indirect resources prepared by the compute classification/decode path.
        /// One args record exists per draw slot. Shared GPU buffers are bound once per slot-local runtime material.
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

                gpuResidentArgsBuffer = argsBuffer;
                lastBoundInstanceBuffer = instanceBuffer;
                lastBoundSlotPackedStartsBuffer = slotPackedStartsBuffer;
                lastBoundSlotEmittedInstanceCountsBuffer = slotEmittedInstanceCountsBuffer;
                lastBoundSubmittedInstanceCount = 0L;
                activeSlotIndices.Clear();
                for (int slotIndex = 0; slotIndex < slotResources.Length; slotIndex++)
                {
                    SlotResources slot = slotResources[slotIndex];
                    slot.BindSharedBuffers(instanceBuffer, slotPackedStartsBuffer);
                    activeSlotIndices.Add(slotIndex);
                }

                hasGpuResidentFrame = slotResources.Length > 0;
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
            for (int i = 0; i < slotResources.Length; i++)
            {
                slotResources[i].Dispose();
            }

            activeSlotIndices.Clear();
            slotEmittedCountsReadback = Array.Empty<uint>();
            lastBoundSubmittedInstanceCount = 0L;
            gpuResidentArgsBuffer = null;
            hasGpuResidentFrame = false;
            lastBoundInstanceBuffer = null;
            lastBoundSlotPackedStartsBuffer = null;
            lastBoundSlotEmittedInstanceCountsBuffer = null;
            commandBufferDrawWrapper.ClearCommandBuffer();
            rasterCommandBufferDrawWrapper.ClearCommandBuffer();
        }

        private sealed class SlotResources : IDisposable
        {
            private const string ForwardLitPassName = "ForwardLit";
            private const string DepthOnlyPassName = "DepthOnly";
            private const string ShadowCasterPassName = "ShadowCaster";
            private static readonly int InstanceBufferId = Shader.PropertyToID("_VegetationInstanceData");
            private static readonly int SlotPackedStartsId = Shader.PropertyToID("_VegetationSlotPackedStarts");
            private static readonly int SlotIndexId = Shader.PropertyToID("_VegetationSlotIndex");
            private readonly int forwardShaderPass;
            private readonly int depthShaderPass;
            private readonly int shadowShaderPass;
            private readonly Material runtimeMaterial;

            public SlotResources(
                VegetationDrawSlot drawSlot,
                Bounds conservativeWorldBounds)
            {
                DrawSlot = drawSlot;
                ConservativeWorldBounds = conservativeWorldBounds;
                SharedArgsBufferOffset = checked(GraphicsBuffer.IndirectDrawIndexedArgs.size * drawSlot.SlotIndex);
                runtimeMaterial = new Material(drawSlot.Material)
                {
                    name = $"{drawSlot.DebugLabel}:IndirectRuntime",
                    enableInstancing = true,
                    hideFlags = HideFlags.HideAndDontSave
                };
                runtimeMaterial.SetInteger(SlotIndexId, drawSlot.SlotIndex);
                forwardShaderPass = runtimeMaterial.FindPass(ForwardLitPassName);
                depthShaderPass = runtimeMaterial.FindPass(DepthOnlyPassName);
                shadowShaderPass = runtimeMaterial.FindPass(ShadowCasterPassName);
            }

            public VegetationDrawSlot DrawSlot { get; }

            public Bounds ConservativeWorldBounds { get; }

            public int SharedArgsBufferOffset { get; }

            public void BindSharedBuffers(GraphicsBuffer sharedInstanceBuffer, ComputeBuffer slotPackedStartsBuffer)
            {
                runtimeMaterial.SetBuffer(InstanceBufferId, sharedInstanceBuffer);
                runtimeMaterial.SetBuffer(SlotPackedStartsId, slotPackedStartsBuffer);
                runtimeMaterial.SetInteger(SlotIndexId, DrawSlot.SlotIndex);
            }

            public bool TryResolveDrawSubmission(
                VegetationRenderPassMode passMode,
                out Material material,
                out int shaderPass)
            {
                if (passMode == VegetationRenderPassMode.Depth)
                {
                    material = runtimeMaterial;
                    shaderPass = depthShaderPass;
                    return shaderPass >= 0;
                }

                material = runtimeMaterial;
                shaderPass = passMode == VegetationRenderPassMode.Shadow
                    ? shadowShaderPass
                    : forwardShaderPass;
                return shaderPass >= 0;
            }

            public int ResolveArgsBufferOffset()
            {
                return SharedArgsBufferOffset;
            }

            public void Dispose()
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(runtimeMaterial);
                    return;
                }

                UnityEngine.Object.DestroyImmediate(runtimeMaterial);
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
            bool hasReadback = TryReadbackSlotEmittedCounts();
            int uploadedSlotCount = activeSlotIndices.Count;
            int submittedSlotCount = renderedSlotCount;
            if (hasReadback)
            {
                uploadedSlotCount = 0;
                for (int slotIndex = 0; slotIndex < slotResources.Length; slotIndex++)
                {
                    if (slotEmittedCountsReadback[slotIndex] > 0u)
                    {
                        uploadedSlotCount++;
                    }
                }

                submittedSlotCount = uploadedSlotCount;
            }

            bool isDepthPass = passMode == VegetationRenderPassMode.Depth;
            if (isDepthPass)
            {
                if (cameraInstanceId == lastDepthRenderCameraInstanceId &&
                    uploadedSlotCount == lastDepthRenderUploadedSlotCount &&
                    submittedSlotCount == lastDepthRenderRenderedSlotCount)
                {
                    return;
                }

                lastDepthRenderCameraInstanceId = cameraInstanceId;
                lastDepthRenderUploadedSlotCount = uploadedSlotCount;
                lastDepthRenderRenderedSlotCount = submittedSlotCount;
            }
            else if (passMode == VegetationRenderPassMode.Color)
            {
                if (cameraInstanceId == lastColorRenderCameraInstanceId &&
                    uploadedSlotCount == lastColorRenderUploadedSlotCount &&
                    submittedSlotCount == lastColorRenderRenderedSlotCount)
                {
                    return;
                }

                lastColorRenderCameraInstanceId = cameraInstanceId;
                lastColorRenderUploadedSlotCount = uploadedSlotCount;
                lastColorRenderRenderedSlotCount = submittedSlotCount;
            }
            else
            {
                if (cameraInstanceId == lastShadowRenderCameraInstanceId &&
                    uploadedSlotCount == lastShadowRenderUploadedSlotCount &&
                    submittedSlotCount == lastShadowRenderRenderedSlotCount)
                {
                    return;
                }

                lastShadowRenderCameraInstanceId = cameraInstanceId;
                lastShadowRenderUploadedSlotCount = uploadedSlotCount;
                lastShadowRenderRenderedSlotCount = submittedSlotCount;
            }

            StringBuilder builder = new StringBuilder(256);
            int loggedSlotCount = 0;
            for (int activeSlotOffset = 0; activeSlotOffset < activeSlotIndices.Count && loggedSlotCount < 4; activeSlotOffset++)
            {
                int slotIndex = activeSlotIndices[activeSlotOffset];
                SlotResources slot = slotResources[slotIndex];
                uint emittedInstanceCount = hasReadback ? slotEmittedCountsReadback[slot.DrawSlot.SlotIndex] : 0u;
                if (hasReadback && emittedInstanceCount == 0u)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

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
                loggedSlotCount++;
            }

            string summary =
                $"VegetationIndirectRenderer render camera={camera.name} pass={passMode} uploadedSlots={uploadedSlotCount} renderedSlots={submittedSlotCount} renderedInstances={lastBoundSubmittedInstanceCount} slots=[{builder}]";

            if (submittedSlotCount == 0)
            {
                UnityEngine.Debug.LogWarning(summary);
            }
            else
            {
                UnityEngine.Debug.Log(summary);
            }
        }

        private bool TryReadbackSlotEmittedCounts()
        {
            ComputeBuffer? slotEmittedInstanceCountsBuffer = lastBoundSlotEmittedInstanceCountsBuffer;
            if (slotEmittedInstanceCountsBuffer == null)
            {
                return false;
            }

            EnsureSlotEmittedCountsReadbackCapacity(slotResources.Length);
            lastBoundSubmittedInstanceCount = 0L;
            if (slotResources.Length <= 0)
            {
                return false;
            }

            slotEmittedInstanceCountsBuffer.GetData(slotEmittedCountsReadback, 0, 0, slotResources.Length);
            for (int slotIndex = 0; slotIndex < slotResources.Length; slotIndex++)
            {
                lastBoundSubmittedInstanceCount += slotEmittedCountsReadback[slotIndex];
            }

            return true;
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
