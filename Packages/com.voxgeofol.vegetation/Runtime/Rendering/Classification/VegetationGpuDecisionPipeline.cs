#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// GPU-resident vegetation classification and indirect-emission pipeline for the urgent tree-first runtime contracts.
    /// It accepts one tree tier per visible tree first, then expands branch work only for promoted trees.
    /// </summary>
    public sealed class VegetationGpuDecisionPipeline : IDisposable
    {
        private const int FrameStatCount = 13;
        private const int FrameStatVisibleTrees = 0;
        private const int FrameStatAcceptedTreeL3 = 1;
        private const int FrameStatPromotedL2 = 2;
        private const int FrameStatPromotedL1 = 3;
        private const int FrameStatPromotedL0 = 4;
        private const int FrameStatRejectedPromotions = 5;
        private const int FrameStatExpandedTrees = 6;
        private const int FrameStatExpandedBranchWorkItems = 7;
        private const int FrameStatAcceptedTierCostUsage = 8;
        private const int FrameStatBaselineTreeL3Failures = 9;
        private const int FrameStatVisibleInstanceCapHits = 10;
        private const int FrameStatExpandedBranchWorkItemCapHits = 11;
        private const int FrameStatEmittedVisibleInstances = 12;
        private static readonly ProfilerMarker PrepareResidentFrameMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.PrepareResidentFrame");
        private static readonly ProfilerMarker ResetFrameStateMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.ResetFrameState");
        private static readonly ProfilerMarker CountTreeInstancesMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.CountTreeInstances");
        private static readonly ProfilerMarker CountBranchInstancesMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.CountBranchInstances");
        private static readonly ProfilerMarker ClampRequestedSlotCountsMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.ClampRequestedSlotCounts");
        private static readonly ProfilerMarker BuildSlotStartsMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.BuildSlotStarts");
        private static readonly ProfilerMarker EmitTreeInstancesMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.EmitTreeInstances");
        private static readonly ProfilerMarker EmitBranchInstancesMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.EmitBranchInstances");
        private static readonly ProfilerMarker FinalizeIndirectArgsMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.FinalizeIndirectArgs");
        private static readonly int CellGpuStrideBytes = Marshal.SizeOf<CellGpu>();
        private static readonly int LodProfileGpuStrideBytes = Marshal.SizeOf<LodProfileGpu>();
        private static readonly int BlueprintGpuStrideBytes = Marshal.SizeOf<BlueprintGpu>();
        private static readonly int PlacementGpuStrideBytes = Marshal.SizeOf<PlacementGpu>();
        private static readonly int PrototypeGpuStrideBytes = Marshal.SizeOf<PrototypeGpu>();
        private static readonly int TreeGpuStrideBytes = Marshal.SizeOf<TreeGpu>();
        private static readonly int TreeVisibilityGpuStrideBytes = Marshal.SizeOf<TreeVisibilityGpu>();
        private static readonly int ExpandedBranchWorkItemStrideBytes = Marshal.SizeOf<VegetationBranchDecisionRecord>();
        private static readonly int SlotGpuStrideBytes = Marshal.SizeOf<SlotGpu>();
        private static readonly int VisibleInstanceStrideBytesInternal = Marshal.SizeOf<VegetationIndirectInstanceData>();
        private const float PriorityRingScale = 4f;
        private const int UIntStrideBytes = sizeof(uint);
        private const int ComputeThreadGroupSize = 64;
        private const int DispatchArgumentCount = 3;
        private readonly ComputeShader classifyShader;
        private readonly VegetationRuntimeRegistry registry;
        private readonly int resetFrameStateKernel;
        private readonly int classifyCellsKernel;
        private readonly int classifyTreesKernel;
        private readonly int acceptTreeTiersKernel;
        private readonly int generateExpandedBranchWorkItemsKernel;
        private readonly int buildExpandedBranchDispatchArgsKernel;
        private readonly int resetSlotCountsKernel;
        private readonly int countTreesKernel;
        private readonly int countExpandedBranchesKernel;
        private readonly int clampRequestedSlotCountsKernel;
        private readonly int buildSlotStartsKernel;
        private readonly int emitTreesKernel;
        private readonly int emitExpandedBranchesKernel;
        private readonly int finalizeIndirectArgsKernel;
        private readonly int visibleInstanceCapacity;
        private readonly int expandedBranchWorkItemCapacity;
        private readonly int approxWorkUnitCapacity;
        private readonly int priorityRingCount;
        private ComputeBuffer cellBuffer = null!;
        private ComputeBuffer lodBuffer = null!;
        private ComputeBuffer blueprintBuffer = null!;
        private ComputeBuffer placementBuffer = null!;
        private ComputeBuffer prototypeBuffer = null!;
        private ComputeBuffer treeBuffer = null!;
        private ComputeBuffer treeVisibilityBuffer = null!;
        private ComputeBuffer expandedBranchWorkItemBuffer = null!;
        private ComputeBuffer expandedBranchWorkItemCountBuffer = null!;
        private ComputeBuffer expandedBranchDispatchArgsBuffer = null!;
        private ComputeBuffer frameStatsBuffer = null!;
        private ComputeBuffer priorityRingTreeCountBuffer = null!;
        private ComputeBuffer priorityRingOffsetsBuffer = null!;
        private ComputeBuffer priorityOrderedVisibleTreeIndicesBuffer = null!;
        private ComputeBuffer slotMetadataBuffer = null!;
        private ComputeBuffer slotRequestedInstanceCountBuffer = null!;
        private ComputeBuffer slotEmittedInstanceCountBuffer = null!;
        private ComputeBuffer slotPackedStartsBuffer = null!;
        private ComputeBuffer cellVisibilityBuffer = null!;
        private GraphicsBuffer residentInstanceBuffer = null!;
        private GraphicsBuffer residentArgsBuffer = null!;
        private readonly Vector4[] frustumPlaneVectors = new Vector4[6];
        private readonly uint[] latestPreparedFrameStats;
        private readonly uint[] latestSlotEmittedCounts;
        private int[] latestActiveSlotIndices;
        private bool residentFramePrepared;
        private bool preparedFrameTelemetryReadbackPending;
        private bool slotEmissionReadbackPending;
        private bool hasLatestPreparedFrameStats;
        private bool hasLatestActiveSlotIndices;
        private int preparedFrameReadbackSequence;
        private int pendingPreparedFrameTelemetryReadbackSequence = -1;
        private int pendingSlotEmissionReadbackSequence = -1;
        private bool disposed;

        public readonly struct PreparedFrameTelemetry
        {
            public PreparedFrameTelemetry(
                int visibleTrees,
                int acceptedTreeL3,
                int promotedL2,
                int promotedL1,
                int promotedL0,
                int rejectedPromotions,
                int expandedTrees,
                int expandedBranchWorkItems,
                int acceptedTierCostUsage,
                int baselineTreeL3Failures,
                int nonZeroEmittedSlots,
                long emittedVisibleInstanceCount,
                bool approxWorkUnitCapHit,
                bool visibleInstanceCapHit,
                bool expandedBranchWorkItemCapHit)
            {
                VisibleTrees = visibleTrees;
                AcceptedTreeL3 = acceptedTreeL3;
                PromotedL2 = promotedL2;
                PromotedL1 = promotedL1;
                PromotedL0 = promotedL0;
                RejectedPromotions = rejectedPromotions;
                ExpandedTrees = expandedTrees;
                ExpandedBranchWorkItems = expandedBranchWorkItems;
                AcceptedTierCostUsage = acceptedTierCostUsage;
                BaselineTreeL3Failures = baselineTreeL3Failures;
                NonZeroEmittedSlots = nonZeroEmittedSlots;
                EmittedVisibleInstanceCount = emittedVisibleInstanceCount;
                ApproxWorkUnitCapHit = approxWorkUnitCapHit;
                VisibleInstanceCapHit = visibleInstanceCapHit;
                ExpandedBranchWorkItemCapHit = expandedBranchWorkItemCapHit;
            }

            public int VisibleTrees { get; }
            public int AcceptedTreeL3 { get; }
            public int PromotedL2 { get; }
            public int PromotedL1 { get; }
            public int PromotedL0 { get; }
            public int RejectedPromotions { get; }
            public int ExpandedTrees { get; }
            public int ExpandedBranchWorkItems { get; }
            public int AcceptedTierCostUsage { get; }
            public int BaselineTreeL3Failures { get; }
            public int NonZeroEmittedSlots { get; }
            public long EmittedVisibleInstanceCount { get; }
            public bool ApproxWorkUnitCapHit { get; }
            public bool VisibleInstanceCapHit { get; }
            public bool ExpandedBranchWorkItemCapHit { get; }
        }

        public readonly struct PreparedFrameSlotTelemetry
        {
            public PreparedFrameSlotTelemetry(int slotIndex, uint emittedInstanceCount)
            {
                SlotIndex = slotIndex;
                EmittedInstanceCount = emittedInstanceCount;
            }

            public int SlotIndex { get; }

            public uint EmittedInstanceCount { get; }
        }

        public readonly struct PreparedFrameIndirectArgsTelemetry
        {
            public PreparedFrameIndirectArgsTelemetry(
                int slotIndex,
                uint requestedInstanceCount,
                uint emittedInstanceCount,
                uint packedStart,
                uint indexCountPerInstance,
                uint instanceCount,
                uint startIndexLocation,
                int baseVertexLocation,
                uint startInstanceLocation)
            {
                SlotIndex = slotIndex;
                RequestedInstanceCount = requestedInstanceCount;
                EmittedInstanceCount = emittedInstanceCount;
                PackedStart = packedStart;
                IndexCountPerInstance = indexCountPerInstance;
                InstanceCount = instanceCount;
                StartIndexLocation = startIndexLocation;
                BaseVertexLocation = baseVertexLocation;
                StartInstanceLocation = startInstanceLocation;
            }

            public int SlotIndex { get; }

            public uint RequestedInstanceCount { get; }

            public uint EmittedInstanceCount { get; }

            public uint PackedStart { get; }

            public uint IndexCountPerInstance { get; }

            public uint InstanceCount { get; }

            public uint StartIndexLocation { get; }

            public int BaseVertexLocation { get; }

            public uint StartInstanceLocation { get; }
        }

        public GraphicsBuffer ResidentInstanceBuffer => residentInstanceBuffer;

        public GraphicsBuffer ResidentArgsBuffer => residentArgsBuffer;

        public ComputeBuffer ResidentSlotPackedStartsBuffer => slotPackedStartsBuffer;

        public ComputeBuffer ResidentSlotEmittedInstanceCountsBuffer => slotEmittedInstanceCountBuffer;

        public bool HasResidentFramePrepared => residentFramePrepared;

        public int BlueprintCount => registry.TreeBlueprints.Count;

        public int CellCount => registry.SpatialGrid.Cells.Count;

        public int AllocatedCellBufferElementCount => Mathf.Max(1, CellCount);

        public long CellBufferBytes => checked((long)AllocatedCellBufferElementCount * CellGpuStrideBytes);

        public int LodProfileCount => registry.LodProfiles.Count;

        public int AllocatedLodProfileBufferElementCount => Mathf.Max(1, LodProfileCount);

        public long LodProfileBufferBytes => checked((long)AllocatedLodProfileBufferElementCount * LodProfileGpuStrideBytes);

        public int AllocatedBlueprintBufferElementCount => Mathf.Max(1, BlueprintCount);

        public long BlueprintBufferBytes => checked((long)AllocatedBlueprintBufferElementCount * BlueprintGpuStrideBytes);

        public int BlueprintPlacementCount => registry.BlueprintBranchPlacements.Count;

        public int AllocatedBlueprintPlacementBufferElementCount => Mathf.Max(1, registry.BlueprintBranchPlacements.Count);

        public long BlueprintPlacementBufferBytes => checked((long)AllocatedBlueprintPlacementBufferElementCount * PlacementGpuStrideBytes);

        public int BranchPrototypeCount => registry.BranchPrototypes.Count;

        public int AllocatedPrototypeBufferElementCount => Mathf.Max(1, registry.BranchPrototypes.Count);

        public long PrototypeBufferBytes => checked((long)AllocatedPrototypeBufferElementCount * PrototypeGpuStrideBytes);

        public int TreeCount => registry.TreeInstances.Count;

        public int AllocatedTreeBufferElementCount => Mathf.Max(1, TreeCount);

        public long TreeBufferBytes => checked((long)AllocatedTreeBufferElementCount * TreeGpuStrideBytes);

        public int AllocatedTreeVisibilityBufferElementCount => Mathf.Max(1, registry.TreeInstances.Count);

        public long TreeVisibilityBufferBytes => checked((long)AllocatedTreeVisibilityBufferElementCount * TreeVisibilityGpuStrideBytes);

        public int PriorityRingCount => priorityRingCount;

        public long PriorityRingTreeCountBufferBytes => checked((long)Mathf.Max(1, priorityRingCount) * UIntStrideBytes);

        public long PriorityRingOffsetsBufferBytes => checked((long)Mathf.Max(1, priorityRingCount) * UIntStrideBytes);

        public int PriorityOrderedVisibleTreeIndexCapacity => Mathf.Max(1, registry.TreeInstances.Count);

        public long PriorityOrderedVisibleTreeIndexBufferBytes => checked((long)PriorityOrderedVisibleTreeIndexCapacity * UIntStrideBytes);

        public int ExpandedBranchWorkItemCapacity => expandedBranchWorkItemCapacity;

        public long ExpandedBranchWorkItemBufferBytes => checked((long)ExpandedBranchWorkItemCapacity * ExpandedBranchWorkItemStrideBytes);

        public long ExpandedBranchDispatchArgsBufferBytes => checked((long)DispatchArgumentCount * UIntStrideBytes);

        public int DrawSlotCount => registry.DrawSlots.Count;

        public int AllocatedDrawSlotBufferElementCount => Mathf.Max(1, DrawSlotCount);

        public int ApproxWorkUnitCapacity => approxWorkUnitCapacity;

        public long SlotMetadataBufferBytes => checked((long)AllocatedDrawSlotBufferElementCount * SlotGpuStrideBytes);

        public long SlotRequestedInstanceCountBufferBytes => checked((long)AllocatedDrawSlotBufferElementCount * UIntStrideBytes);

        public long SlotEmittedInstanceCountBufferBytes => checked((long)AllocatedDrawSlotBufferElementCount * UIntStrideBytes);

        public long SlotPackedStartsBufferBytes => checked((long)AllocatedDrawSlotBufferElementCount * UIntStrideBytes);

        public long CellVisibilityBufferBytes => checked((long)AllocatedCellBufferElementCount * UIntStrideBytes);

        public long ExpandedBranchWorkItemCountBufferBytes => UIntStrideBytes;

        public long FrameStatsBufferBytes => checked((long)FrameStatCount * UIntStrideBytes);

        public long TotalBranchTelemetryBufferBytes => checked(
            BlueprintPlacementBufferBytes +
            PrototypeBufferBytes +
            TreeVisibilityBufferBytes +
            ExpandedBranchWorkItemBufferBytes);

        public int VisibleInstanceStrideBytes => VisibleInstanceStrideBytesInternal;

        public int VisibleInstanceCapacity => visibleInstanceCapacity;

        public long VisibleInstanceCapacityBytes => checked((long)visibleInstanceCapacity * VisibleInstanceStrideBytesInternal);

        public long IndirectArgsBufferBytes => checked((long)Mathf.Max(1, DrawSlotCount) * GraphicsBuffer.IndirectDrawIndexedArgs.size);

        public long TotalComputeBufferBytes => checked(
            CellBufferBytes +
            LodProfileBufferBytes +
            BlueprintBufferBytes +
            BlueprintPlacementBufferBytes +
            PrototypeBufferBytes +
            TreeBufferBytes +
            TreeVisibilityBufferBytes +
            PriorityRingTreeCountBufferBytes +
            PriorityRingOffsetsBufferBytes +
            PriorityOrderedVisibleTreeIndexBufferBytes +
            ExpandedBranchWorkItemBufferBytes +
            ExpandedBranchWorkItemCountBufferBytes +
            ExpandedBranchDispatchArgsBufferBytes +
            FrameStatsBufferBytes +
            SlotMetadataBufferBytes +
            SlotRequestedInstanceCountBufferBytes +
            SlotEmittedInstanceCountBufferBytes +
            SlotPackedStartsBufferBytes +
            CellVisibilityBufferBytes);

        public long TotalGraphicsBufferBytes => checked(
            VisibleInstanceCapacityBytes +
            IndirectArgsBufferBytes);

        public long TotalGpuBufferBytes => checked(TotalComputeBufferBytes + TotalGraphicsBufferBytes);

        public VegetationGpuDecisionPipeline(
            ComputeShader classifyShader,
            VegetationRuntimeRegistry registry,
            VegetationViewRuntimeBudget budget)
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                throw new NotSupportedException(
                    "This runtime does not support compute shaders, so the urgent GPU decision path is unavailable.");
            }

            this.classifyShader = classifyShader ?? throw new ArgumentNullException(nameof(classifyShader));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.visibleInstanceCapacity = budget.MaxVisibleInstances;
            this.expandedBranchWorkItemCapacity = budget.MaxExpandedBranchWorkItems;
            this.approxWorkUnitCapacity = budget.MaxApproxWorkUnits;
            this.priorityRingCount = ComputePriorityRingCount(registry);
            latestPreparedFrameStats = new uint[FrameStatCount];
            latestSlotEmittedCounts = new uint[Mathf.Max(1, registry.DrawSlots.Count)];
            latestActiveSlotIndices = Array.Empty<int>();

            try
            {
                resetFrameStateKernel = classifyShader.FindKernel("ResetFrameState");
                classifyCellsKernel = classifyShader.FindKernel("ClassifyCells");
                classifyTreesKernel = classifyShader.FindKernel("ClassifyTrees");
                acceptTreeTiersKernel = classifyShader.FindKernel("AcceptTreeTiers");
                generateExpandedBranchWorkItemsKernel = classifyShader.FindKernel("GenerateExpandedBranchWorkItems");
                buildExpandedBranchDispatchArgsKernel = classifyShader.FindKernel("BuildExpandedBranchDispatchArgs");
                resetSlotCountsKernel = classifyShader.FindKernel("ResetSlotCounts");
                countTreesKernel = classifyShader.FindKernel("CountTrees");
                countExpandedBranchesKernel = classifyShader.FindKernel("CountExpandedBranches");
                clampRequestedSlotCountsKernel = classifyShader.FindKernel("ClampRequestedSlotCounts");
                buildSlotStartsKernel = classifyShader.FindKernel("BuildSlotStarts");
                emitTreesKernel = classifyShader.FindKernel("EmitTrees");
                emitExpandedBranchesKernel = classifyShader.FindKernel("EmitExpandedBranches");
                finalizeIndirectArgsKernel = classifyShader.FindKernel("FinalizeIndirectArgs");
            }
            catch (ArgumentException exception)
            {
                throw new NotSupportedException(
                    "VegetationClassify.compute imported without the expected kernels. The urgent GPU decision path is unavailable in this Unity environment.",
                    exception);
            }

            try
            {
                cellBuffer = CreateStructuredBuffer<CellGpu>(Mathf.Max(1, registry.SpatialGrid.Cells.Count));
                lodBuffer = CreateStructuredBuffer<LodProfileGpu>(Mathf.Max(1, registry.LodProfiles.Count));
                blueprintBuffer = CreateStructuredBuffer<BlueprintGpu>(Mathf.Max(1, registry.TreeBlueprints.Count));
                placementBuffer = CreateStructuredBuffer<PlacementGpu>(Mathf.Max(1, registry.BlueprintBranchPlacements.Count));
                prototypeBuffer = CreateStructuredBuffer<PrototypeGpu>(Mathf.Max(1, registry.BranchPrototypes.Count));
                treeBuffer = CreateStructuredBuffer<TreeGpu>(Mathf.Max(1, registry.TreeInstances.Count));
                treeVisibilityBuffer = CreateStructuredBuffer<TreeVisibilityGpu>(Mathf.Max(1, registry.TreeInstances.Count));
                expandedBranchWorkItemBuffer = CreateStructuredBuffer<VegetationBranchDecisionRecord>(this.expandedBranchWorkItemCapacity);
                expandedBranchWorkItemCountBuffer = CreateStructuredBuffer<uint>(1);
                expandedBranchDispatchArgsBuffer = new ComputeBuffer(
                    DispatchArgumentCount,
                    UIntStrideBytes,
                    ComputeBufferType.IndirectArguments);
                frameStatsBuffer = CreateStructuredBuffer<uint>(FrameStatCount);
                priorityRingTreeCountBuffer = CreateStructuredBuffer<uint>(this.priorityRingCount);
                priorityRingOffsetsBuffer = CreateStructuredBuffer<uint>(this.priorityRingCount);
                priorityOrderedVisibleTreeIndicesBuffer = CreateStructuredBuffer<uint>(Mathf.Max(1, registry.TreeInstances.Count));
                slotMetadataBuffer = CreateStructuredBuffer<SlotGpu>(Mathf.Max(1, registry.DrawSlots.Count));
                slotRequestedInstanceCountBuffer = CreateStructuredBuffer<uint>(Mathf.Max(1, registry.DrawSlots.Count));
                slotEmittedInstanceCountBuffer = CreateStructuredBuffer<uint>(Mathf.Max(1, registry.DrawSlots.Count));
                slotPackedStartsBuffer = CreateStructuredBuffer<uint>(Mathf.Max(1, registry.DrawSlots.Count));
                cellVisibilityBuffer = CreateStructuredBuffer<uint>(Mathf.Max(1, registry.SpatialGrid.Cells.Count));
                residentInstanceBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    this.visibleInstanceCapacity,
                    Marshal.SizeOf<VegetationIndirectInstanceData>());
                residentArgsBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments,
                    Mathf.Max(1, registry.DrawSlots.Count * (GraphicsBuffer.IndirectDrawIndexedArgs.size / sizeof(uint))),
                    sizeof(uint));

                UploadStaticData();
                BindBuffers();
            }
            catch
            {
                ReleaseResources();
                throw;
            }
        }

        /// <summary>
        /// [INTEGRATION] Executes the full GPU-resident classification and tree-first accepted-content emission path into indirect draw resources.
        /// </summary>
        public void PrepareResidentFrame(
            Vector3 cameraWorldPosition,
            Plane[] frustumPlanes,
            bool allowExpandedTreePromotion,
            bool limitExpandedPromotionToNearTiers,
            bool captureTelemetry)
        {
            using (PrepareResidentFrameMarker.Auto())
            {
                if (disposed)
                {
                    return;
                }

                if (frustumPlanes == null)
                {
                    return;
                }

                if (frustumPlanes.Length < 6)
                {
                   return;
                }

                UploadDynamicFrameData(
                    cameraWorldPosition,
                    frustumPlanes,
                    allowExpandedTreePromotion,
                    limitExpandedPromotionToNearTiers);

                using (ResetFrameStateMarker.Auto())
                {
                    DispatchKernel(resetFrameStateKernel, 1);
                }

                DispatchKernel(classifyCellsKernel, registry.SpatialGrid.Cells.Count);
                DispatchKernel(classifyTreesKernel, registry.TreeInstances.Count);
                DispatchKernel(acceptTreeTiersKernel, 1);

                DispatchKernel(resetSlotCountsKernel, registry.DrawSlots.Count);

                using (CountTreeInstancesMarker.Auto())
                {
                    DispatchKernel(countTreesKernel, registry.TreeInstances.Count);
                }

                if (allowExpandedTreePromotion)
                {
                    DispatchKernel(generateExpandedBranchWorkItemsKernel, registry.TreeInstances.Count);
                    DispatchKernel(buildExpandedBranchDispatchArgsKernel, 1);

                    using (CountBranchInstancesMarker.Auto())
                    {
                        DispatchKernelIndirect(countExpandedBranchesKernel, expandedBranchDispatchArgsBuffer);
                    }
                }

                using (ClampRequestedSlotCountsMarker.Auto())
                {
                    DispatchKernel(clampRequestedSlotCountsKernel, 1);
                }

                using (BuildSlotStartsMarker.Auto())
                {
                    DispatchKernel(buildSlotStartsKernel, 1);
                }

                using (EmitTreeInstancesMarker.Auto())
                {
                    DispatchKernel(emitTreesKernel, registry.TreeInstances.Count);
                }

                if (allowExpandedTreePromotion)
                {
                    using (EmitBranchInstancesMarker.Auto())
                    {
                        DispatchKernelIndirect(emitExpandedBranchesKernel, expandedBranchDispatchArgsBuffer);
                    }
                }

                using (FinalizeIndirectArgsMarker.Auto())
                {
                    DispatchKernel(finalizeIndirectArgsKernel, registry.DrawSlots.Count);
                }

                residentFramePrepared = true;
                SchedulePreparedFrameReadbacks(captureTelemetry);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            ReleaseResources();
        }

        /// <summary>
        /// [INTEGRATION] Synchronous prepared-frame readback is disabled because it introduces render-thread GPU fences.
        /// </summary>
        public PreparedFrameTelemetry ReadbackPreparedFrameTelemetry()
        {
            return TryGetLatestPreparedFrameTelemetry(out PreparedFrameTelemetry telemetry)
                ? telemetry
                : default;
        }

        /// <summary>
        /// [INTEGRATION] Synchronous prepared-frame slot readback is disabled because it introduces render-thread GPU fences.
        /// </summary>
        public void ReadbackPreparedFrameSlotTelemetry(List<PreparedFrameSlotTelemetry> target)
        {
            if (target == null)
            {
                return;
            }

            target.Clear();
            if (!hasLatestActiveSlotIndices)
            {
                return;
            }

            for (int i = 0; i < latestActiveSlotIndices.Length; i++)
            {
                int slotIndex = latestActiveSlotIndices[i];
                if (slotIndex < 0 || slotIndex >= latestSlotEmittedCounts.Length)
                {
                    continue;
                }

                target.Add(new PreparedFrameSlotTelemetry(slotIndex, latestSlotEmittedCounts[slotIndex]));
            }
        }

        /// <summary>
        /// [INTEGRATION] Synchronous indirect-args readback is disabled on the production prepare path.
        /// </summary>
        public void ReadbackPreparedFrameIndirectArgsTelemetry(List<PreparedFrameIndirectArgsTelemetry> target, int maxLiveSlots)
        {
            if (target == null)
            {
                return;
            }

            target.Clear();
        }

        /// <summary>
        /// [INTEGRATION] Returns the latest non-blocking prepared-frame telemetry snapshot captured from async GPU readback.
        /// </summary>
        public bool TryGetLatestPreparedFrameTelemetry(out PreparedFrameTelemetry telemetry)
        {
            if (!hasLatestPreparedFrameStats)
            {
                telemetry = default;
                return false;
            }

            uint rejectedPromotions = latestPreparedFrameStats[FrameStatRejectedPromotions];
            uint acceptedTierCostUsage = latestPreparedFrameStats[FrameStatAcceptedTierCostUsage];
            uint baselineTreeL3Failures = latestPreparedFrameStats[FrameStatBaselineTreeL3Failures];
            telemetry = new PreparedFrameTelemetry(
                (int)latestPreparedFrameStats[FrameStatVisibleTrees],
                (int)latestPreparedFrameStats[FrameStatAcceptedTreeL3],
                (int)latestPreparedFrameStats[FrameStatPromotedL2],
                (int)latestPreparedFrameStats[FrameStatPromotedL1],
                (int)latestPreparedFrameStats[FrameStatPromotedL0],
                (int)rejectedPromotions,
                (int)latestPreparedFrameStats[FrameStatExpandedTrees],
                (int)latestPreparedFrameStats[FrameStatExpandedBranchWorkItems],
                (int)acceptedTierCostUsage,
                (int)baselineTreeL3Failures,
                CountLatestNonZeroEmittedSlots(),
                latestPreparedFrameStats[FrameStatEmittedVisibleInstances],
                baselineTreeL3Failures > 0u || (rejectedPromotions > 0u && acceptedTierCostUsage >= (uint)approxWorkUnitCapacity),
                latestPreparedFrameStats[FrameStatVisibleInstanceCapHits] > 0u,
                latestPreparedFrameStats[FrameStatExpandedBranchWorkItemCapHits] > 0u);
            return true;
        }

        /// <summary>
        /// [INTEGRATION] Returns the latest non-blocking active-slot subset captured from async emitted-slot readback.
        /// </summary>
        public bool TryGetLatestActiveSlotIndices(out IReadOnlyList<int> activeSlotIndices)
        {
            if (!hasLatestActiveSlotIndices)
            {
                activeSlotIndices = Array.Empty<int>();
                return false;
            }

            activeSlotIndices = latestActiveSlotIndices;
            return true;
        }

        private void UploadStaticData()
        {
            CellGpu[] cells = new CellGpu[Mathf.Max(1, registry.SpatialGrid.Cells.Count)];
            for (int i = 0; i < registry.SpatialGrid.Cells.Count; i++)
            {
                Bounds bounds = registry.SpatialGrid.Cells[i].AuthoritativeBounds;
                cells[i] = new CellGpu
                {
                    Center = bounds.center,
                    Extents = bounds.extents
                };
            }

            cellBuffer.SetData(cells);

            LodProfileGpu[] lodProfiles = new LodProfileGpu[Mathf.Max(1, registry.LodProfiles.Count)];
            for (int i = 0; i < registry.LodProfiles.Count; i++)
            {
                VegetationLodProfileRuntime source = registry.LodProfiles[i];
                lodProfiles[i] = new LodProfileGpu
                {
                    L0Distance = source.L0Distance,
                    L1Distance = source.L1Distance,
                    L2Distance = source.L2Distance,
                    ImpostorDistance = source.ImpostorDistance,
                    AbsoluteCullDistance = source.AbsoluteCullDistance
                };
            }

            lodBuffer.SetData(lodProfiles);

            BlueprintGpu[] blueprints = new BlueprintGpu[Mathf.Max(1, registry.TreeBlueprints.Count)];
            for (int i = 0; i < registry.TreeBlueprints.Count; i++)
            {
                VegetationTreeBlueprintRuntime source = registry.TreeBlueprints[i];
                blueprints[i] = new BlueprintGpu
                {
                    LodProfileIndex = source.LodProfileIndex,
                    BranchPlacementStartIndex = source.BranchPlacementStartIndex,
                    BranchPlacementCount = source.BranchPlacementCount,
                    TrunkFullDrawSlot = source.TrunkFullDrawSlot,
                    TrunkL3DrawSlot = source.TrunkL3DrawSlot,
                    TreeL3DrawSlot = source.TreeL3DrawSlot,
                    ImpostorDrawSlot = source.ImpostorDrawSlot,
                    TreeL3WorkCost = source.TreeL3WorkCost,
                    ImpostorWorkCost = source.ImpostorWorkCost,
                    ExpandedTierCostL2 = source.ExpandedTierCostL2,
                    ExpandedTierCostL1 = source.ExpandedTierCostL1,
                    ExpandedTierCostL0 = source.ExpandedTierCostL0
                };
            }

            blueprintBuffer.SetData(blueprints);

            PlacementGpu[] placements = new PlacementGpu[Mathf.Max(1, registry.BlueprintBranchPlacements.Count)];
            for (int i = 0; i < registry.BlueprintBranchPlacements.Count; i++)
            {
                VegetationBlueprintBranchPlacementRuntime source = registry.BlueprintBranchPlacements[i];
                placements[i] = new PlacementGpu
                {
                    LocalToTree = source.LocalToTree,
                    TreeToLocal = source.TreeToLocal,
                    PrototypeIndex = source.PrototypeIndex,
                    LocalBoundsCenter = source.LocalBoundsCenter,
                    BoundingSphereRadius = source.BoundingSphereRadius,
                    LocalBoundsExtents = source.LocalBoundsExtents
                };
            }

            placementBuffer.SetData(placements);

            PrototypeGpu[] prototypes = new PrototypeGpu[Mathf.Max(1, registry.BranchPrototypes.Count)];
            for (int i = 0; i < registry.BranchPrototypes.Count; i++)
            {
                VegetationBranchPrototypeRuntime source = registry.BranchPrototypes[i];
                prototypes[i] = new PrototypeGpu
                {
                    WoodDrawSlotL0 = source.WoodDrawSlotL0,
                    FoliageDrawSlotL0 = source.FoliageDrawSlotL0,
                    WoodDrawSlotL1 = source.WoodDrawSlotL1,
                    CanopyDrawSlotL1 = source.CanopyDrawSlotL1,
                    WoodDrawSlotL2 = source.WoodDrawSlotL2,
                    CanopyDrawSlotL2 = source.CanopyDrawSlotL2,
                    WoodDrawSlotL3 = source.WoodDrawSlotL3,
                    CanopyDrawSlotL3 = source.CanopyDrawSlotL3,
                    PackedLeafTint = source.PackedLeafTint,
                    LocalBoundsCenter = source.LocalBoundsCenter,
                    LocalBoundsExtents = source.LocalBoundsExtents
                };
            }

            prototypeBuffer.SetData(prototypes);

            TreeGpu[] trees = new TreeGpu[Mathf.Max(1, registry.TreeInstances.Count)];
            for (int i = 0; i < registry.TreeInstances.Count; i++)
            {
                VegetationTreeInstanceRuntime source = registry.TreeInstances[i];
                trees[i] = new TreeGpu
                {
                    SphereCenterWorld = source.SphereCenterWorld,
                    BoundingSphereRadius = source.BoundingSphereRadius,
                    CellIndex = source.CellIndex,
                    BlueprintIndex = source.BlueprintIndex,
                    WorldBounds = ToBoundsGpu(source.WorldBounds),
                    UploadInstanceData = source.UploadInstanceData
                };
            }

            treeBuffer.SetData(trees);

            SlotGpu[] slots = new SlotGpu[Mathf.Max(1, registry.DrawSlots.Count)];
            for (int i = 0; i < registry.DrawSlots.Count; i++)
            {
                VegetationDrawSlot drawSlot = registry.DrawSlots[i];
                slots[i] = new SlotGpu
                {
                    IndexCountPerInstance = drawSlot.IndexCountPerInstance,
                    StartIndexLocation = drawSlot.StartIndexLocation,
                    BaseVertexLocation = drawSlot.BaseVertexLocation
                };
            }

            slotMetadataBuffer.SetData(slots);
        }

        private void BindBuffers()
        {
            classifyShader.SetBuffer(resetFrameStateKernel, "_ExpandedBranchWorkItems", expandedBranchWorkItemBuffer);
            classifyShader.SetBuffer(resetFrameStateKernel, "_ExpandedBranchWorkItemCount", expandedBranchWorkItemCountBuffer);
            classifyShader.SetBuffer(resetFrameStateKernel, "_FrameStats", frameStatsBuffer);

            classifyShader.SetBuffer(classifyCellsKernel, "_Cells", cellBuffer);
            classifyShader.SetBuffer(classifyCellsKernel, "_CellVisibility", cellVisibilityBuffer);

            classifyShader.SetBuffer(classifyTreesKernel, "_Trees", treeBuffer);
            classifyShader.SetBuffer(classifyTreesKernel, "_Blueprints", blueprintBuffer);
            classifyShader.SetBuffer(classifyTreesKernel, "_LodProfiles", lodBuffer);
            classifyShader.SetBuffer(classifyTreesKernel, "_CellVisibility", cellVisibilityBuffer);
            classifyShader.SetBuffer(classifyTreesKernel, "_TreeVisibility", treeVisibilityBuffer);

            classifyShader.SetBuffer(acceptTreeTiersKernel, "_Trees", treeBuffer);
            classifyShader.SetBuffer(acceptTreeTiersKernel, "_Blueprints", blueprintBuffer);
            classifyShader.SetBuffer(acceptTreeTiersKernel, "_TreeVisibility", treeVisibilityBuffer);
            classifyShader.SetBuffer(acceptTreeTiersKernel, "_FrameStats", frameStatsBuffer);
            classifyShader.SetBuffer(acceptTreeTiersKernel, "_PriorityRingTreeCounts", priorityRingTreeCountBuffer);
            classifyShader.SetBuffer(acceptTreeTiersKernel, "_PriorityRingOffsets", priorityRingOffsetsBuffer);
            classifyShader.SetBuffer(acceptTreeTiersKernel, "_PriorityOrderedVisibleTreeIndices", priorityOrderedVisibleTreeIndicesBuffer);

            classifyShader.SetBuffer(generateExpandedBranchWorkItemsKernel, "_Trees", treeBuffer);
            classifyShader.SetBuffer(generateExpandedBranchWorkItemsKernel, "_Blueprints", blueprintBuffer);
            classifyShader.SetBuffer(generateExpandedBranchWorkItemsKernel, "_TreeVisibility", treeVisibilityBuffer);
            classifyShader.SetBuffer(generateExpandedBranchWorkItemsKernel, "_ExpandedBranchWorkItems", expandedBranchWorkItemBuffer);
            classifyShader.SetBuffer(generateExpandedBranchWorkItemsKernel, "_ExpandedBranchWorkItemCount", expandedBranchWorkItemCountBuffer);
            classifyShader.SetBuffer(generateExpandedBranchWorkItemsKernel, "_FrameStats", frameStatsBuffer);

            classifyShader.SetBuffer(buildExpandedBranchDispatchArgsKernel, "_ExpandedBranchWorkItemCount", expandedBranchWorkItemCountBuffer);
            classifyShader.SetBuffer(buildExpandedBranchDispatchArgsKernel, "_ExpandedBranchDispatchArgs", expandedBranchDispatchArgsBuffer);

            classifyShader.SetBuffer(resetSlotCountsKernel, "_SlotRequestedInstanceCounts", slotRequestedInstanceCountBuffer);
            classifyShader.SetBuffer(resetSlotCountsKernel, "_SlotEmittedInstanceCounts", slotEmittedInstanceCountBuffer);
            classifyShader.SetBuffer(resetSlotCountsKernel, "_SlotPackedStarts", slotPackedStartsBuffer);

            classifyShader.SetBuffer(countTreesKernel, "_Trees", treeBuffer);
            classifyShader.SetBuffer(countTreesKernel, "_Blueprints", blueprintBuffer);
            classifyShader.SetBuffer(countTreesKernel, "_TreeVisibility", treeVisibilityBuffer);
            classifyShader.SetBuffer(countTreesKernel, "_SlotRequestedInstanceCounts", slotRequestedInstanceCountBuffer);

            classifyShader.SetBuffer(countExpandedBranchesKernel, "_Trees", treeBuffer);
            classifyShader.SetBuffer(countExpandedBranchesKernel, "_Placements", placementBuffer);
            classifyShader.SetBuffer(countExpandedBranchesKernel, "_Prototypes", prototypeBuffer);
            classifyShader.SetBuffer(countExpandedBranchesKernel, "_ExpandedBranchWorkItems", expandedBranchWorkItemBuffer);
            classifyShader.SetBuffer(countExpandedBranchesKernel, "_ExpandedBranchWorkItemCount", expandedBranchWorkItemCountBuffer);
            classifyShader.SetBuffer(countExpandedBranchesKernel, "_SlotRequestedInstanceCounts", slotRequestedInstanceCountBuffer);

            classifyShader.SetBuffer(clampRequestedSlotCountsKernel, "_SlotRequestedInstanceCounts", slotRequestedInstanceCountBuffer);
            classifyShader.SetBuffer(clampRequestedSlotCountsKernel, "_FrameStats", frameStatsBuffer);

            classifyShader.SetBuffer(buildSlotStartsKernel, "_SlotRequestedInstanceCounts", slotRequestedInstanceCountBuffer);
            classifyShader.SetBuffer(buildSlotStartsKernel, "_SlotPackedStarts", slotPackedStartsBuffer);

            classifyShader.SetBuffer(emitTreesKernel, "_Trees", treeBuffer);
            classifyShader.SetBuffer(emitTreesKernel, "_Blueprints", blueprintBuffer);
            classifyShader.SetBuffer(emitTreesKernel, "_TreeVisibility", treeVisibilityBuffer);
            classifyShader.SetBuffer(emitTreesKernel, "_SlotRequestedInstanceCounts", slotRequestedInstanceCountBuffer);
            classifyShader.SetBuffer(emitTreesKernel, "_SlotPackedStarts", slotPackedStartsBuffer);
            classifyShader.SetBuffer(emitTreesKernel, "_SlotEmittedInstanceCounts", slotEmittedInstanceCountBuffer);
            classifyShader.SetBuffer(emitTreesKernel, "_VisibleInstances", residentInstanceBuffer);
            classifyShader.SetBuffer(emitTreesKernel, "_FrameStats", frameStatsBuffer);

            classifyShader.SetBuffer(emitExpandedBranchesKernel, "_Trees", treeBuffer);
            classifyShader.SetBuffer(emitExpandedBranchesKernel, "_Placements", placementBuffer);
            classifyShader.SetBuffer(emitExpandedBranchesKernel, "_Prototypes", prototypeBuffer);
            classifyShader.SetBuffer(emitExpandedBranchesKernel, "_ExpandedBranchWorkItems", expandedBranchWorkItemBuffer);
            classifyShader.SetBuffer(emitExpandedBranchesKernel, "_ExpandedBranchWorkItemCount", expandedBranchWorkItemCountBuffer);
            classifyShader.SetBuffer(emitExpandedBranchesKernel, "_SlotRequestedInstanceCounts", slotRequestedInstanceCountBuffer);
            classifyShader.SetBuffer(emitExpandedBranchesKernel, "_SlotPackedStarts", slotPackedStartsBuffer);
            classifyShader.SetBuffer(emitExpandedBranchesKernel, "_SlotEmittedInstanceCounts", slotEmittedInstanceCountBuffer);
            classifyShader.SetBuffer(emitExpandedBranchesKernel, "_VisibleInstances", residentInstanceBuffer);
            classifyShader.SetBuffer(emitExpandedBranchesKernel, "_FrameStats", frameStatsBuffer);

            classifyShader.SetBuffer(finalizeIndirectArgsKernel, "_Slots", slotMetadataBuffer);
            classifyShader.SetBuffer(finalizeIndirectArgsKernel, "_SlotRequestedInstanceCounts", slotRequestedInstanceCountBuffer);
            classifyShader.SetBuffer(finalizeIndirectArgsKernel, "_SlotPackedStarts", slotPackedStartsBuffer);
            classifyShader.SetBuffer(finalizeIndirectArgsKernel, "_SlotEmittedInstanceCounts", slotEmittedInstanceCountBuffer);
            classifyShader.SetBuffer(finalizeIndirectArgsKernel, "_IndirectArgs", residentArgsBuffer);
        }

        private void UploadDynamicFrameData(
            Vector3 cameraWorldPosition,
            Plane[] frustumPlanes,
            bool allowExpandedTreePromotion,
            bool limitExpandedPromotionToNearTiers)
        {
            for (int i = 0; i < 6; i++)
            {
                Plane plane = frustumPlanes[i];
                frustumPlaneVectors[i] = new Vector4(plane.normal.x, plane.normal.y, plane.normal.z, plane.distance);
            }

            classifyShader.SetVector("_CameraWorldPosition",
                new Vector4(cameraWorldPosition.x, cameraWorldPosition.y, cameraWorldPosition.z, 0f));
            classifyShader.SetVectorArray("_FrustumPlanes", frustumPlaneVectors);
            classifyShader.SetInt("_CellCount", registry.SpatialGrid.Cells.Count);
            classifyShader.SetInt("_TreeCount", registry.TreeInstances.Count);
            classifyShader.SetInt("_LodProfileCount", registry.LodProfiles.Count);
            classifyShader.SetInt("_BlueprintCount", registry.TreeBlueprints.Count);
            classifyShader.SetInt("_PlacementCount", registry.BlueprintBranchPlacements.Count);
            classifyShader.SetInt("_PrototypeCount", registry.BranchPrototypes.Count);
            classifyShader.SetInt("_DrawSlotCount", registry.DrawSlots.Count);
            classifyShader.SetInt("_VisibleInstanceCapacity", visibleInstanceCapacity);
            classifyShader.SetInt("_ExpandedBranchWorkItemCapacity", expandedBranchWorkItemCapacity);
            classifyShader.SetInt("_ApproxWorkUnitCapacity", approxWorkUnitCapacity);
            classifyShader.SetInt("_AllowExpandedTierPromotion", allowExpandedTreePromotion ? 1 : 0);
            classifyShader.SetInt("_LimitExpandedPromotionToNearTiers", limitExpandedPromotionToNearTiers ? 1 : 0);
            classifyShader.SetInt("_PriorityRingCount", priorityRingCount);
        }

        private void DispatchKernel(int kernelIndex, int itemCount)
        {
            int threadGroupCount = Mathf.Max(1, Mathf.CeilToInt(itemCount / (float)ComputeThreadGroupSize));
            classifyShader.Dispatch(kernelIndex, threadGroupCount, 1, 1);
        }

        private void DispatchKernelIndirect(int kernelIndex, ComputeBuffer dispatchArgsBuffer)
        {
            if (dispatchArgsBuffer == null)
            {
                return;
            }

            classifyShader.DispatchIndirect(kernelIndex, dispatchArgsBuffer, 0);
        }

        private static ComputeBuffer CreateStructuredBuffer<T>(int count) where T : struct
        {
            return new ComputeBuffer(count, Marshal.SizeOf<T>());
        }

        private void SchedulePreparedFrameReadbacks(bool captureTelemetry)
        {
            int readbackSequence = ++preparedFrameReadbackSequence;
            if (captureTelemetry && !preparedFrameTelemetryReadbackPending)
            {
                preparedFrameTelemetryReadbackPending = true;
                pendingPreparedFrameTelemetryReadbackSequence = readbackSequence;
                AsyncGPUReadback.Request(frameStatsBuffer, OnPreparedFrameTelemetryReadbackCompleted);
            }

            if (!slotEmissionReadbackPending)
            {
                slotEmissionReadbackPending = true;
                pendingSlotEmissionReadbackSequence = readbackSequence;
                AsyncGPUReadback.Request(slotEmittedInstanceCountBuffer, OnSlotEmissionReadbackCompleted);
            }
        }

        private void OnPreparedFrameTelemetryReadbackCompleted(AsyncGPUReadbackRequest request)
        {
            preparedFrameTelemetryReadbackPending = false;
            if (disposed || request.hasError)
            {
                return;
            }

            NativeArray<uint> data = request.GetData<uint>();
            int copyCount = Math.Min(data.Length, latestPreparedFrameStats.Length);
            for (int i = 0; i < copyCount; i++)
            {
                latestPreparedFrameStats[i] = data[i];
            }

            for (int i = copyCount; i < latestPreparedFrameStats.Length; i++)
            {
                latestPreparedFrameStats[i] = 0u;
            }

            hasLatestPreparedFrameStats = pendingPreparedFrameTelemetryReadbackSequence >= 0;
            pendingPreparedFrameTelemetryReadbackSequence = -1;
        }

        private void OnSlotEmissionReadbackCompleted(AsyncGPUReadbackRequest request)
        {
            slotEmissionReadbackPending = false;
            if (disposed || request.hasError)
            {
                return;
            }

            NativeArray<uint> data = request.GetData<uint>();
            int copyCount = Math.Min(data.Length, latestSlotEmittedCounts.Length);
            int activeSlotCount = 0;
            for (int i = 0; i < copyCount; i++)
            {
                uint emittedCount = data[i];
                latestSlotEmittedCounts[i] = emittedCount;
                if (emittedCount > 0u)
                {
                    activeSlotCount++;
                }
            }

            for (int i = copyCount; i < latestSlotEmittedCounts.Length; i++)
            {
                latestSlotEmittedCounts[i] = 0u;
            }

            latestActiveSlotIndices = new int[activeSlotCount];
            int writeIndex = 0;
            for (int i = 0; i < copyCount; i++)
            {
                if (latestSlotEmittedCounts[i] == 0u)
                {
                    continue;
                }

                latestActiveSlotIndices[writeIndex] = i;
                writeIndex++;
            }

            hasLatestActiveSlotIndices = pendingSlotEmissionReadbackSequence >= 0;
            pendingSlotEmissionReadbackSequence = -1;
        }

        private int CountLatestNonZeroEmittedSlots()
        {
            if (!hasLatestActiveSlotIndices)
            {
                return 0;
            }

            return latestActiveSlotIndices.Length;
        }

        private static int ComputePriorityRingCount(VegetationRuntimeRegistry registry)
        {
            if (registry == null || registry.LodProfiles.Count == 0)
            {
                return 1;
            }

            float maxAbsoluteCullDistance = 0f;
            for (int lodProfileIndex = 0; lodProfileIndex < registry.LodProfiles.Count; lodProfileIndex++)
            {
                maxAbsoluteCullDistance = Mathf.Max(
                    maxAbsoluteCullDistance,
                    registry.LodProfiles[lodProfileIndex].AbsoluteCullDistance);
            }

            return Mathf.Max(1, Mathf.CeilToInt(maxAbsoluteCullDistance * PriorityRingScale) + 1);
        }

        private void ReleaseResources()
        {
            ReleaseComputeBuffer(ref cellBuffer);
            ReleaseComputeBuffer(ref lodBuffer);
            ReleaseComputeBuffer(ref blueprintBuffer);
            ReleaseComputeBuffer(ref placementBuffer);
            ReleaseComputeBuffer(ref prototypeBuffer);
            ReleaseComputeBuffer(ref treeBuffer);
            ReleaseComputeBuffer(ref treeVisibilityBuffer);
            ReleaseComputeBuffer(ref expandedBranchWorkItemBuffer);
            ReleaseComputeBuffer(ref expandedBranchWorkItemCountBuffer);
            ReleaseComputeBuffer(ref expandedBranchDispatchArgsBuffer);
            ReleaseComputeBuffer(ref frameStatsBuffer);
            ReleaseComputeBuffer(ref priorityRingTreeCountBuffer);
            ReleaseComputeBuffer(ref priorityRingOffsetsBuffer);
            ReleaseComputeBuffer(ref priorityOrderedVisibleTreeIndicesBuffer);
            ReleaseComputeBuffer(ref slotMetadataBuffer);
            ReleaseComputeBuffer(ref slotRequestedInstanceCountBuffer);
            ReleaseComputeBuffer(ref slotEmittedInstanceCountBuffer);
            ReleaseComputeBuffer(ref slotPackedStartsBuffer);
            ReleaseComputeBuffer(ref cellVisibilityBuffer);
            ReleaseGraphicsBuffer(ref residentInstanceBuffer);
            ReleaseGraphicsBuffer(ref residentArgsBuffer);
        }

        private static void ReleaseComputeBuffer(ref ComputeBuffer buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.Release();
            buffer = null!;
        }

        private static void ReleaseGraphicsBuffer(ref GraphicsBuffer buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.Release();
            buffer = null!;
        }

        private static BoundsGpu ToBoundsGpu(Bounds bounds)
        {
            return new BoundsGpu
            {
                Center = bounds.center,
                Extents = bounds.extents
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CellGpu
        {
            public Vector3 Center;
            public float Padding0;
            public Vector3 Extents;
            public float Padding1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LodProfileGpu
        {
            public float L0Distance;
            public float L1Distance;
            public float L2Distance;
            public float ImpostorDistance;
            public float AbsoluteCullDistance;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BlueprintGpu
        {
            public int LodProfileIndex;
            public int BranchPlacementStartIndex;
            public int BranchPlacementCount;
            public int TrunkFullDrawSlot;
            public int TrunkL3DrawSlot;
            public int TreeL3DrawSlot;
            public int ImpostorDrawSlot;
            public int TreeL3WorkCost;
            public int ImpostorWorkCost;
            public int ExpandedTierCostL2;
            public int ExpandedTierCostL1;
            public int ExpandedTierCostL0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PlacementGpu
        {
            public Matrix4x4 LocalToTree;
            public Matrix4x4 TreeToLocal;
            public int PrototypeIndex;
            public Vector3 LocalBoundsCenter;
            public float BoundingSphereRadius;
            public Vector3 LocalBoundsExtents;
            public float Padding0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PrototypeGpu
        {
            public int WoodDrawSlotL0;
            public int FoliageDrawSlotL0;
            public int WoodDrawSlotL1;
            public int CanopyDrawSlotL1;
            public int WoodDrawSlotL2;
            public int CanopyDrawSlotL2;
            public int WoodDrawSlotL3;
            public int CanopyDrawSlotL3;
            public uint PackedLeafTint;
            public Vector3 LocalBoundsCenter;
            public Vector3 LocalBoundsExtents;
            public float Padding0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TreeGpu
        {
            public Vector3 SphereCenterWorld;
            public float BoundingSphereRadius;
            public int CellIndex;
            public int BlueprintIndex;
            public BoundsGpu WorldBounds;
            public VegetationIndirectInstanceData UploadInstanceData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TreeVisibilityGpu
        {
            public float TreeDistance;
            public int PriorityRing;
            public int DesiredTier;
            public int AcceptedTier;
            public int AcceptedTierCost;
            public uint Visible;
            public uint Padding0;
            public uint Padding1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BoundsGpu
        {
            public Vector3 Center;
            public float Padding0;
            public Vector3 Extents;
            public float Padding1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SlotGpu
        {
            public uint IndexCountPerInstance;
            public uint StartIndexLocation;
            public int BaseVertexLocation;
            public uint Padding0;
        }
    }
}
