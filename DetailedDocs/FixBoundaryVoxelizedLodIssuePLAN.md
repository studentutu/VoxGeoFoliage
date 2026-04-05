# Split Canonical L0 Ownership from Compact L1/L2 Hierarchies

## Summary
- Keep one canonical `L0` hierarchy as the only ownership and split authority for canopy occupancy.
- Bake two additional persisted compact hierarchies, `L1` and `L2`, from owned `L0` occupancy at their authored voxel resolutions.
- Allow `L1/L2` to prune or merge deeper `L0` nodes when the coarse voxel result is equivalent enough for that tier, so runtime traversal cost drops when already in `L1` or `L2`.
- Apply the same strict bound-preserving voxel-bake rule to every generated geometry path: canopy leaves, voxelized branch wood, and tree impostor.
- Fix the two current correctness bugs in the dirty branch: node-bound overflow and the no-op `L1/L2` LOD path.

## Current Problems
### Problem A — Voxel Geometry Overflows Node Bounds
- In [CpuVoxelSurfaceMeshBuilder.cs](C:/Users/admin/Documents/UnityProjects/VoxGeoFol/Packages/com.voxgeofol.vegetation/Runtime/VoxelizerV2/Scripts/CpuVoxelSurfaceMeshBuilder.cs), `IsVoxelOwnedByBounds` tests voxel-center containment only.
- Both `BuildRawSurfaceMesh` and `BuildReducedSurfaceMesh` emit full voxel cubes/quads using `volume.UnitLength`, so boundary-owned voxels can render up to `unitLength / 2` beyond the tested node bounds.
- In [CPUVoxelizer.cs](C:/Users/admin/Documents/UnityProjects/VoxGeoFol/Packages/com.voxgeofol.vegetation/Runtime/VoxelizerV2/Scripts/CPUVoxelizer.cs), `VoxelizeToVolume` expands the scan bounds by `halfUnit`, so the outermost occupied voxels can also extend beyond the original source mesh AABB.
- Result: persisted hierarchy bounds do not reliably match rendered voxel geometry, and shell nodes can appear to grow beyond the intended shell.

### Problem B — L1/L2 Produce Identical Meshes to L0
- The current dirty change in [MeshVoxelizerHierarchyBuilder.cs](C:/Users/admin/Documents/UnityProjects/VoxGeoFol/Packages/com.voxgeofol.vegetation/Runtime/Authoring/Hierarchy/MeshVoxelizerHierarchyBuilder.cs) introduced `CreateSharedVoxelLevel`, which wraps the same `L0` `CpuVoxelVolume` with a different resolution integer.
- [CpuVoxelSurfaceMeshBuilder.cs](C:/Users/admin/Documents/UnityProjects/VoxGeoFol/Packages/com.voxgeofol.vegetation/Runtime/VoxelizerV2/Scripts/CpuVoxelSurfaceMeshBuilder.cs) builds meshes from the actual `CpuVoxelVolume` cells and `UnitLength`, so `L1/L2` output remains geometrically identical to `L0`.
- `VoxelLevelData.Resolution` is metadata only in this path; it does not change emitted geometry.
- Result: there is no true shell reduction for `L1/L2`, and no traversal/search-space win from entering lower LOD tiers.

## Global Bounding Rule
- Every editor-baked voxel artifact must be bounded by its authoritative source occupancy, not by voxel-center tests alone.
- This rule applies to all generated parts:
  - canopy leaves / shell hierarchies,
  - voxelized branch wood attachments (`shellL1WoodMesh`, `shellL2WoodMesh`),
  - tree impostor mesh.
- Persisted bounds must always come from the actual emitted mesh bounds for that artifact.
- Coarser voxel tiers may simplify or merge, but they must never expand outside the authoritative source occupancy for that artifact.

## Public API / Data Changes
- Change [BranchPrototypeSO.cs](C:/Users/admin/Documents/UnityProjects/VoxGeoFol/Packages/com.voxgeofol.vegetation/Runtime/Authoring/BranchPrototypeSO.cs) from one `shellNodes` array to three persisted arrays:
  - `shellNodesL0`
  - `shellNodesL1`
  - `shellNodesL2`
- Keep `shellL1WoodMesh` and `shellL2WoodMesh` as branch-level attachments.
- Keep `ShellBakeSettings` unchanged. `voxelResolutionL0/L1/L2` remain the authored controls for canonical `L0` and compact `L1/L2` generation.
- Keep `BranchShellNode` shape unchanged if possible.
- New `localBounds` rule: in each tier-specific hierarchy, `localBounds` must be the full rendered mesh AABB for that persisted node, not the center-tested ownership box.

## Implementation Changes
- In [MeshVoxelizerHierarchyBuilder.cs](C:/Users/admin/Documents/UnityProjects/VoxGeoFol/Packages/com.voxgeofol.vegetation/Runtime/Authoring/Hierarchy/MeshVoxelizerHierarchyBuilder.cs), build one canonical filled `L0` CPU volume from foliage.
- Build canonical `L0` hierarchy from that volume:
  - split decisions use owned `L0` surface voxels only,
  - ownership stays exclusive by octant partition,
  - internal recursion may still use partition bounds, but persisted `L0` node bounds come from the actual emitted `L0` mesh bounds.
