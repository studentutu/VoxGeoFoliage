#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
        private const int FrameStatCount = 9;
        private const int FrameStatVisibleTrees = 0;
        private const int FrameStatAcceptedTreeL3 = 1;
        private const int FrameStatPromotedL2 = 2;
        private const int FrameStatPromotedL1 = 3;
        private const int FrameStatPromotedL0 = 4;
        private const int FrameStatRejectedPromotions = 5;
        private const int FrameStatExpandedTrees = 6;
        private const int FrameStatExpandedBranchWorkItems = 7;
        private const int FrameStatAcceptedTierCostUsage = 8;
        private static readonly ProfilerMarker PrepareResidentFrameMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.PrepareResidentFrame");
        private static readonly ProfilerMarker ResetFrameStateMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.ResetFrameState");
        private static readonly ProfilerMarker CountTreeInstancesMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.CountTreeInstances");
        private static readonly ProfilerMarker CountBranchInstancesMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.CountBranchInstances");
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
        private const int UIntStrideBytes = sizeof(uint);
        private readonly ComputeShader classifyShader;
        private readonly VegetationRuntimeRegistry registry;
        private readonly int resetFrameStateKernel;
        private readonly int classifyCellsKernel;
        private readonly int classifyTreesKernel;
        private readonly int acceptTreeTiersKernel;
        private readonly int generateExpandedBranchWorkItemsKernel;
        private readonly int resetSlotCountsKernel;
        private readonly int countTreesKernel;
        private readonly int countExpandedBranchesKernel;
        private readonly int buildSlotStartsKernel;
        private readonly int emitTreesKernel;
        private readonly int emitExpandedBranchesKernel;
        private readonly int finalizeIndirectArgsKernel;
        private readonly int visibleInstanceCapacity;
        private ComputeBuffer cellBuffer = null!;
        private ComputeBuffer lodBuffer = null!;
        private ComputeBuffer blueprintBuffer = null!;
        private ComputeBuffer placementBuffer = null!;
        private ComputeBuffer prototypeBuffer = null!;
        private ComputeBuffer treeBuffer = null!;
        private ComputeBuffer treeVisibilityBuffer = null!;
        private ComputeBuffer expandedBranchWorkItemBuffer = null!;
        private ComputeBuffer expandedBranchWorkItemCountBuffer = null!;
        private ComputeBuffer frameStatsBuffer = null!;
        private ComputeBuffer slotMetadataBuffer = null!;
        private ComputeBuffer slotRequestedInstanceCountBuffer = null!;
        private ComputeBuffer slotEmittedInstanceCountBuffer = null!;
        private ComputeBuffer slotPackedStartsBuffer = null!;
        private ComputeBuffer cellVisibilityBuffer = null!;
        private GraphicsBuffer residentInstanceBuffer = null!;
        private GraphicsBuffer residentArgsBuffer = null!;
        private readonly Vector4[] frustumPlaneVectors = new Vector4[6];
        private uint[] slotEmittedCountsReadback = Array.Empty<uint>();
        private uint[] frameStatsReadback = Array.Empty<uint>();
        private bool residentFramePrepared;
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
                int nonZeroEmittedSlots,
                long emittedVisibleInstanceCount)
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
                NonZeroEmittedSlots = nonZeroEmittedSlots;
                EmittedVisibleInstanceCount = emittedVisibleInstanceCount;
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
            public int NonZeroEmittedSlots { get; }
            public long EmittedVisibleInstanceCount { get; }
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

        public int ExpandedBranchWorkItemCapacity => visibleInstanceCapacity;

        public long ExpandedBranchWorkItemBufferBytes => checked((long)ExpandedBranchWorkItemCapacity * ExpandedBranchWorkItemStrideBytes);

        public int DrawSlotCount => registry.DrawSlots.Count;

        public int AllocatedDrawSlotBufferElementCount => Mathf.Max(1, DrawSlotCount);

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
            ExpandedBranchWorkItemBufferBytes +
            ExpandedBranchWorkItemCountBufferBytes +
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

        public VegetationGpuDecisionPipeline(ComputeShader classifyShader, VegetationRuntimeRegistry registry, int visibleInstanceCapacity)
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                throw new NotSupportedException(
                    "This runtime does not support compute shaders, so the urgent GPU decision path is unavailable.");
            }

            this.classifyShader = classifyShader ?? throw new ArgumentNullException(nameof(classifyShader));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.visibleInstanceCapacity = Mathf.Max(1, visibleInstanceCapacity);

            try
            {
                resetFrameStateKernel = classifyShader.FindKernel("ResetFrameState");
                classifyCellsKernel = classifyShader.FindKernel("ClassifyCells");
                classifyTreesKernel = classifyShader.FindKernel("ClassifyTrees");
                acceptTreeTiersKernel = classifyShader.FindKernel("AcceptTreeTiers");
                generateExpandedBranchWorkItemsKernel = classifyShader.FindKernel("GenerateExpandedBranchWorkItems");
                resetSlotCountsKernel = classifyShader.FindKernel("ResetSlotCounts");
                countTreesKernel = classifyShader.FindKernel("CountTrees");
                countExpandedBranchesKernel = classifyShader.FindKernel("CountExpandedBranches");
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
                expandedBranchWorkItemBuffer = CreateStructuredBuffer<VegetationBranchDecisionRecord>(this.visibleInstanceCapacity);
                expandedBranchWorkItemCountBuffer = CreateStructuredBuffer<uint>(1);
                frameStatsBuffer = CreateStructuredBuffer<uint>(FrameStatCount);
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
        public void PrepareResidentFrame(Vector3 cameraWorldPosition, Plane[] frustumPlanes, bool allowExpandedTreePromotion)
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

                UploadDynamicFrameData(cameraWorldPosition, frustumPlanes, allowExpandedTreePromotion);

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

                    using (CountBranchInstancesMarker.Auto())
                    {
                        DispatchKernel(countExpandedBranchesKernel, visibleInstanceCapacity);
                    }
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
                        DispatchKernel(emitExpandedBranchesKernel, visibleInstanceCapacity);
                    }
                }

                using (FinalizeIndirectArgsMarker.Auto())
                {
                    DispatchKernel(finalizeIndirectArgsKernel, registry.DrawSlots.Count);
                }

                residentFramePrepared = true;
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
            slotEmittedCountsReadback = Array.Empty<uint>();
            frameStatsReadback = Array.Empty<uint>();
        }

        /// <summary>
        /// [INTEGRATION] Reads back one prepared-frame summary from emitted slot counts and frame stats so diagnostics can inspect urgent acceptance behavior.
        /// </summary>
        public PreparedFrameTelemetry ReadbackPreparedFrameTelemetry()
        {
            if (disposed)
            {
                return new PreparedFrameTelemetry();
            }

            if (!residentFramePrepared)
            {
              return new PreparedFrameTelemetry();
            }

            EnsureFrameStatsReadbackCapacity();
            frameStatsBuffer.GetData(frameStatsReadback, 0, 0, FrameStatCount);
            ReadbackSlotEmittedCounts();

            int nonZeroEmittedSlots = 0;
            long emittedVisibleInstanceCount = 0L;
            for (int slotIndex = 0; slotIndex < registry.DrawSlots.Count; slotIndex++)
            {
                uint emittedCount = slotEmittedCountsReadback[slotIndex];
                if (emittedCount > 0u)
                {
                    nonZeroEmittedSlots++;
                }

                emittedVisibleInstanceCount += emittedCount;
            }

            return new PreparedFrameTelemetry(
                (int)frameStatsReadback[FrameStatVisibleTrees],
                (int)frameStatsReadback[FrameStatAcceptedTreeL3],
                (int)frameStatsReadback[FrameStatPromotedL2],
                (int)frameStatsReadback[FrameStatPromotedL1],
                (int)frameStatsReadback[FrameStatPromotedL0],
                (int)frameStatsReadback[FrameStatRejectedPromotions],
                (int)frameStatsReadback[FrameStatExpandedTrees],
                (int)frameStatsReadback[FrameStatExpandedBranchWorkItems],
                (int)frameStatsReadback[FrameStatAcceptedTierCostUsage],
                nonZeroEmittedSlots,
                emittedVisibleInstanceCount);
        }

        /// <summary>
        /// [INTEGRATION] Reads back the emitted instance count for every non-zero draw slot in the prepared frame so diagnostics can estimate draw workload.
        /// </summary>
        public void ReadbackPreparedFrameSlotTelemetry(List<PreparedFrameSlotTelemetry> target)
        {
            if (disposed)
            {
                return;
            }
            
            if (target == null)
            {
                return;
            }

            if (!residentFramePrepared)
            {
               return;
            }

            target.Clear();
            ReadbackSlotEmittedCounts();
            for (int slotIndex = 0; slotIndex < registry.DrawSlots.Count; slotIndex++)
            {
                uint emittedCount = slotEmittedCountsReadback[slotIndex];
                if (emittedCount == 0u)
                {
                    continue;
                }

                target.Add(new PreparedFrameSlotTelemetry(slotIndex, emittedCount));
            }
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

            classifyShader.SetBuffer(generateExpandedBranchWorkItemsKernel, "_Trees", treeBuffer);
            classifyShader.SetBuffer(generateExpandedBranchWorkItemsKernel, "_Blueprints", blueprintBuffer);
            classifyShader.SetBuffer(generateExpandedBranchWorkItemsKernel, "_TreeVisibility", treeVisibilityBuffer);
            classifyShader.SetBuffer(generateExpandedBranchWorkItemsKernel, "_ExpandedBranchWorkItems", expandedBranchWorkItemBuffer);
            classifyShader.SetBuffer(generateExpandedBranchWorkItemsKernel, "_ExpandedBranchWorkItemCount", expandedBranchWorkItemCountBuffer);
            classifyShader.SetBuffer(generateExpandedBranchWorkItemsKernel, "_FrameStats", frameStatsBuffer);

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

            classifyShader.SetBuffer(buildSlotStartsKernel, "_SlotRequestedInstanceCounts", slotRequestedInstanceCountBuffer);
            classifyShader.SetBuffer(buildSlotStartsKernel, "_SlotPackedStarts", slotPackedStartsBuffer);

            classifyShader.SetBuffer(emitTreesKernel, "_Trees", treeBuffer);
            classifyShader.SetBuffer(emitTreesKernel, "_Blueprints", blueprintBuffer);
            classifyShader.SetBuffer(emitTreesKernel, "_TreeVisibility", treeVisibilityBuffer);
            classifyShader.SetBuffer(emitTreesKernel, "_SlotPackedStarts", slotPackedStartsBuffer);
            classifyShader.SetBuffer(emitTreesKernel, "_SlotEmittedInstanceCounts", slotEmittedInstanceCountBuffer);
            classifyShader.SetBuffer(emitTreesKernel, "_VisibleInstances", residentInstanceBuffer);

            classifyShader.SetBuffer(emitExpandedBranchesKernel, "_Trees", treeBuffer);
            classifyShader.SetBuffer(emitExpandedBranchesKernel, "_Placements", placementBuffer);
            classifyShader.SetBuffer(emitExpandedBranchesKernel, "_Prototypes", prototypeBuffer);
            classifyShader.SetBuffer(emitExpandedBranchesKernel, "_ExpandedBranchWorkItems", expandedBranchWorkItemBuffer);
            classifyShader.SetBuffer(emitExpandedBranchesKernel, "_ExpandedBranchWorkItemCount", expandedBranchWorkItemCountBuffer);
            classifyShader.SetBuffer(emitExpandedBranchesKernel, "_SlotPackedStarts", slotPackedStartsBuffer);
            classifyShader.SetBuffer(emitExpandedBranchesKernel, "_SlotEmittedInstanceCounts", slotEmittedInstanceCountBuffer);
            classifyShader.SetBuffer(emitExpandedBranchesKernel, "_VisibleInstances", residentInstanceBuffer);

            classifyShader.SetBuffer(finalizeIndirectArgsKernel, "_Slots", slotMetadataBuffer);
            classifyShader.SetBuffer(finalizeIndirectArgsKernel, "_SlotPackedStarts", slotPackedStartsBuffer);
            classifyShader.SetBuffer(finalizeIndirectArgsKernel, "_SlotEmittedInstanceCounts", slotEmittedInstanceCountBuffer);
            classifyShader.SetBuffer(finalizeIndirectArgsKernel, "_IndirectArgs", residentArgsBuffer);
        }

        private void UploadDynamicFrameData(Vector3 cameraWorldPosition, Plane[] frustumPlanes, bool allowExpandedTreePromotion)
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
            classifyShader.SetInt("_ExpandedBranchWorkItemCapacity", visibleInstanceCapacity);
            classifyShader.SetInt("_AllowExpandedTierPromotion", allowExpandedTreePromotion ? 1 : 0);
        }

        private void DispatchKernel(int kernelIndex, int itemCount)
        {
            const int threadGroupSize = 64;
            int threadGroupCount = Mathf.Max(1, Mathf.CeilToInt(itemCount / (float)threadGroupSize));
            classifyShader.Dispatch(kernelIndex, threadGroupCount, 1, 1);
        }

        private static ComputeBuffer CreateStructuredBuffer<T>(int count) where T : struct
        {
            return new ComputeBuffer(count, Marshal.SizeOf<T>());
        }

        private void EnsureSlotEmittedCountsReadbackCapacity(int requiredSlotCount)
        {
            if (slotEmittedCountsReadback.Length >= requiredSlotCount)
            {
                return;
            }

            slotEmittedCountsReadback = new uint[requiredSlotCount];
        }

        private void EnsureFrameStatsReadbackCapacity()
        {
            if (frameStatsReadback.Length >= FrameStatCount)
            {
                return;
            }

            frameStatsReadback = new uint[FrameStatCount];
        }

        private void ReadbackSlotEmittedCounts()
        {
            EnsureSlotEmittedCountsReadbackCapacity(registry.DrawSlots.Count);
            if (registry.DrawSlots.Count <= 0)
            {
                return;
            }

            slotEmittedInstanceCountBuffer.GetData(slotEmittedCountsReadback, 0, 0, registry.DrawSlots.Count);
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
            ReleaseComputeBuffer(ref frameStatsBuffer);
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
