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
    /// GPU-resident vegetation classification and indirect-emission pipeline for the frozen runtime contracts.
    /// Current shipped limitation: branch kernels still dispatch across the full scene branch array, and shell-tier work still scans selected-tier nodes linearly.
    /// </summary>
    public sealed class VegetationGpuDecisionPipeline : IDisposable
    {
        private static readonly ProfilerMarker PrepareResidentFrameMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.PrepareResidentFrame");
        private static readonly ProfilerMarker ResetSlotCountsMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.ResetSlotCounts");
        private static readonly ProfilerMarker CountTreeInstancesMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.CountTreeInstances");
        private static readonly ProfilerMarker CountBranchInstancesMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.CountBranchInstances");
        private static readonly ProfilerMarker BuildSlotStartsMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.BuildSlotStarts");
        private static readonly ProfilerMarker EmitTreeInstancesMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.EmitTreeInstances");
        private static readonly ProfilerMarker EmitBranchInstancesMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.EmitBranchInstances");
        private static readonly ProfilerMarker FinalizeIndirectArgsMarker = new ProfilerMarker("VoxGeoFol.VegetationGpuDecisionPipeline.FinalizeIndirectArgs");
        private readonly ComputeShader classifyShader;
        private readonly VegetationRuntimeRegistry registry;
        private readonly int classifyCellsKernel;
        private readonly int classifyTreesKernel;
        private readonly int classifyBranchesKernel;
        private readonly int resetSlotCountsKernel;
        private readonly int countTreesKernel;
        private readonly int countBranchesKernel;
        private readonly int buildSlotStartsKernel;
        private readonly int emitTreesKernel;
        private readonly int emitBranchesKernel;
        private readonly int finalizeIndirectArgsKernel;
        private readonly int visibleInstanceCapacity;
        private ComputeBuffer cellBuffer = null!;
        private ComputeBuffer lodBuffer = null!;
        private ComputeBuffer treeBuffer = null!;
        private ComputeBuffer branchBuffer = null!;
        private ComputeBuffer prototypeBuffer = null!;
        private ComputeBuffer shellNodesL1Buffer = null!;
        private ComputeBuffer shellNodesL2Buffer = null!;
        private ComputeBuffer shellNodesL3Buffer = null!;
        private ComputeBuffer slotMetadataBuffer = null!;
        private ComputeBuffer slotRequestedInstanceCountBuffer = null!;
        private ComputeBuffer slotEmittedInstanceCountBuffer = null!;
        private ComputeBuffer slotPackedStartsBuffer = null!;
        private ComputeBuffer cellVisibilityBuffer = null!;
        private ComputeBuffer treeModesBuffer = null!;
        private ComputeBuffer branchDecisionBuffer = null!;
        private GraphicsBuffer residentInstanceBuffer = null!;
        private GraphicsBuffer residentArgsBuffer = null!;
        private readonly Vector4[] frustumPlaneVectors = new Vector4[6];
        private bool residentFramePrepared;
        private bool disposed;

        public GraphicsBuffer ResidentInstanceBuffer => residentInstanceBuffer;

        public GraphicsBuffer ResidentArgsBuffer => residentArgsBuffer;

        public ComputeBuffer ResidentSlotPackedStartsBuffer => slotPackedStartsBuffer;

        public bool HasResidentFramePrepared => residentFramePrepared;

        public VegetationGpuDecisionPipeline(ComputeShader classifyShader, VegetationRuntimeRegistry registry, int visibleInstanceCapacity)
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                throw new NotSupportedException(
                    "This runtime does not support compute shaders, so the Phase D GPU decision path is unavailable.");
            }

            this.classifyShader = classifyShader ?? throw new ArgumentNullException(nameof(classifyShader));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.visibleInstanceCapacity = Mathf.Max(1, visibleInstanceCapacity);

            try
            {
                classifyCellsKernel = classifyShader.FindKernel("ClassifyCells");
                classifyTreesKernel = classifyShader.FindKernel("ClassifyTrees");
                classifyBranchesKernel = classifyShader.FindKernel("ClassifyBranches");
                resetSlotCountsKernel = classifyShader.FindKernel("ResetSlotCounts");
                countTreesKernel = classifyShader.FindKernel("CountTrees");
                countBranchesKernel = classifyShader.FindKernel("CountBranches");
                buildSlotStartsKernel = classifyShader.FindKernel("BuildSlotStarts");
                emitTreesKernel = classifyShader.FindKernel("EmitTrees");
                emitBranchesKernel = classifyShader.FindKernel("EmitBranches");
                finalizeIndirectArgsKernel = classifyShader.FindKernel("FinalizeIndirectArgs");
            }
            catch (ArgumentException exception)
            {
                throw new NotSupportedException(
                    "VegetationClassify.compute imported without the expected kernels. The Phase D GPU decision path is unavailable in this Unity environment.",
                    exception);
            }

            try
            {
                cellBuffer = CreateStructuredBuffer<CellGpu>(Mathf.Max(1, registry.SpatialGrid.Cells.Count));
                lodBuffer = CreateStructuredBuffer<LodProfileGpu>(Mathf.Max(1, registry.LodProfiles.Count));
                treeBuffer = CreateStructuredBuffer<TreeGpu>(Mathf.Max(1, registry.TreeInstances.Count));
                branchBuffer = CreateStructuredBuffer<BranchGpu>(Mathf.Max(1, registry.SceneBranches.Count));
                prototypeBuffer = CreateStructuredBuffer<PrototypeGpu>(Mathf.Max(1, registry.BranchPrototypes.Count));
                shellNodesL1Buffer = CreateStructuredBuffer<ShellNodeGpu>(Mathf.Max(1, registry.ShellNodesL1.Count));
                shellNodesL2Buffer = CreateStructuredBuffer<ShellNodeGpu>(Mathf.Max(1, registry.ShellNodesL2.Count));
                shellNodesL3Buffer = CreateStructuredBuffer<ShellNodeGpu>(Mathf.Max(1, registry.ShellNodesL3.Count));
                slotMetadataBuffer = CreateStructuredBuffer<SlotGpu>(Mathf.Max(1, registry.DrawSlots.Count));
                slotRequestedInstanceCountBuffer = CreateStructuredBuffer<uint>(Mathf.Max(1, registry.DrawSlots.Count));
                slotEmittedInstanceCountBuffer = CreateStructuredBuffer<uint>(Mathf.Max(1, registry.DrawSlots.Count));
                slotPackedStartsBuffer = CreateStructuredBuffer<uint>(Mathf.Max(1, registry.DrawSlots.Count));
                cellVisibilityBuffer = CreateStructuredBuffer<uint>(Mathf.Max(1, registry.SpatialGrid.Cells.Count));
                treeModesBuffer = CreateStructuredBuffer<int>(Mathf.Max(1, registry.TreeInstances.Count));
                branchDecisionBuffer =
                    CreateStructuredBuffer<VegetationBranchDecisionRecord>(Mathf.Max(1, registry.SceneBranches.Count));
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
        /// [INTEGRATION] Executes the full GPU-resident classification and decode path into indirect draw resources without CPU readback.
        /// </summary>
        public void PrepareResidentFrame(Vector3 cameraWorldPosition, Plane[] frustumPlanes)
        {
            using (PrepareResidentFrameMarker.Auto())
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(VegetationGpuDecisionPipeline));
                }

                if (frustumPlanes == null)
                {
                    throw new ArgumentNullException(nameof(frustumPlanes));
                }

                if (frustumPlanes.Length < 6)
                {
                    throw new ArgumentException("Phase D GPU frustum classification requires six frustum planes.", nameof(frustumPlanes));
                }

                UploadDynamicFrameData(cameraWorldPosition, frustumPlanes);
                DispatchKernel(classifyCellsKernel, registry.SpatialGrid.Cells.Count);
                DispatchKernel(classifyTreesKernel, registry.TreeInstances.Count);
                DispatchKernel(classifyBranchesKernel, registry.SceneBranches.Count);

                using (ResetSlotCountsMarker.Auto())
                {
                    DispatchKernel(resetSlotCountsKernel, registry.DrawSlots.Count);
                }

                using (CountTreeInstancesMarker.Auto())
                {
                    DispatchKernel(countTreesKernel, registry.TreeInstances.Count);
                }

                using (CountBranchInstancesMarker.Auto())
                {
                    DispatchKernel(countBranchesKernel, registry.SceneBranches.Count);
                }

                using (BuildSlotStartsMarker.Auto())
                {
                    DispatchKernel(buildSlotStartsKernel, 1);
                }

                using (EmitTreeInstancesMarker.Auto())
                {
                    DispatchKernel(emitTreesKernel, registry.TreeInstances.Count);
                }

                using (EmitBranchInstancesMarker.Auto())
                {
                    DispatchKernel(emitBranchesKernel, registry.SceneBranches.Count);
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

            TreeGpu[] trees = new TreeGpu[Mathf.Max(1, registry.TreeInstances.Count)];
            for (int i = 0; i < registry.TreeInstances.Count; i++)
            {
                VegetationTreeInstanceRuntime tree = registry.TreeInstances[i];
                VegetationTreeBlueprintRuntime blueprint = registry.TreeBlueprints[tree.BlueprintIndex];
                trees[i] = new TreeGpu
                {
                    SphereCenterWorld = tree.SphereCenterWorld,
                    BoundingSphereRadius = tree.BoundingSphereRadius,
                    CellIndex = tree.CellIndex,
                    LodProfileIndex = blueprint.LodProfileIndex,
                    SceneBranchStartIndex = tree.SceneBranchStartIndex,
                    SceneBranchCount = tree.SceneBranchCount,
                    WorldBounds = ToBoundsGpu(tree.WorldBounds),
                    TrunkFullWorldBounds = ToBoundsGpu(tree.TrunkFullWorldBounds),
                    TrunkL3WorldBounds = ToBoundsGpu(tree.TrunkL3WorldBounds),
                    ImpostorWorldBounds = ToBoundsGpu(tree.ImpostorWorldBounds),
                    TrunkFullDrawSlot = blueprint.TrunkFullDrawSlot,
                    TrunkL3DrawSlot = blueprint.TrunkL3DrawSlot,
                    ImpostorDrawSlot = blueprint.ImpostorDrawSlot,
                    UploadInstanceData = tree.UploadInstanceData
                };
            }

            treeBuffer.SetData(trees);

            BranchGpu[] branches = new BranchGpu[Mathf.Max(1, registry.SceneBranches.Count)];
            for (int i = 0; i < registry.SceneBranches.Count; i++)
            {
                VegetationSceneBranchRuntime branch = registry.SceneBranches[i];
                branches[i] = new BranchGpu
                {
                    TreeIndex = branch.TreeIndex,
                    BranchPlacementIndex = branch.BranchPlacementIndex,
                    PrototypeIndex = branch.PrototypeIndex,
                    SphereCenterWorld = branch.SphereCenterWorld,
                    BoundingSphereRadius = branch.BoundingSphereRadius,
                    LocalToWorld = branch.LocalToWorld,
                    WoodDrawSlotL0 = branch.WoodDrawSlotL0,
                    WoodDrawSlotL1 = branch.WoodDrawSlotL1,
                    WoodDrawSlotL2 = branch.WoodDrawSlotL2,
                    WoodDrawSlotL3 = branch.WoodDrawSlotL3,
                    FoliageDrawSlotL0 = branch.FoliageDrawSlotL0,
                    WoodUploadInstanceData = branch.WoodUploadInstanceData,
                    FoliageUploadInstanceData = branch.FoliageUploadInstanceData
                };
            }

            branchBuffer.SetData(branches);

            PrototypeGpu[] prototypes = new PrototypeGpu[Mathf.Max(1, registry.BranchPrototypes.Count)];
            for (int i = 0; i < registry.BranchPrototypes.Count; i++)
            {
                VegetationBranchPrototypeRuntime prototype = registry.BranchPrototypes[i];
                prototypes[i] = new PrototypeGpu
                {
                    ShellNodeStartIndexL1 = prototype.ShellNodeStartIndexL1,
                    ShellNodeCountL1 = prototype.ShellNodeCountL1,
                    ShellNodeStartIndexL2 = prototype.ShellNodeStartIndexL2,
                    ShellNodeCountL2 = prototype.ShellNodeCountL2,
                    ShellNodeStartIndexL3 = prototype.ShellNodeStartIndexL3,
                    ShellNodeCountL3 = prototype.ShellNodeCountL3,
                    LocalBoundsCenter = prototype.LocalBoundsCenter,
                    LocalBoundsExtents = prototype.LocalBoundsExtents
                };
            }

            prototypeBuffer.SetData(prototypes);
            UploadShellNodes(shellNodesL1Buffer, registry.ShellNodesL1);
            UploadShellNodes(shellNodesL2Buffer, registry.ShellNodesL2);
            UploadShellNodes(shellNodesL3Buffer, registry.ShellNodesL3);

            SlotGpu[] slots = new SlotGpu[Mathf.Max(1, registry.DrawSlots.Count)];
            for (int i = 0; i < registry.DrawSlots.Count; i++)
            {
                VegetationDrawSlot drawSlot = registry.DrawSlots[i];
                slots[i] = new SlotGpu
                {
                    IndexCountPerInstance = drawSlot.IndexCountPerInstance,
                    StartIndexLocation = drawSlot.StartIndexLocation,
                    BaseVertexIndex = checked((uint)drawSlot.BaseVertexLocation)
                };
            }

            slotMetadataBuffer.SetData(slots);
        }

        private void UploadShellNodes(ComputeBuffer targetBuffer,
            IReadOnlyList<VegetationBranchShellNodeRuntimeBfs> source)
        {
            ShellNodeGpu[] nodes = new ShellNodeGpu[Mathf.Max(1, source.Count)];
            for (int i = 0; i < source.Count; i++)
            {
                VegetationBranchShellNodeRuntimeBfs node = source[i];
                nodes[i] = new ShellNodeGpu
                {
                    LocalCenter = node.LocalCenter,
                    LocalExtents = node.LocalExtents,
                    FirstChildIndex = node.FirstChildIndex,
                    ChildMask = node.ChildMask,
                    ShellDrawSlot = node.ShellDrawSlot
                };
            }

            targetBuffer.SetData(nodes);
        }

        private void BindBuffers()
        {
            classifyShader.SetBuffer(classifyCellsKernel, "_Cells", cellBuffer);
            classifyShader.SetBuffer(classifyCellsKernel, "_CellVisibility", cellVisibilityBuffer);

            classifyShader.SetBuffer(classifyTreesKernel, "_Trees", treeBuffer);
            classifyShader.SetBuffer(classifyTreesKernel, "_LodProfiles", lodBuffer);
            classifyShader.SetBuffer(classifyTreesKernel, "_CellVisibility", cellVisibilityBuffer);
            classifyShader.SetBuffer(classifyTreesKernel, "_TreeModes", treeModesBuffer);

            classifyShader.SetBuffer(classifyBranchesKernel, "_Trees", treeBuffer);
            classifyShader.SetBuffer(classifyBranchesKernel, "_Branches", branchBuffer);
            classifyShader.SetBuffer(classifyBranchesKernel, "_LodProfiles", lodBuffer);
            classifyShader.SetBuffer(classifyBranchesKernel, "_TreeModes", treeModesBuffer);
            classifyShader.SetBuffer(classifyBranchesKernel, "_BranchDecisions", branchDecisionBuffer);

            classifyShader.SetBuffer(resetSlotCountsKernel, "_SlotRequestedInstanceCounts", slotRequestedInstanceCountBuffer);
            classifyShader.SetBuffer(resetSlotCountsKernel, "_SlotEmittedInstanceCounts", slotEmittedInstanceCountBuffer);
            classifyShader.SetBuffer(resetSlotCountsKernel, "_SlotPackedStarts", slotPackedStartsBuffer);

            classifyShader.SetBuffer(countTreesKernel, "_Trees", treeBuffer);
            classifyShader.SetBuffer(countTreesKernel, "_BranchDecisions", branchDecisionBuffer);
            classifyShader.SetBuffer(countTreesKernel, "_TreeModes", treeModesBuffer);
            classifyShader.SetBuffer(countTreesKernel, "_SlotRequestedInstanceCounts", slotRequestedInstanceCountBuffer);

            classifyShader.SetBuffer(countBranchesKernel, "_Branches", branchBuffer);
            classifyShader.SetBuffer(countBranchesKernel, "_Prototypes", prototypeBuffer);
            classifyShader.SetBuffer(countBranchesKernel, "_ShellNodesL1", shellNodesL1Buffer);
            classifyShader.SetBuffer(countBranchesKernel, "_ShellNodesL2", shellNodesL2Buffer);
            classifyShader.SetBuffer(countBranchesKernel, "_ShellNodesL3", shellNodesL3Buffer);
            classifyShader.SetBuffer(countBranchesKernel, "_BranchDecisions", branchDecisionBuffer);
            classifyShader.SetBuffer(countBranchesKernel, "_SlotRequestedInstanceCounts", slotRequestedInstanceCountBuffer);

            classifyShader.SetBuffer(buildSlotStartsKernel, "_SlotRequestedInstanceCounts", slotRequestedInstanceCountBuffer);
            classifyShader.SetBuffer(buildSlotStartsKernel, "_SlotPackedStarts", slotPackedStartsBuffer);

            classifyShader.SetBuffer(emitTreesKernel, "_Trees", treeBuffer);
            classifyShader.SetBuffer(emitTreesKernel, "_BranchDecisions", branchDecisionBuffer);
            classifyShader.SetBuffer(emitTreesKernel, "_TreeModes", treeModesBuffer);
            classifyShader.SetBuffer(emitTreesKernel, "_SlotPackedStarts", slotPackedStartsBuffer);
            classifyShader.SetBuffer(emitTreesKernel, "_SlotEmittedInstanceCounts", slotEmittedInstanceCountBuffer);
            classifyShader.SetBuffer(emitTreesKernel, "_VisibleInstances", residentInstanceBuffer);

            classifyShader.SetBuffer(emitBranchesKernel, "_Branches", branchBuffer);
            classifyShader.SetBuffer(emitBranchesKernel, "_Prototypes", prototypeBuffer);
            classifyShader.SetBuffer(emitBranchesKernel, "_ShellNodesL1", shellNodesL1Buffer);
            classifyShader.SetBuffer(emitBranchesKernel, "_ShellNodesL2", shellNodesL2Buffer);
            classifyShader.SetBuffer(emitBranchesKernel, "_ShellNodesL3", shellNodesL3Buffer);
            classifyShader.SetBuffer(emitBranchesKernel, "_BranchDecisions", branchDecisionBuffer);
            classifyShader.SetBuffer(emitBranchesKernel, "_SlotPackedStarts", slotPackedStartsBuffer);
            classifyShader.SetBuffer(emitBranchesKernel, "_SlotEmittedInstanceCounts", slotEmittedInstanceCountBuffer);
            classifyShader.SetBuffer(emitBranchesKernel, "_VisibleInstances", residentInstanceBuffer);

            classifyShader.SetBuffer(finalizeIndirectArgsKernel, "_Slots", slotMetadataBuffer);
            classifyShader.SetBuffer(finalizeIndirectArgsKernel, "_SlotPackedStarts", slotPackedStartsBuffer);
            classifyShader.SetBuffer(finalizeIndirectArgsKernel, "_SlotEmittedInstanceCounts", slotEmittedInstanceCountBuffer);
            classifyShader.SetBuffer(finalizeIndirectArgsKernel, "_IndirectArgs", residentArgsBuffer);
        }

        private void UploadDynamicFrameData(Vector3 cameraWorldPosition, Plane[] frustumPlanes)
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
            classifyShader.SetInt("_BranchCount", registry.SceneBranches.Count);
            classifyShader.SetInt("_DrawSlotCount", registry.DrawSlots.Count);
            classifyShader.SetInt("_VisibleInstanceCapacity", visibleInstanceCapacity);
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

        private void ReleaseResources()
        {
            ReleaseComputeBuffer(ref cellBuffer);
            ReleaseComputeBuffer(ref lodBuffer);
            ReleaseComputeBuffer(ref treeBuffer);
            ReleaseComputeBuffer(ref branchBuffer);
            ReleaseComputeBuffer(ref prototypeBuffer);
            ReleaseComputeBuffer(ref shellNodesL1Buffer);
            ReleaseComputeBuffer(ref shellNodesL2Buffer);
            ReleaseComputeBuffer(ref shellNodesL3Buffer);
            ReleaseComputeBuffer(ref slotMetadataBuffer);
            ReleaseComputeBuffer(ref slotRequestedInstanceCountBuffer);
            ReleaseComputeBuffer(ref slotEmittedInstanceCountBuffer);
            ReleaseComputeBuffer(ref slotPackedStartsBuffer);
            ReleaseComputeBuffer(ref cellVisibilityBuffer);
            ReleaseComputeBuffer(ref treeModesBuffer);
            ReleaseComputeBuffer(ref branchDecisionBuffer);
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
        private struct TreeGpu
        {
            public Vector3 SphereCenterWorld;
            public float BoundingSphereRadius;
            public int CellIndex;
            public int LodProfileIndex;
            public int SceneBranchStartIndex;
            public int SceneBranchCount;
            public BoundsGpu WorldBounds;
            public BoundsGpu TrunkFullWorldBounds;
            public BoundsGpu TrunkL3WorldBounds;
            public BoundsGpu ImpostorWorldBounds;
            public int TrunkFullDrawSlot;
            public int TrunkL3DrawSlot;
            public int ImpostorDrawSlot;
            public int Padding0;
            public VegetationIndirectInstanceData UploadInstanceData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BranchGpu
        {
            public int TreeIndex;
            public int BranchPlacementIndex;
            public int PrototypeIndex;
            public int WoodDrawSlotL0;
            public Vector3 SphereCenterWorld;
            public float BoundingSphereRadius;
            public Matrix4x4 LocalToWorld;
            public int WoodDrawSlotL1;
            public int WoodDrawSlotL2;
            public int WoodDrawSlotL3;
            public int FoliageDrawSlotL0;
            public int Padding0;
            public int Padding1;
            public int Padding2;
            public VegetationIndirectInstanceData WoodUploadInstanceData;
            public VegetationIndirectInstanceData FoliageUploadInstanceData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PrototypeGpu
        {
            public int ShellNodeStartIndexL1;
            public int ShellNodeCountL1;
            public int ShellNodeStartIndexL2;
            public int ShellNodeCountL2;
            public int ShellNodeStartIndexL3;
            public int ShellNodeCountL3;
            public Vector3 LocalBoundsCenter;
            public float Padding0;
            public Vector3 LocalBoundsExtents;
            public float Padding1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ShellNodeGpu
        {
            public Vector3 LocalCenter;
            public float Padding0;
            public Vector3 LocalExtents;
            public int FirstChildIndex;
            public uint ChildMask;
            public int ShellDrawSlot;
            public int Padding1;
            public int Padding2;
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
            public uint BaseVertexIndex;
            public uint Padding0;
        }
    }
}
