#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Frozen runtime contract built from scene authoring data.
    /// </summary>
    public sealed class VegetationRuntimeRegistry
    {
        public VegetationRuntimeRegistry(
            VegetationDrawSlot[] drawSlots,
            int[] drawSlotMaxInstanceCounts,
            Bounds[] drawSlotConservativeWorldBounds,
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
            this.drawSlotMaxInstanceCounts = drawSlotMaxInstanceCounts;
            this.drawSlotConservativeWorldBounds = drawSlotConservativeWorldBounds;
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
        private readonly int[] drawSlotMaxInstanceCounts;
        private readonly Bounds[] drawSlotConservativeWorldBounds;
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

        public IReadOnlyList<int> DrawSlotMaxInstanceCounts => drawSlotMaxInstanceCounts;

        public IReadOnlyList<Bounds> DrawSlotConservativeWorldBounds => drawSlotConservativeWorldBounds;

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

        internal int[] NodeDrawSlotIndices => nodeDrawSlotIndices;

        internal Bounds[] NodeWorldBounds => nodeWorldBounds;
    }
}
