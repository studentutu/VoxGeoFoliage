# Progress

Purpose: track immediate tasks and current milestone status.

## Current Milestone: Milestone 1 - MVP Assembled Vegetation

Authority: [Milestone1.md](../DetailedDocs/Milestone1.md)

### Phase A: Foundation (Authoring Data Model + Validation Tests)
- [x] Create folder structure + `Vegetation.asmdef`
- [x] Implement `BranchPrototypeSO` with `woodMesh`, `foliageMesh`, and `leafColorTint`
- [x] Implement `TreeBlueprintSO`, `LODProfileSO`, and `BranchPlacement`
- [x] Match the real source asset contract directly: readable two-mesh branch prototypes
- [x] Author first source assets from `branch_leaves_fullgeo` (only from it, see Assets/Tree/VoxFoliage)
- [x] Create one demo `TreeBlueprintSO` and one `LODProfileSO` (see Assets/Tree/VoxFoliage)
- [x] Implement `VegetationTreeAuthoring`
- [x] Implement authoring validation logic (readability, opacity, budgets, bounds, scale)
- [x] Write authoring validation EditMode tests
- [x] Keep tree blueprint authoring data directly on the ScriptableObjects; no prefab-to-blueprint sync remains in the package
- [x] Keep an explicit branch-root reference on `VegetationTreeAuthoring` for editor preview reconstruction
- [x] Compile check + run tests

Status note:
- Full Unity compile (`Fully Compile by Unity`) passed on `2026-03-28`.
- Unity EditMode tests passed on `2026-03-28` (`runParsetests.sh`).
- `2026-03-31`: removed the obsolete prefab-to-blueprint sync utility from the public package; current authoring keeps branch placements/bounds directly on the ScriptableObjects and only regenerates shell/impostor meshes.
- `VegetationTreeAuthoring` clean scene binding and retains the assigned branch root used by the extracted Phase C editor preview tooling.
- `Assets/Tree/VoxFoliage/TreeBlueprint_branch_leaves_fullgeo.asset` now contains 52 authored branch placements plus a linked `LODProfileSO`.
- `Assets/Tree/VoxFoliage/BranchPrototype_branch_leaves_fullgeo.asset` now uses mesh-derived `localBounds` and a foliage budget aligned with the real imported source mesh.

### Phase B: Shell Generation (Canopy Shell + Impostor Baking + Tests)
- [x] Implement MeshVoxelizer-based hierarchical canopy generation on foliage geometry
- [x] Implement shell extraction by splitting L0 surface voxels into octant nodes
- [x] Rebuild `CanopyShellGenerator` on `MeshVoxelizerHierarchyBuilder`
- [x] Replace the MVP single-chain branch shell model with hierarchical `shellNodes`
- [x] Rebuild `ImpostorMeshGenerator` on direct original-tree CPU voxel extraction
- [x] Wire shell / impostor baking into SOs
- [x] Shape bake outputs so editor preview can reconstruct shell tiers by level from blueprint data
- [x] Rewrite shell generation EditMode tests around the hierarchy builder and new bake path
- [x] Remove obsolete editor voxel generation code and legacy tests
- [x] Compile check
- [ ] Run tests

