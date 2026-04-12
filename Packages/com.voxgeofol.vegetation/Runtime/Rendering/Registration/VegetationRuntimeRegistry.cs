#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Frozen runtime registration snapshot built from scene authoring data.
    /// It stores the static urgent-path owners: exact draw-slot registries, tree instances, reusable tree-blueprint branch placements,
    /// and compact branch prototype tier meshes. Per-frame accepted tree tiers and expanded branch worklists are owned later by the GPU pipeline.
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
            VegetationTreeInstanceRuntime[] treeInstances,
            VegetationSpatialGrid spatialGrid)
        {
            this.drawSlots = drawSlots;
            this.drawSlotConservativeWorldBounds = drawSlotConservativeWorldBounds;
            this.lodProfiles = lodProfiles;
            this.treeBlueprints = treeBlueprints;
            this.blueprintBranchPlacements = blueprintBranchPlacements;
            this.branchPrototypes = branchPrototypes;
            this.treeInstances = treeInstances;
            SpatialGrid = spatialGrid;
        }

        private readonly VegetationDrawSlot[] drawSlots;
        private readonly Bounds[] drawSlotConservativeWorldBounds;
        private readonly VegetationLodProfileRuntime[] lodProfiles;
        private readonly VegetationTreeBlueprintRuntime[] treeBlueprints;
        private readonly VegetationBlueprintBranchPlacementRuntime[] blueprintBranchPlacements;
        private readonly VegetationBranchPrototypeRuntime[] branchPrototypes;
        private readonly VegetationTreeInstanceRuntime[] treeInstances;

        public IReadOnlyList<VegetationDrawSlot> DrawSlots => drawSlots;

        public IReadOnlyList<Bounds> DrawSlotConservativeWorldBounds => drawSlotConservativeWorldBounds;

        public IReadOnlyList<VegetationLodProfileRuntime> LodProfiles => lodProfiles;

        public IReadOnlyList<VegetationTreeBlueprintRuntime> TreeBlueprints => treeBlueprints;

        public IReadOnlyList<VegetationBlueprintBranchPlacementRuntime> BlueprintBranchPlacements => blueprintBranchPlacements;

        public IReadOnlyList<VegetationBranchPrototypeRuntime> BranchPrototypes => branchPrototypes;

        public IReadOnlyList<VegetationTreeInstanceRuntime> TreeInstances => treeInstances;

        public VegetationSpatialGrid SpatialGrid { get; }
    }
}
