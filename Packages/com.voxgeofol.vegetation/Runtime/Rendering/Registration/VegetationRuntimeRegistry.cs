#nullable enable

using System;
using System.Collections.Generic;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Frozen Phase D runtime contract built from scene authoring data.
    /// </summary>
    public sealed class VegetationRuntimeRegistry
    {
        public VegetationRuntimeRegistry(
            IReadOnlyList<VegetationDrawSlot> drawSlots,
            IReadOnlyList<VegetationLodProfileRuntime> lodProfiles,
            IReadOnlyList<VegetationTreeBlueprintRuntime> treeBlueprints,
            IReadOnlyList<VegetationBlueprintBranchPlacementRuntime> blueprintBranchPlacements,
            IReadOnlyList<VegetationBranchPrototypeRuntime> branchPrototypes,
            IReadOnlyList<VegetationBranchShellNodeRuntimeBfs> shellNodesL1,
            IReadOnlyList<VegetationBranchShellNodeRuntimeBfs> shellNodesL2,
            IReadOnlyList<VegetationBranchShellNodeRuntimeBfs> shellNodesL3,
            IReadOnlyList<VegetationTreeInstanceRuntime> treeInstances,
            IReadOnlyList<VegetationSceneBranchRuntime> sceneBranches,
            VegetationSpatialGrid spatialGrid,
            int totalNodeDecisionCapacity)
        {
            DrawSlots = drawSlots;
            LodProfiles = lodProfiles;
            TreeBlueprints = treeBlueprints;
            BlueprintBranchPlacements = blueprintBranchPlacements;
            BranchPrototypes = branchPrototypes;
            ShellNodesL1 = shellNodesL1;
            ShellNodesL2 = shellNodesL2;
            ShellNodesL3 = shellNodesL3;
            TreeInstances = treeInstances;
            SceneBranches = sceneBranches;
            SpatialGrid = spatialGrid;
            TotalNodeDecisionCapacity = totalNodeDecisionCapacity;
        }

        public IReadOnlyList<VegetationDrawSlot> DrawSlots { get; }

        public IReadOnlyList<VegetationLodProfileRuntime> LodProfiles { get; }

        public IReadOnlyList<VegetationTreeBlueprintRuntime> TreeBlueprints { get; }

        public IReadOnlyList<VegetationBlueprintBranchPlacementRuntime> BlueprintBranchPlacements { get; }

        public IReadOnlyList<VegetationBranchPrototypeRuntime> BranchPrototypes { get; }

        public IReadOnlyList<VegetationBranchShellNodeRuntimeBfs> ShellNodesL1 { get; }

        public IReadOnlyList<VegetationBranchShellNodeRuntimeBfs> ShellNodesL2 { get; }

        public IReadOnlyList<VegetationBranchShellNodeRuntimeBfs> ShellNodesL3 { get; }

        public IReadOnlyList<VegetationTreeInstanceRuntime> TreeInstances { get; }

        public IReadOnlyList<VegetationSceneBranchRuntime> SceneBranches { get; }

        public VegetationSpatialGrid SpatialGrid { get; }

        public int TotalNodeDecisionCapacity { get; }

        /// <summary>
        /// [INTEGRATION] Creates the stable per-slot visible-output container that Phase D rebuilds every frame.
        /// </summary>
        public VegetationFrameOutput CreateFrameOutput()
        {
            return new VegetationFrameOutput(DrawSlots);
        }

        /// <summary>
        /// [INTEGRATION] Resolves the per-branch node-decision slice for the selected runtime shell tier.
        /// </summary>
        public void GetDecisionRange(VegetationSceneBranchRuntime sceneBranch, VegetationRuntimeBranchTier runtimeTier, out int startIndex, out int count)
        {
            switch (runtimeTier)
            {
                case VegetationRuntimeBranchTier.L1:
                    startIndex = sceneBranch.DecisionStartL1;
                    count = sceneBranch.DecisionCountL1;
                    return;
                case VegetationRuntimeBranchTier.L2:
                    startIndex = sceneBranch.DecisionStartL2;
                    count = sceneBranch.DecisionCountL2;
                    return;
                case VegetationRuntimeBranchTier.L3:
                    startIndex = sceneBranch.DecisionStartL3;
                    count = sceneBranch.DecisionCountL3;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(runtimeTier), runtimeTier, "Node decisions exist only for shell runtime tiers L1/L2/L3.");
            }
        }

        /// <summary>
        /// [INTEGRATION] Resolves the prototype shell-node cache selected by one runtime shell tier.
        /// </summary>
        public IReadOnlyList<VegetationBranchShellNodeRuntimeBfs> GetShellNodes(VegetationRuntimeBranchTier runtimeTier)
        {
            return runtimeTier switch
            {
                VegetationRuntimeBranchTier.L1 => ShellNodesL1,
                VegetationRuntimeBranchTier.L2 => ShellNodesL2,
                VegetationRuntimeBranchTier.L3 => ShellNodesL3,
                _ => throw new ArgumentOutOfRangeException(nameof(runtimeTier), runtimeTier, "Shell-node runtime caches exist only for shell runtime tiers L1/L2/L3.")
            };
        }
    }
}
