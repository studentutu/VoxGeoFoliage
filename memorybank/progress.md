# Progress

Purpose: track immediate tasks and current milestone status.

## Current Milestone: Milestone 1 - MVP Assembled Vegetation

Authority: [Milestone1.md](../DetailedDocs/Milestone1.md)

## Completed Summary

- Phase A foundation is complete: the vegetation package folder structure, authoring ScriptableObjects, validation logic, demo source assets, `VegetationTreeAuthoring`, and authoring EditMode tests are in place. Authoring now keeps placements and bounds directly on the ScriptableObjects; only generated shell and impostor meshes are regenerated.
- Phase B shell and impostor baking is complete: hierarchical `BranchPrototypeSO.shellNodes`, the `VoxelizerV2` CPU volume backend cutover, direct original-tree impostor extraction, generated mesh persistence into owner-local `GeneratedMeshes/`, and updated EditMode coverage all landed. Latest compile validations for this phase passed on `2026-04-01`.
- Phase C editor tooling is implemented: preview and bake entry points were moved into the editor assembly, the custom inspector and dedicated window are wired, and preview-related EditMode coverage is in place. Compile passed on `2026-03-29`.
- Phase C mesh-simplification follow-up is implemented: shell-node generation now supports topology-preserving coplanar face reduction, `L0` reduction is separately switchable, generated `shellL1WoodMesh` / `shellL2WoodMesh` now come from voxelized meshes instead of source clones, impostor generation uses the same reduction/fallback pipeline, and over-budget generation now logs `Debug.LogError` without aborting the bake. Existing automated bake tests explicitly skip simplification/fallback so they remain fast. Full Unity compile passed on `2026-04-03`.
- Repo-local simplification inspection tooling is now in place: `Assets/Scripts/CheckVoxelMeshSimplification.cs` can compare an existing source mesh against raw voxel output, reduced voxel output, and Unity `MeshLodUtility` output from the same inspector-driven demo component. Full Unity compile passed on `2026-04-03`.
- Public package preparation is complete: the vegetation feature now lives in `Packages/com.voxgeofol.vegetation`, distributable demo content ships from `Samples~/Vegetation Demo`, the repo-local mirror remains under `Assets/Tree`, and generated meshes stay in writable `Assets/` space.

## Open Tasks

### Phase C Follow-up Mesh simplification, hard poly count reduction.
- [ ] Run manual in-Editor visual verification for preview tiers and bake flows.
- [ ] Use `CheckVoxelMeshSimplification` on one or more real foliage/branch meshes to compare raw voxel, reduced voxel, and Unity LOD triangle counts and shape preservation before further tuning.
- [ ] Ask developer to compare L1/L2/L3 shells to the `branch_leaves_quadcards` version.
- [ ] Tune real sample-asset bake settings until the generated meshes satisfy validation budgets without relying on last-resort `MeshLodUtility` fallback.

#### **Phase C Implementation plan**

##### **Summary**
- Keep `ShellBakeSettings` authoritative for the initial canopy bake. `L0/L1/L2` always start from the authored voxel resolutions.
- Add topology-preserving reduction to generated voxel meshes by merging adjacent coplanar faces and removing redundant triangles while preserving voxel silhouette and bounds.
- Extend that reduction to `L0` shell-node meshes, but keep a dedicated skip toggle for `L0` so the developer can inspect unreduced near-detail output.
- Do not stop baking on budget failure. Persist the best generated result, continue the bake, and emit `Debug.LogError` immediately at the point of failure.

##### **Public API / Settings Changes**
- Add these booleans to `ShellBakeSettings`, default `false`:
  - `skipReduction`
  - `skipL0Reduction`
  - `skipSimplifyFallback`
- Keep `ShellBakeSettings` as the authority for branch-shell generation behavior, including the existing shell resolutions and the planned wood-resolution fields.
- Add matching fast-path control to `ImpostorBakeSettings`:
  - `skipReduction`
  - `skipSimplifyFallback`
- Add an internal mesh-build options object for the voxel-surface pipeline so reduction/fallback switches remain explicit and test setup can disable them cleanly.

##### **Implementation Changes**
- Refactor generated mesh processing into explicit stages:
  1. Build the raw voxel surface mesh from occupancy.
  2. If allowed by settings, run topology-preserving reduction:
     - merge adjacent coplanar voxel faces,
     - weld merged-face corners as part of that reduction pass,
     - remove redundant and degenerate triangles,
     - rebuild bounds and normals.
  3. If still over budget and fallback is allowed, run bounded simplification fallback.
  4. Persist the selected candidate and log errors immediately when a candidate still misses budget.
- `L0` shell nodes:
  - route every generated `shellL0Mesh` through the reduction stage,
  - gate it with `skipL0Reduction`,
  - never lower `L0` voxel resolution automatically.
- `L1/L2` shell nodes:
  - route through the same reduction stage,
  - gate with `skipReduction`,
  - if still over budget and `skipSimplifyFallback == false`, allow at most two rebakes, with `L1` never dropping below the current `L2`.
- Generated wood attachments:
  - replace cloning with voxelized generated meshes,
  - apply the same reduction stage,
  - gate with `skipReduction`.
- Impostor meshes:
  - keep the current source assembly flow,
  - apply reduction only when `ImpostorBakeSettings.skipReduction == false`,
  - apply bounded fallback only when `ImpostorBakeSettings.skipSimplifyFallback == false`.
- Fallback behavior:
  - max two rebakes per generated mesh,
  - Unity 6.3 `MeshLodUtility.GenerateMeshLods` remains the last resort after retry exhaustion,
  - if fallback is skipped or exhausted, persist the lowest-triangle candidate produced in this bake.
- Logging behavior:
  - do not aggregate errors,
  - emit `Debug.LogError` directly at each over-budget or fallback-exhausted decision point,
  - include asset name, mesh kind, tier/node info, triangle count, budget, and whether fallback was skipped or exhausted.

##### **Verification**
- Do not add new tests in this phase.
- Update existing editor test helpers and fixture settings so all simplification is skipped during automated tests:
  - shell tests use `skipReduction = true`, `skipL0Reduction = true`, `skipSimplifyFallback = true`,
  - impostor-related tests use `skipReduction = true`, `skipSimplifyFallback = true`.
- Keep existing assertions focused on hierarchy generation, persistence, validation, and editor wiring; tests continue using the fast raw-generation path.
- `Fully Compile by Unity` passed on `2026-04-03`.
- Manual editor verification remains required for `R0`, `R1`, `R2`, `ShellL0`, `ShellL1`, `ShellL2`, and `R3`.

##### **Assumptions**
- Direct `Debug.LogError` calls are preferred over any aggregated reporting in this phase.
- The primary goal is correctness and optional-path control for simplification, not polished diagnostics.
- Validation budgets remain hard validation errors even though baking itself becomes non-blocking.



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

### Phase F: Optional improvements

- [ ] Support voxelization by Texture (by an alpha-cutout masked texture). See usage of `GPUVoxelizer` in `GPUTextureDemo` (uses `Voxelizer.compute`)
    - this will allow existing branches from quadcards to be used instead (they are far more easier to find).

## Deferred

- HiZ depth pyramid occlusion
- LOD transition dithering / cross-fade
- Runtime streaming / dynamic loading
- Hierarchical wind system
- Scale quantization optimization
- Feature-grade placement tools (terrain scatter, paint). Basic editor-only `MassPlacement` physical-ground scatter already exists under `Assets/Scripts/MassPlacement`.
- Cell streaming
