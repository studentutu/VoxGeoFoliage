# VoxGeoFol Vegetation

Embedded UPM package for the repo's branch-assembled vegetation authoring workflow.

## Contents

- `Runtime/Authoring`: ScriptableObjects, scene authoring component, and validation logic.
- `Editor`: Phase A asset sync plus canopy shell / impostor bake tooling.
- `Tests/Editor`: EditMode coverage that travels with the package.
- `Samples~/Vegetation Demo`: non-essential demo assets for public distribution.

## Generated Meshes

Generated shell and impostor meshes are written beside the owning asset under a `GeneratedMeshes/` folder when the owner asset lives under `Assets/`. If the owner asset lives under `Packages/`, the tooling falls back to `Assets/VoxGeoFol.Generated/Vegetation/Meshes/` so public package installs never try to write into the package cache.

For canopy shell outputs specifically, `BranchPrototypeSO.generatedCanopyShellsRelativeFolder` can override that location with an explicit folder such as `Assets/Tree/VoxFoliage/GeneratedCanopyShells`.

For impostor outputs specifically, `TreeBlueprintSO.generatedImpostorMeshesRelativeFolder` can override that location with an explicit folder such as `Assets/Tree/VoxFoliage/GeneratedImpostors`.

## Sample Workflow

1. Import the `Vegetation Demo` sample from Package Manager.
2. Run `Tools/VoxGeoFol/Vegetation/Refresh Demo Phase A Assets`.
3. Use the `VegetationTreeAuthoring` context menu to bake canopy shells and the impostor.

See [`Documentation~/QuickStart.md`](Documentation~/QuickStart.md) for the public-package layout and sample assumptions.
