#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Frozen runtime registration snapshot built from scene authoring data.
    /// It stores the static registry owners: exact draw-slot registries, flat tree branch spans, bounded scene-branch records, and prototype-local branch-shell metadata.
    /// Per-frame classification worklists and accepted-content buffers are owned later by the GPU pipeline, not by this registry.
    /// </summary>
    public sealed class VegetationRuntimeRegistry
    {
        public VegetationRuntimeRegistry(
            VegetationDrawSlot[] drawSlots,
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
            VegetationSpatialGrid spatialGrid)
        {
            this.drawSlots = drawSlots;
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
            SpatialGrid = spatialGrid;
        }

        private readonly VegetationDrawSlot[] drawSlots;
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

        public IReadOnlyList<VegetationDrawSlot> DrawSlots => drawSlots;

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
    }
}