Status note:
- Full Unity compile (`Fully Compile by Unity`) passed on `2026-03-30` after removing the legacy editor voxel pipeline and rebuilding the Phase B bake path around `MeshVoxelizerHierarchyBuilder`.
- Rider MSBuild compile (`Compile by Rider MSBuild`) passed on `2026-03-31` after simplifying impostor baking to direct original-tree surface extraction without temporary hierarchy allocation.
- Full Unity compile (`Fully Compile by Unity`) passed on `2026-04-01` after switching production impostor baking to the `VoxelizerV2` CPU volume + surface path and adding the new runtime voxel helpers.
- Rider MSBuild compile (`Compile by Rider MSBuild`) passed on `2026-04-01` after switching the canopy hierarchy builder internals to the `VoxelizerV2` CPU volume backend while preserving the existing hierarchy node contract.
- The authoritative branch canopy data is now `BranchPrototypeSO.shellNodes`; branch-wide `shellL0Mesh/shellL1Mesh/shellL2Mesh` were removed.
- `2026-04-01`: canopy shell resolution defaults were updated for the CPU voxel backend to `80/16/6` (`L0/L1/L2`) across `ShellBakeSettings`, `MeshVoxelizerHierarchyBuilder`, tests, and the demo scene.
- `CanopyShellGenerator` now voxelizes readable foliage at `80/16/6`, splits L0 surface voxels into octant nodes, and persists one `L0/L1/L2` mesh triplet per node.
- `ShellBakeSettings` now expose `maxOctreeDepth`, `voxelResolutionL0`, `voxelResolutionL1`, `voxelResolutionL2`, and `minimumSurfaceVoxelCountToSplit`.
- `ImpostorMeshGenerator` now merges the original tree meshes (`trunkMesh` + placed branch `woodMesh`/`foliageMesh`) and extracts a coarse size-4 impostor surface through the `VoxelizerV2` CPU volume + surface path; baked canopy shells are no longer required for impostor generation.
- `2026-04-01`: `Runtime/MeshVoxelizerV1/MeshVoxelizerHierarchyBuilder.cs` now uses the `VoxelizerV2` CPU volume backend internally, while `DetailedDocs/VoxelizerBackendInvestigation.md` records the follow-up recommendation to remove or archive the obsolete ray-traced voxelizer path after validation.
- `ImpostorBakeSettings` now expose `voxelResolution` with default `4`.
- `TreeBlueprintSO` now exposes `generatedImpostorMeshesRelativeFolder`, matching the branch prototype shell-folder override pattern so impostor meshes can be written into a caller-selected project folder.
- Editor preview, authoring validation, and summary accounting consume the leaf frontier of the hierarchy; impostor generation now voxelizes the original assembled tree meshes directly.
- Unity EditMode tests were rewritten for the MeshVoxelizer hierarchy path on `2026-03-30`, but they were not rerun through the full Unity test runner in this pass.
- The directly affected bake tests were minimized on `2026-03-31` by replacing the shell-dependent impostor cases with one coarse original-tree impostor test and one editor-utility impostor persistence test.
- Added `ShellBakeSettings` and `ImpostorBakeSettings` under `Packages/com.voxgeofol.vegetation/Runtime/Authoring/`.
- Added `BranchShellNode`, `BranchShellNodeUtility`, `GeneratedMeshAssetUtility`, `CanopyShellGenerator`, `ImpostorMeshGenerator`, and `Runtime/MeshVoxelizerV1/*` under the vegetation package.
- Generated shell and impostor meshes now persist as standalone `.mesh` assets under owner-local `GeneratedMeshes/` folders in `Assets/`.
- `CanopyShellGenerator` now bakes node-local `L0/L1/L2` shells plus branch-level `shellL1WoodMesh` and `shellL2WoodMesh` so branch wood remains attached in shell preview tiers.
- Removed obsolete `Voxelizer`, `VoxelGrid`, and `MarchingTetrahedraMesher` from `Packages/com.voxgeofol.vegetation/Editor/`.
- Added `Packages/com.voxgeofol.vegetation/Tests/Editor/CanopyShellGenerationTests.cs` plus shell-preview coverage in `VegetationEditorAuthoringTests.cs`.
- `2026-03-30`: real branch validation exposed incorrect shell output from the first hierarchical baker; the production bake path now uses `Runtime/MeshVoxelizerV1/MeshVoxelizerHierarchyBuilder.cs` and `MeshVoxelizerHierarchyNode.cs`, with `MeshVoxelizerHierarchyDemo.cs` retained as the manual validation tool.

### Phase B Follow-up: MeshVoxelizer Rewrite Investigation
- [x] Add an experimental MeshVoxelizer-based hierarchy sample for the canopy shell levels
- [x] Replace the production `CanopyShellGenerator` bake path with the MeshVoxelizer-based hierarchy once the split strategy is validated on real branches
- [x] Cut over the production impostor baker to the `VoxelizerV2` CPU volume + surface backend
- [x] Cut over the production canopy hierarchy builder internals to the `VoxelizerV2` CPU volume backend
- [x] Document the follow-up recommendation to remove or archive the obsolete ray-traced voxelizer path
- [ ] Run the full Unity EditMode test suite on the rebuilt Phase B bake path

