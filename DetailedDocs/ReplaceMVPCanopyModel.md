# Replace MVP Canopy Model

## Status
- [x] Spec locked for hierarchical canopy replacement.
- [x] Implementation complete.
- [x] Docs and memory bank updated.
- [x] Full Unity compile completed.

## Scope Decisions
- `ReplaceMVPCanopyModel.md` is the primary working spec and progress tracker for this change.
- Replace the MVP single-chain canopy model with an adaptive octree where every occupied node stores its own `L0/L1/L2` shell chain.
- Use MeshVoxelizer-based shell resolutions `16/12/8` via `ShellBakeSettings.voxelResolutionL0/L1/L2`.
- Set `maxOctreeDepth = 4`.
- Split nodes only when the L0 surface voxel count reaches `minimumSurfaceVoxelCountToSplit`.
- Remove obsolete shell fields, methods, and code paths completely. No fallback path, adapter layer, or legacy compatibility code.
- Manual deletion and rebake of old generated branch shell assets is intentionally out of scope; the developer will handle that.

## Authoring Model
- [x] Remove `shellL0Mesh`, `shellL1Mesh`, and `shellL2Mesh` from `BranchPrototypeSO`.
- [x] Add authoritative `BranchShellNode[] shellNodes`.
- [x] Add `BranchShellNode` data model with `localBounds`, `depth`, `firstChildIndex`, `childMask`, and per-node `L0/L1/L2` meshes.
- [x] Keep `woodMesh`, `shellL1WoodMesh`, and `shellL2WoodMesh` branch-level only.
- [x] Replace shell-related helper accessors and validation entry points to read `shellNodes` only.

## Bake Pipeline
- [x] Replace source voxel size settings with MeshVoxelizer `16/12/8` resolutions.
- [x] Add `maxOctreeDepth` to bake settings with default `4`.
- [x] Build an adaptive octree directly from MeshVoxelizer L0 surface occupancy.
- [x] Generate `L0/L1/L2` shell meshes on every occupied node from owned voxel cells inside the node bounds.
- [x] Rebuild `CanopyShellGenerator` on `MeshVoxelizerHierarchyBuilder`.
- [x] Rebuild `ImpostorMeshGenerator` on the same MeshVoxelizer hierarchy extraction path.
- [x] Remove the obsolete `Voxelizer`, `VoxelGrid`, and `MarchingTetrahedraMesher` code paths.

## Consumers
- [x] Update editor preview to render shell tiers from the leaf frontier only.
- [x] Update editor summary to count shell triangles from the leaf frontier only.
- [x] Update impostor generation to use leaf-frontier `L2` plus branch-level `shellL2WoodMesh`.
- [x] Update generated mesh persistence for deterministic node-path assets and stale node cleanup.

## Validation And Tests
- [x] Replace legacy branch shell validation with hierarchy validation.
- [x] Rewrite canopy shell generation tests for hierarchical nodes and the MeshVoxelizer bake path.
- [x] Rewrite preview and authoring sync tests for hierarchical nodes.
- [x] Add tests for hierarchy creation, node topology, normals/degenerates, leaf-frontier triangle ordering, and impostor assembly.
- [x] Remove obsolete single-chain shell assertions and the legacy editor voxel-generation tests.

## Docs And Cleanup
- [x] Remove obsolete shell APIs, fields, and methods completely.
- [x] Update `UnityAssembledVegetation_FULL.md`.
- [x] Update `Milestone1.md`.
- [x] Update memory bank docs after implementation finishes.
- [x] Record compile/test status and any blockers in this file.

## Notes
- Progress for this workstream is recorded in this file during implementation.
- Runtime BRG/GPU phases remain out of scope for this pass, but the new node layout should remain flattenable for future runtime buffers.
- `Fully Compile by Unity` passed on `2026-03-30` after removing the legacy editor voxel pipeline and rebuilding the production bake path on `Packages/com.voxgeofol.vegetation/Runtime/MeshVoxelizerV1/`.
- Unity EditMode tests were rewritten to match the MeshVoxelizer hierarchy change but were not executed through the full Unity test runner in this pass.
- `Packages/com.voxgeofol.vegetation/Runtime/MeshVoxelizerV1/MeshVoxelizerHierarchyBuilder.cs`, `MeshVoxelizerHierarchyNode.cs`, and `MeshVoxelizerHierarchyDemo.cs` now back the validated hierarchy flow.
- `CanopyShellGenerator` and `ImpostorMeshGenerator` both consume `MeshVoxelizerHierarchyBuilder`; the old `Voxelizer`, `VoxelGrid`, and `MarchingTetrahedraMesher` files were removed from the editor assembly.
