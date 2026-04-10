#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// GPU decision-path mirror for Phase D parity checks against the frozen runtime contracts.
    /// </summary>
    public sealed class VegetationGpuDecisionPipeline : IDisposable
    {
        private readonly ComputeShader classifyShader;
        private readonly VegetationRuntimeRegistry registry;
        private readonly int classifyCellsKernel;
        private readonly int classifyTreesKernel;
        private readonly int classifyBranchesAndNodesKernel;
        private readonly ComputeBuffer cellBuffer;
        private readonly ComputeBuffer lodBuffer;
        private readonly ComputeBuffer treeBuffer;
        private readonly ComputeBuffer branchBuffer;
        private readonly ComputeBuffer prototypeBuffer;
        private readonly ComputeBuffer shellNodesL1Buffer;
        private readonly ComputeBuffer shellNodesL2Buffer;
        private readonly ComputeBuffer shellNodesL3Buffer;
        private readonly ComputeBuffer cellVisibilityBuffer;
        private readonly ComputeBuffer treeModesBuffer;
        private readonly ComputeBuffer branchDecisionBuffer;
        private readonly ComputeBuffer nodeDecisionBuffer;
        private readonly Vector4[] frustumPlaneVectors = new Vector4[6];
        private AsyncGPUReadbackRequest cellVisibilityReadback;
        private AsyncGPUReadbackRequest treeModesReadback;
        private AsyncGPUReadbackRequest branchDecisionsReadback;
        private AsyncGPUReadbackRequest nodeDecisionsReadback;
        private VegetationFrameDecisionState? completedReadbackState;
        private bool readbackPending;
        private bool disposed;

        public VegetationGpuDecisionPipeline(ComputeShader classifyShader, VegetationRuntimeRegistry registry)
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                throw new NotSupportedException(
                    "This runtime does not support compute shaders, so the Phase D GPU decision path is unavailable.");
            }

            this.classifyShader = classifyShader ?? throw new ArgumentNullException(nameof(classifyShader));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));

            try
            {
                classifyCellsKernel = classifyShader.FindKernel("ClassifyCells");
                classifyTreesKernel = classifyShader.FindKernel("ClassifyTrees");
                classifyBranchesAndNodesKernel = classifyShader.FindKernel("ClassifyBranchesAndNodes");
            }
            catch (ArgumentException exception)
            {
                throw new NotSupportedException(
                    "VegetationClassify.compute imported without the expected kernels. The Phase D GPU decision path is unavailable in this Unity environment.",
                    exception);
            }

            cellBuffer = CreateStructuredBuffer<CellGpu>(Mathf.Max(1, registry.SpatialGrid.Cells.Count));
            lodBuffer = CreateStructuredBuffer<LodProfileGpu>(Mathf.Max(1, registry.LodProfiles.Count));
            treeBuffer = CreateStructuredBuffer<TreeGpu>(Mathf.Max(1, registry.TreeInstances.Count));
            branchBuffer = CreateStructuredBuffer<BranchGpu>(Mathf.Max(1, registry.SceneBranches.Count));
            prototypeBuffer = CreateStructuredBuffer<PrototypeGpu>(Mathf.Max(1, registry.BranchPrototypes.Count));
            shellNodesL1Buffer = CreateStructuredBuffer<ShellNodeGpu>(Mathf.Max(1, registry.ShellNodesL1.Count));
            shellNodesL2Buffer = CreateStructuredBuffer<ShellNodeGpu>(Mathf.Max(1, registry.ShellNodesL2.Count));
            shellNodesL3Buffer = CreateStructuredBuffer<ShellNodeGpu>(Mathf.Max(1, registry.ShellNodesL3.Count));
            cellVisibilityBuffer = CreateStructuredBuffer<uint>(Mathf.Max(1, registry.SpatialGrid.Cells.Count));
            treeModesBuffer = CreateStructuredBuffer<int>(Mathf.Max(1, registry.TreeInstances.Count));
            branchDecisionBuffer =
                CreateStructuredBuffer<VegetationBranchDecisionRecord>(Mathf.Max(1, registry.SceneBranches.Count));
            nodeDecisionBuffer =
                CreateStructuredBuffer<VegetationNodeDecisionRecord>(Mathf.Max(1, registry.TotalNodeDecisionCapacity));

            UploadStaticData();
            BindBuffers();
        }

        /// <summary>
        /// [INTEGRATION] Runs one immediate GPU classification/decode-decision frame for parity verification.
        /// </summary>
        public VegetationFrameDecisionState EvaluateFrameImmediate(Vector3 cameraWorldPosition, Plane[] frustumPlanes)
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
                throw new ArgumentException("Phase D GPU frustum classification requires six frustum planes.",
                    nameof(frustumPlanes));
            }

            UploadDynamicFrameData(cameraWorldPosition, frustumPlanes);
            DispatchKernel(classifyCellsKernel, registry.SpatialGrid.Cells.Count);
            DispatchKernel(classifyTreesKernel, registry.TreeInstances.Count);
            DispatchKernel(classifyBranchesAndNodesKernel, registry.SceneBranches.Count);

            VegetationFrameDecisionState state = new VegetationFrameDecisionState(registry);
            state.Reset(registry);
            cellVisibilityBuffer.GetData(state.CellVisibilityMask, 0, 0, registry.SpatialGrid.Cells.Count);
            state.RefreshVisibleCellIndices(BuildVisibleCellIndices(state.CellVisibilityMask));

            int[] treeModes = new int[Mathf.Max(1, registry.TreeInstances.Count)];
            treeModesBuffer.GetData(treeModes, 0, 0, registry.TreeInstances.Count);
            for (int i = 0; i < registry.TreeInstances.Count; i++)
            {
                state.TreeModes[i] = (VegetationTreeRenderMode)treeModes[i];
            }

            branchDecisionBuffer.GetData(state.BranchDecisions, 0, 0, registry.SceneBranches.Count);
            nodeDecisionBuffer.GetData(state.NodeDecisions, 0, 0, registry.TotalNodeDecisionCapacity);
            return state;
        }

        /// <summary>
        /// [INTEGRATION] Schedules one non-blocking GPU readback of the current Phase D decision buffers for Phase E fallback decode.
        /// </summary>
        public void ScheduleFrameReadback(Vector3 cameraWorldPosition, Plane[] frustumPlanes)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(VegetationGpuDecisionPipeline));
            }

            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                throw new NotSupportedException(
                    "This runtime does not support AsyncGPUReadback, so the Phase E GPU decision readback bridge is unavailable.");
            }

            if (readbackPending)
            {
                return;
            }

            if (frustumPlanes == null)
            {
                throw new ArgumentNullException(nameof(frustumPlanes));
            }

            if (frustumPlanes.Length < 6)
            {
                throw new ArgumentException("Phase D GPU frustum classification requires six frustum planes.",
                    nameof(frustumPlanes));
            }

            UploadDynamicFrameData(cameraWorldPosition, frustumPlanes);
            DispatchKernel(classifyCellsKernel, registry.SpatialGrid.Cells.Count);
            DispatchKernel(classifyTreesKernel, registry.TreeInstances.Count);
            DispatchKernel(classifyBranchesAndNodesKernel, registry.SceneBranches.Count);

            cellVisibilityReadback = AsyncGPUReadback.Request(cellVisibilityBuffer);
            treeModesReadback = AsyncGPUReadback.Request(treeModesBuffer);
            branchDecisionsReadback = AsyncGPUReadback.Request(branchDecisionBuffer);
            nodeDecisionsReadback = AsyncGPUReadback.Request(nodeDecisionBuffer);
            readbackPending = true;
        }

        /// <summary>
        /// [INTEGRATION] Tries to consume the latest completed GPU decision readback without blocking the frame.
        /// </summary>
        public bool TryConsumeCompletedReadback(out VegetationFrameDecisionState? state)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(VegetationGpuDecisionPipeline));
            }

            if (!readbackPending)
            {
                state = null;
                return false;
            }

            if (!cellVisibilityReadback.done ||
                !treeModesReadback.done ||
                !branchDecisionsReadback.done ||
                !nodeDecisionsReadback.done)
            {
                state = null;
                return false;
            }

            readbackPending = false;
            if (cellVisibilityReadback.hasError ||
                treeModesReadback.hasError ||
                branchDecisionsReadback.hasError ||
                nodeDecisionsReadback.hasError)
            {
                if (cellVisibilityReadback.hasError)
                    UnityEngine.Debug.LogError(
                        $"VegetationGpuDecisionPipeline GPU decision cellVisibilityReadback error.");
                if (treeModesReadback.hasError)
                    UnityEngine.Debug.LogError($"VegetationGpuDecisionPipeline GPU decision treeModesReadback error.");
                if (branchDecisionsReadback.hasError)
                    UnityEngine.Debug.LogError(
                        $"VegetationGpuDecisionPipeline GPU decision branchDecisionsReadback error.");
                if (nodeDecisionsReadback.hasError)
                    UnityEngine.Debug.LogError(
                        $"VegetationGpuDecisionPipeline GPU decision nodeDecisionsReadback error.");
                
                state = null;
                return false;
            }

            completedReadbackState ??= new VegetationFrameDecisionState(registry);
            completedReadbackState.Reset(registry);

            NativeArray<uint> cellVisibility = cellVisibilityReadback.GetData<uint>();
            for (int i = 0; i < registry.SpatialGrid.Cells.Count; i++)
            {
                completedReadbackState.CellVisibilityMask[i] = cellVisibility[i];
            }

            completedReadbackState.RefreshVisibleCellIndices(
                BuildVisibleCellIndices(completedReadbackState.CellVisibilityMask));

            NativeArray<int> treeModes = treeModesReadback.GetData<int>();
            for (int i = 0; i < registry.TreeInstances.Count; i++)
            {
                completedReadbackState.TreeModes[i] = (VegetationTreeRenderMode)treeModes[i];
            }

            branchDecisionsReadback.GetData<VegetationBranchDecisionRecord>()
                .CopyTo(completedReadbackState.BranchDecisions);
            nodeDecisionsReadback.GetData<VegetationNodeDecisionRecord>().CopyTo(completedReadbackState.NodeDecisions);

            state = completedReadbackState;
            return true;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cellBuffer.Release();
            lodBuffer.Release();
            treeBuffer.Release();
            branchBuffer.Release();
            prototypeBuffer.Release();
            shellNodesL1Buffer.Release();
            shellNodesL2Buffer.Release();
            shellNodesL3Buffer.Release();
            cellVisibilityBuffer.Release();
            treeModesBuffer.Release();
            branchDecisionBuffer.Release();
            nodeDecisionBuffer.Release();
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
                    SceneBranchCount = tree.SceneBranchCount
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
                    DecisionStartL1 = branch.DecisionStartL1,
                    DecisionCountL1 = branch.DecisionCountL1,
                    DecisionStartL2 = branch.DecisionStartL2,
                    DecisionCountL2 = branch.DecisionCountL2,
                    DecisionStartL3 = branch.DecisionStartL3,
                    DecisionCountL3 = branch.DecisionCountL3
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
                    ShellNodeCountL3 = prototype.ShellNodeCountL3
                };
            }

            prototypeBuffer.SetData(prototypes);
            UploadShellNodes(shellNodesL1Buffer, registry.ShellNodesL1);
            UploadShellNodes(shellNodesL2Buffer, registry.ShellNodesL2);
            UploadShellNodes(shellNodesL3Buffer, registry.ShellNodesL3);
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

            classifyShader.SetBuffer(classifyBranchesAndNodesKernel, "_Trees", treeBuffer);
            classifyShader.SetBuffer(classifyBranchesAndNodesKernel, "_Branches", branchBuffer);
            classifyShader.SetBuffer(classifyBranchesAndNodesKernel, "_Prototypes", prototypeBuffer);
            classifyShader.SetBuffer(classifyBranchesAndNodesKernel, "_LodProfiles", lodBuffer);
            classifyShader.SetBuffer(classifyBranchesAndNodesKernel, "_TreeModes", treeModesBuffer);
            classifyShader.SetBuffer(classifyBranchesAndNodesKernel, "_ShellNodesL1", shellNodesL1Buffer);
            classifyShader.SetBuffer(classifyBranchesAndNodesKernel, "_ShellNodesL2", shellNodesL2Buffer);
            classifyShader.SetBuffer(classifyBranchesAndNodesKernel, "_ShellNodesL3", shellNodesL3Buffer);
            classifyShader.SetBuffer(classifyBranchesAndNodesKernel, "_BranchDecisions", branchDecisionBuffer);
            classifyShader.SetBuffer(classifyBranchesAndNodesKernel, "_NodeDecisions", nodeDecisionBuffer);
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
            classifyShader.SetInt("_NodeDecisionCount", registry.TotalNodeDecisionCapacity);
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

        private static int[] BuildVisibleCellIndices(IReadOnlyList<uint> mask)
        {
            int visibleCount = 0;
            for (int i = 0; i < mask.Count; i++)
            {
                if (mask[i] != 0u)
                {
                    visibleCount++;
                }
            }

            int[] result = new int[visibleCount];
            int targetIndex = 0;
            for (int i = 0; i < mask.Count; i++)
            {
                if (mask[i] != 0u)
                {
                    result[targetIndex++] = i;
                }
            }

            return result;
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
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BranchGpu
        {
            public int TreeIndex;
            public int BranchPlacementIndex;
            public int PrototypeIndex;
            public int Padding0;
            public Vector3 SphereCenterWorld;
            public float BoundingSphereRadius;
            public Matrix4x4 LocalToWorld;
            public int DecisionStartL1;
            public int DecisionCountL1;
            public int DecisionStartL2;
            public int DecisionCountL2;
            public int DecisionStartL3;
            public int DecisionCountL3;
            public int Padding1;
            public int Padding2;
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
    }
}