### Phase C: Editor Preview
- [x] Extract preview and bake entry points out of `VegetationTreeAuthoring` into editor-only utilities
- [x] Implement `VegetationEditorPreview` (R0 = trunk + branch wood + branch foliage + shellL0)
- [x] Implement `VegetationTreeAuthoringEditor` + `VegetationTreeAuthoringWindow`
- [x] Wire bake buttons
- [x] Manual visual verification
- [x] Compile check

Status note:
- Added `VegetationEditorPreview`, `VegetationTreeAuthoringEditorUtility`, `VegetationTreeAuthoringEditorPanel`, `VegetationTreeAuthoringEditor`, and `VegetationTreeAuthoringWindow` under `Packages/com.voxgeofol.vegetation/Editor/`.
- `VegetationTreeAuthoring` is back to being a clean scene binding plus validation surface; preview reconstruction and bake buttons now live entirely in the editor assembly.
- Full Unity compile (`Fully Compile by Unity`) passed on `2026-03-29` after adding the Phase C editor files and regenerating the solution.
- `Packages/com.voxgeofol.vegetation/Tests/Editor/VegetationEditorAuthoringTests.cs` now exercises the extracted preview utility and editor utility bake entry point.
- Manual in-Editor visual verification is still pending.

## Public Package Preparation
- [x] Pack the vegetation feature into `Packages/com.voxgeofol.vegetation`
- [x] Split package content into `Runtime`, `Editor`, `Tests`, `Documentation~`, and `Samples~`
- [x] Move non-essential vegetation demo assets into package sample source at `Packages/com.voxgeofol.vegetation/Samples~/Vegetation Demo`
- [x] Keep a repo-local mirror of the demo assets under `Assets/Tree` for current scenes
- [x] Change generated mesh persistence to owner-local `GeneratedMeshes/` folders in `Assets/`
- [x] Run required full Unity compile after the package migration

Status note:
- the vegetation feature became an embedded public package rooted at `Packages/com.voxgeofol.vegetation`.
- Vegetation EditMode coverage moved from `Assets/EditorTests/Vegetation` to `Packages/com.voxgeofol.vegetation/Tests/Editor`.
- Public package consumers now rely on authoring ScriptableObjects plus generated mesh assets; no demo-only prefab sync remains in the package.
- Full Unity compile (`Fully Compile by Unity`) passed on `2026-03-29` after the package migration.

### Phase D: Spatial Grid + CPU Classification
- [ ] Implement `VegetationSpatialGrid`
- [ ] Implement `VegetationClassifier` (CPU mirror)
- [ ] Mirror tree-blueprint expansion into branch draw-slot selection
- [ ] Write spatial grid EditMode tests
- [ ] Write classification EditMode tests
- [ ] Compile check + run tests

### Phase E: GPU Pipeline (Shaders + BRG + Renderer Feature)
- [ ] Implement `VegetationClassify.compute`
- [ ] Implement `VegetationCanopyLit.shader`
- [ ] Implement `VegetationTrunkLit.shader`
- [ ] Implement `VegetationImpostorLit.shader`
- [ ] Implement `VegetationDepthOnly.shader`
- [ ] Implement `VegetationBRGManager` with per-blueprint / per-prototype draw-slot registry
- [ ] Implement `VegetationRendererFeature` + `VegetationRenderPass`
- [ ] Implement `VegetationRuntimeManager` with GPU static-buffer flattening
- [ ] End-to-end manual test in a demo scene
- [ ] Compile check + manual verification

## Deferred
- HiZ depth pyramid occlusion
- LOD transition dithering / cross-fade
- Runtime streaming / dynamic loading
- Hierarchical wind system
- Scale quantization optimization
- Feature-grade placement tools (terrain scatter, paint). Basic editor-only `MassPlacement` physical-ground scatter already exists under `Assets/Scripts/MassPlacement`.
- Cell streaming
