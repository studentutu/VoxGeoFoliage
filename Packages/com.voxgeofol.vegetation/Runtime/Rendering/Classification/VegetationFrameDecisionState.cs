#nullable enable

using System;
using System.Collections.Generic;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Reusable per-frame decision buffers shared by the CPU mirror and GPU parity path.
    /// </summary>
    public sealed class VegetationFrameDecisionState
    {
        private readonly uint[] cellVisibilityMask;
        private readonly VegetationTreeRenderMode[] treeModes;
        private readonly VegetationBranchDecisionRecord[] branchDecisions;
        private readonly VegetationNodeDecisionRecord[] nodeDecisions;
        private readonly List<int> visibleCellIndices = new List<int>();

        public VegetationFrameDecisionState(VegetationRuntimeRegistry registry)
        {
            cellVisibilityMask = new uint[registry.SpatialGrid.Cells.Count];
            treeModes = new VegetationTreeRenderMode[registry.TreeInstances.Count];
            branchDecisions = new VegetationBranchDecisionRecord[registry.SceneBranches.Count];
            nodeDecisions = new VegetationNodeDecisionRecord[registry.TotalNodeDecisionCapacity];
        }

        public uint[] CellVisibilityMask => cellVisibilityMask;

        public IReadOnlyList<int> VisibleCellIndices => visibleCellIndices;

        public VegetationTreeRenderMode[] TreeModes => treeModes;

        public VegetationBranchDecisionRecord[] BranchDecisions => branchDecisions;

        public VegetationNodeDecisionRecord[] NodeDecisions => nodeDecisions;

        /// <summary>
        /// [INTEGRATION] Reinitializes the reusable frame buffers against the frozen runtime registry contract.
        /// </summary>
        public void Reset(VegetationRuntimeRegistry registry)
        {
            Array.Clear(cellVisibilityMask, 0, cellVisibilityMask.Length);
            visibleCellIndices.Clear();

            for (int i = 0; i < treeModes.Length; i++)
            {
                treeModes[i] = VegetationTreeRenderMode.Culled;
            }

            for (int branchIndex = 0; branchIndex < branchDecisions.Length; branchIndex++)
            {
                VegetationSceneBranchRuntime sceneBranch = registry.SceneBranches[branchIndex];
                branchDecisions[branchIndex] = new VegetationBranchDecisionRecord
                {
                    TreeIndex = sceneBranch.TreeIndex,
                    BranchInstanceIndex = branchIndex,
                    BranchPlacementIndex = sceneBranch.BranchPlacementIndex,
                    RuntimeTier = VegetationBranchDecisionRecord.InactiveRuntimeTier
                };

                WriteRejectSlice(sceneBranch.TreeIndex, branchIndex, sceneBranch.BranchPlacementIndex, sceneBranch.DecisionStartL1, sceneBranch.DecisionCountL1, (int)VegetationRuntimeBranchTier.L1);
                WriteRejectSlice(sceneBranch.TreeIndex, branchIndex, sceneBranch.BranchPlacementIndex, sceneBranch.DecisionStartL2, sceneBranch.DecisionCountL2, (int)VegetationRuntimeBranchTier.L2);
                WriteRejectSlice(sceneBranch.TreeIndex, branchIndex, sceneBranch.BranchPlacementIndex, sceneBranch.DecisionStartL3, sceneBranch.DecisionCountL3, (int)VegetationRuntimeBranchTier.L3);
            }
        }

        /// <summary>
        /// [INTEGRATION] Replaces the stored visible-cell ordering with the latest deterministic spatial-grid query result.
        /// </summary>
        public void RefreshVisibleCellIndices(IReadOnlyList<int> orderedVisibleCells)
        {
            visibleCellIndices.Clear();
            for (int i = 0; i < orderedVisibleCells.Count; i++)
            {
                visibleCellIndices.Add(orderedVisibleCells[i]);
            }
        }

        private void WriteRejectSlice(int treeIndex, int branchInstanceIndex, int branchPlacementIndex, int startIndex, int count, int runtimeTier)
        {
            for (int i = 0; i < count; i++)
            {
                nodeDecisions[startIndex + i] = new VegetationNodeDecisionRecord
                {
                    TreeIndex = treeIndex,
                    BranchInstanceIndex = branchInstanceIndex,
                    BranchPlacementIndex = branchPlacementIndex,
                    RuntimeTier = runtimeTier,
                    NodeIndex = i,
                    Decision = (int)VegetationNodeDecision.Reject
                };
            }
        }
    }
}
