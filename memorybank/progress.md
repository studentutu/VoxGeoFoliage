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
- [x] Implement Phase A authoring sync from assembled prefab into `TreeBlueprintSO` branches/bounds
- [x] Implement `VegetationTreeAuthoring` context actions to reconstruct or clear original branch hierarchy from blueprint data
- [x] Compile check + run tests

Status note:
- Full Unity compile (`Fully Compile by Unity`) passed on `2026-03-28`.
- Unity EditMode tests passed on `2026-03-28` (`runParsetests.sh`).
- Added `VegetationPhaseAAuthoringSync` to rebuild the demo authoring data from `Assets/Tree/tree_dense_branches.prefab`.
- `VegetationTreeAuthoring` now rebuilds branch children from `TreeBlueprintSO.branches` and can delete the original branch hierarchy from the assigned branch root.
- `Assets/Tree/VoxFoliage/TreeBlueprint_branch_leaves_fullgeo.asset` now contains 52 generated branch placements plus a linked `LODProfileSO`.
- `Assets/Tree/VoxFoliage/BranchPrototype_branch_leaves_fullgeo.asset` now uses mesh-derived `localBounds` and a foliage budget aligned with the real imported source mesh.

### Phase B: Shell Generation (Canopy Shell + Impostor Baking + Tests)
- [x] Implement `Voxelizer` on foliage geometry
- [x] Implement shell extraction (surface voxels to mesh)
- [x] Implement `MeshSimplifier` (vertex clustering + weld budget reduction for generated meshes)
- [x] Implement `CanopyShellGenerator`
- [x] Implement `ImpostorMeshGenerator` from merged tree-space shell L2 assembly plus simplified branch wood attachments
- [x] Wire shell / impostor baking into SOs
- [x] Implement `VegetationTreeAuthoring` context action to reconstruct from shells (by level) from blueprint data (necessary preview for the Phase B by developer).
- [x] Write shell generation EditMode tests
- [x] Compile check + run tests

Status note:
- Full Unity compile (`Fully Compile by Unity`) passed on `2026-03-28` after adding the Phase B files and regenerating the Unity solution.
- Rider MSBuild compile (`Compile by Rider MSBuild`) passed on `2026-03-28` after the final Phase B fixes.
- Unity EditMode tests passed on `2026-03-28` (`runParsetests.sh`).
- Added `ShellBakeSettings` and `ImpostorBakeSettings` under `Assets/Scripts/Features/Vegetation/Authoring/`.
- Added `VoxelGrid`, `Voxelizer`, `MeshSimplifier`, `GeneratedMeshAssetUtility`, `CanopyShellGenerator`, and `ImpostorMeshGenerator` under `Assets/Scripts/Features/Vegetation/Editor/`.
- Generated shell and impostor meshes now persist as standalone `.mesh` assets under `Assets/Scripts/Features/Vegetation/Runtime/Meshes/`.
- `CanopyShellGenerator` now also bakes `shellL1WoodMesh` and `shellL2WoodMesh` so branch wood remains attached in shell preview tiers.
- `VegetationTreeAuthoring` now reconstructs `ShellL0`, `ShellL1`, and `ShellL2` preview hierarchies with wood included from saved blueprint data.
- Added `Assets/EditorTests/Vegetation/CanopyShellGenerationTests.cs` plus shell-preview coverage in `AuthoringAssetSyncTests.cs`.

### Phase C: Editor Preview
- [x] Add interim `VegetationTreeAuthoring` context actions for `BakeCanopyShells`, `BakeImpostor`, and `BakeCanopyShellsAndImpostor`
- [ ] Implement `VegetationEditorPreview` (R0 = trunk + branch wood + branch foliage + shellL0)
- [ ] Implement `VegetationTreeAuthoringEditor` (custom inspector)
- [ ] Wire bake buttons
- [ ] Manual visual verification
- [ ] Compile check

Status note:
- `VegetationTreeAuthoring` is now the developer-facing interim entry point for shell and impostor baking while the dedicated Phase C inspector/buttons are still pending.
- Rider MSBuild compile passed on `2026-03-29`.
- Unity EditMode tests passed on `2026-03-29` (`runParsetests.sh`).

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
- Hierarchical sub-branch canopy shells
- Hierarchical wind system
- Scale quantization optimization
- Feature-grade placement tools (terrain scatter, paint). Basic editor-only `MassPlacement` physical-ground scatter already exists under `Assets/Scripts/MassPlacement`.
- Cell streaming