- Remove the current `CreateSharedVoxelLevel` path entirely. `L1/L2` must no longer reuse the same `CpuVoxelVolume` instance as `L0`.
- Derive compact `L1` and `L2` hierarchies separately from canonical owned `L0` occupancy:
  - each candidate compact node represents one canonical `L0` subtree,
  - gather that subtree’s owned filled `L0` occupancy,
  - re-voxelize it into a fresh node-local coarse volume at `voxelResolutionL1` or `voxelResolutionL2`,
  - emit a real coarse mesh from that coarse volume,
  - persist node bounds from that emitted mesh’s bounds.
- Use one compaction rule for both `L1` and `L2`:
  - accept one compact parent node when its tier mesh stays inside the canonical subtree’s owned `L0` render bounds and its triangle count is less than or equal to the sum of the recursively generated child tier meshes,
  - otherwise keep descending and persist child tier nodes instead.
- Apply the same strict bound-preserving bake rule to branch wood generation:
  - voxelized `shellL1WoodMesh` and `shellL2WoodMesh` must be bounded to authoritative source wood occupancy,
  - persisted wood mesh bounds must come from emitted geometry,
  - simplification/fallback may reduce triangles but may not expand beyond the source wood occupancy.
- Apply the same strict bound-preserving bake rule to impostor generation:
  - the coarse tree-space impostor mesh must stay bounded to the authoritative source tree occupancy assembled from trunk + branch wood + branch foliage,
  - persisted impostor bounds must come from the emitted impostor mesh,
  - simplification/fallback may reduce triangles but may not expand beyond the assembled source occupancy.
- `CanopyShellGenerator` must:
  - bake canonical `L0` hierarchy first,
  - derive compact `L1`,
  - derive compact `L2`,
  - persist each hierarchy into its own authoring array,
  - continue using reduction/fallback only after real voxel generation.
- `VegetationEditorPreview` must pick the tier-specific hierarchy directly:
  - `R0` and `ShellL0Only` use `shellNodesL0`,
  - `R1` and `ShellL1Only` use `shellNodesL1` plus `shellL1WoodMesh`,
  - `R2` and `ShellL2Only` use `shellNodesL2` plus `shellL2WoodMesh`.
- `VegetationAuthoringValidator` and triangle summary code must validate and count each hierarchy independently instead of assuming one shared node tree.
- Update [Milestone1.md](C:/Users/admin/Documents/UnityProjects/VoxGeoFol/DetailedDocs/Milestone1.md), [UnityAssembledVegetation_FULL.md](C:/Users/admin/Documents/UnityProjects/VoxGeoFol/DetailedDocs/UnityAssembledVegetation_FULL.md), [progress.md](C:/Users/admin/Documents/UnityProjects/VoxGeoFol/memorybank/progress.md), and [projectrules.md](C:/Users/admin/Documents/UnityProjects/VoxGeoFol/memorybank/projectrules.md) so they describe canonical `L0` plus compact `L1/L2` hierarchies and the global strict-bounds rule for leaves, branch wood, and impostor baking.

## Test Plan
- Add a regression for Problem A:
  - every persisted node in `L0/L1/L2` must have `localBounds` fully containing its stored mesh bounds,
  - no tier may use center-tested ownership bounds as persisted render bounds.
- Add equivalent strict-bounds regressions for generated branch wood and impostor meshes:
  - voxelized wood output must stay inside authoritative source wood bounds,
  - impostor output must stay inside authoritative assembled tree bounds.
- Add a regression for Problem B:
  - with different authored resolutions and simplification/fallback disabled, at least one `L1` or `L2` mesh must differ from its `L0` counterpart,
  - `L1` and `L2` hierarchies must be stored separately from `L0`.
- Add a compaction regression:
  - `shellNodesL1.Length <= shellNodesL0.Length`,
  - `shellNodesL2.Length <= shellNodesL1.Length` on the representative separated-cluster fixture.
- Update preview tests so `ShellL1Only` and `ShellL2Only` instantiate from the tier-specific hierarchy arrays, not from `L0` leaves.
- Keep existing monotonic validation, but apply it at tier totals:
  - total `L0` shell triangles > total `L1` shell triangles > total `L2` shell triangles.
- Verification target after edits:
  - `Compile by Rider MSBuild` if no new `.cs` files are added,
  - otherwise `Fully Compile by Unity`.

## Assumptions and Defaults
- `L0` is the only source of truth for ownership, subdivision, and subtree authority.
- `L1` and `L2` are not allowed to invent occupancy outside the canonical owned `L0` subtree render bounds.
- Branch wood and impostor voxel baking follow the same rule: no generated geometry may expand beyond the authoritative source occupancy for that artifact.
- Tier-specific hierarchies are bake-time outputs; runtime and preview consume them directly and do not rebuild or re-prune them on the fly.
- Existing baked assets are migrated by rebaking; backward compatibility for the current dirty shared-volume output is not required.
