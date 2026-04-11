#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Frozen Phase D runtime contract built from scene authoring data.
    /// </summary>
    public sealed class VegetationRuntimeRegistry
    {
        public VegetationRuntimeRegistry(
            VegetationDrawSlot[] drawSlots,
            VegetationLodProfileRuntime[] lodProfiles,
            VegetationTreeBlueprintRuntime[] treeBlueprints,
            VegetationBlueprintBranchPlacementRuntime[] blueprintBranchPlacements,
            VegetationBranchPrototypeRuntime[] branchPrototypes,
            VegetationBranchShellNodeRuntimeBfs[] shellNodesL1,
            VegetationBranchShellNodeRuntimeBfs[] shellNodesL2,
            VegetationBranchShellNodeRuntimeBfs[] shellNodesL3,
            VegetationTreeInstanceRuntime[] treeInstances,
            VegetationSceneBranchRuntime[] sceneBranches,
            int[] nodeDrawSlotIndices,
            Bounds[] nodeWorldBounds,
            VegetationSpatialGrid spatialGrid,
            int totalNodeDecisionCapacity)
        {
            this.drawSlots = drawSlots;
            this.lodProfiles = lodProfiles;
            this.treeBlueprints = treeBlueprints;
            this.blueprintBranchPlacements = blueprintBranchPlacements;
            this.branchPrototypes = branchPrototypes;
            this.shellNodesL1 = shellNodesL1;
            this.shellNodesL2 = shellNodesL2;
            this.shellNodesL3 = shellNodesL3;
            this.treeInstances = treeInstances;
            this.sceneBranches = sceneBranches;
            this.nodeDrawSlotIndices = nodeDrawSlotIndices;
            this.nodeWorldBounds = nodeWorldBounds;
            SpatialGrid = spatialGrid;
            TotalNodeDecisionCapacity = totalNodeDecisionCapacity;
        }

        private readonly VegetationDrawSlot[] drawSlots;
        private readonly VegetationLodProfileRuntime[] lodProfiles;
        private readonly VegetationTreeBlueprintRuntime[] treeBlueprints;
        private readonly VegetationBlueprintBranchPlacementRuntime[] blueprintBranchPlacements;
        private readonly VegetationBranchPrototypeRuntime[] branchPrototypes;
        private readonly VegetationBranchShellNodeRuntimeBfs[] shellNodesL1;
        private readonly VegetationBranchShellNodeRuntimeBfs[] shellNodesL2;
        private readonly VegetationBranchShellNodeRuntimeBfs[] shellNodesL3;
        private readonly VegetationTreeInstanceRuntime[] treeInstances;
        private readonly VegetationSceneBranchRuntime[] sceneBranches;
        private readonly int[] nodeDrawSlotIndices;
        private readonly Bounds[] nodeWorldBounds;

        public IReadOnlyList<VegetationDrawSlot> DrawSlots => drawSlots;

        public IReadOnlyList<VegetationLodProfileRuntime> LodProfiles => lodProfiles;

        public IReadOnlyList<VegetationTreeBlueprintRuntime> TreeBlueprints => treeBlueprints;

        public IReadOnlyList<VegetationBlueprintBranchPlacementRuntime> BlueprintBranchPlacements => blueprintBranchPlacements;

        public IReadOnlyList<VegetationBranchPrototypeRuntime> BranchPrototypes => branchPrototypes;

        public IReadOnlyList<VegetationBranchShellNodeRuntimeBfs> ShellNodesL1 => shellNodesL1;

        public IReadOnlyList<VegetationBranchShellNodeRuntimeBfs> ShellNodesL2 => shellNodesL2;

        public IReadOnlyList<VegetationBranchShellNodeRuntimeBfs> ShellNodesL3 => shellNodesL3;

        public IReadOnlyList<VegetationTreeInstanceRuntime> TreeInstances => treeInstances;

        public IReadOnlyList<VegetationSceneBranchRuntime> SceneBranches => sceneBranches;

        public VegetationSpatialGrid SpatialGrid { get; }

        public int TotalNodeDecisionCapacity { get; }

        internal VegetationDrawSlot[] DrawSlotsArray => drawSlots;

        internal VegetationTreeBlueprintRuntime[] TreeBlueprintsArray => treeBlueprints;

        internal VegetationTreeInstanceRuntime[] TreeInstancesArray => treeInstances;

        internal VegetationSceneBranchRuntime[] SceneBranchesArray => sceneBranches;

        internal int[] NodeDrawSlotIndices => nodeDrawSlotIndices;

        internal Bounds[] NodeWorldBounds => nodeWorldBounds;

        /// <summary>
        /// [INTEGRATION] Creates the stable per-slot visible-output container that Phase D rebuilds every frame.
        /// </summary>
        public VegetationFrameOutput CreateFrameOutput(bool captureDebugInstances = true)
        {
            return new VegetationFrameOutput(DrawSlots, captureDebugInstances);
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
                VegetationRuntimeBranchTier.L1 => shellNodesL1,
                VegetationRuntimeBranchTier.L2 => shellNodesL2,
                VegetationRuntimeBranchTier.L3 => shellNodesL3,
                _ => throw new ArgumentOutOfRangeException(nameof(runtimeTier), runtimeTier, "Shell-node runtime caches exist only for shell runtime tiers L1/L2/L3.")
            };
        }
    }
}
