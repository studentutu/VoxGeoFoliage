# Progress

Purpose: track immediate tasks and current milestone status.

## Current Milestone

- Milestone: `Milestone 1 - MVP Assembled Vegetation`
- Scope authority: [Milestone1.md](../DetailedDocs/Milestone1.md)
- Runtime/data authority: [UnityAssembledVegetation_FULL.md](../DetailedDocs/UnityAssembledVegetation_FULL.md)
- Active phase: `Phase C.5 - Runtime MVP Preparation Gate`
- Blocked phases: `Phase D` and `Phase E` remain blocked until every `C5-*` item is closed

## Status Snapshot

- `Phase A` is complete: package layout, authoring ScriptableObjects, validation, demo source assets, and authoring EditMode tests are in place.
- `Phase B` core bake path is complete: hierarchical shell authoring, bounded generated outputs, impostor extraction, generated mesh persistence, and simplification/fallback handling are landed. `trunkL3Mesh` generation is still pending.
- `Phase C` editor tooling is complete: editor-side bake entry points, inspector/window preview flow, preview naming alignment, and validation UI cleanup are landed.
- Contract/doc sync was completed on `2026-04-05`: `UnityAssembledVegetation_FULL.md` is the single runtime/data authority, `Milestone1.md` owns scope/gates/done criteria, `Impostor` is one baked mesh only, and legacy `l3Distance` is dead data for Milestone 1.

## Active Gate

### Phase C.5 - Runtime MVP Preparation Gate

- [ ] `C5-01` Add `trunkL3Mesh` generation and persistence and expose it through the editor bake flow. Legacy `l3Distance` is ignored by Milestone 1 runtime logic and should be removed.
- [ ] `C5-02` Re-bake `Packages/com.voxgeofol.vegetation/Samples~/Vegetation Demo/VoxFoliage/TreeBlueprint_branch_leaves_fullgeo.asset` and confirm assigned `trunkL3Mesh`.
- [ ] `C5-03` Re-bake `Assets/Tree/VoxFoliage/TreeBlueprint_branch_leaves_fullgeo.asset` and confirm assigned `trunkL3Mesh`.
- [ ] `C5-04` Run manual in-Editor visual verification on both exact tree-blueprint assets for `L0`, `L1`, `L2`, `L3`, shell-only `L1`, shell-only `L2`, shell-only `L3`, and `Impostor`.
- [ ] `C5-05` Compare baked `L1/L2` compact hierarchies against `Assets/Tree/Raw/branch_leaves_quadcards.obj` as the current silhouette reference input.
- [ ] `C5-06` Tune `ShellBakeSettings` and `ImpostorBakeSettings` on both exact tree-blueprint assets until validation passes without `MeshLodUtility` fallback.
- [ ] `C5-07` Add validator/test coverage for BFS child ordering in ascending octant-bit order.
- [ ] `C5-08` Rerun compile validation and the Unity EditMode vegetation suite once the Unity Editor can be closed for the long runner path.

## Current Blockers

- `trunkL3Mesh` bake/persistence is not landed yet, so the shipped sample and repo-local mirror cannot satisfy the current runtime contract.
- Exact-asset rebake and manual verification are still missing on the two authoritative demo tree-blueprint assets.
- BFS child-order coverage and refreshed long-path validation evidence are still missing.


### Phase D: Spatial Grid + MVP Visibility/Decode Mirror
- [ ] Implement `VegetationSpatialGrid`
- [ ] Implement the MVP visibility path with GPU-preferred design and CPU fallback for tree-level `Culled` / `Expanded` / `Impostor` selection
- [ ] Mirror branch tier selection into runtime `L0/L1/L2/L3`
- [ ] Mirror BFS survivor decode into exact source-branch or shell-frontier draw-slot selection, with GPU preferred and non-blocking CPU fallback
- [ ] Write spatial grid EditMode tests
- [ ] Write classification EditMode tests
- [ ] Compile check + run tests

### Phase E: GPU Pipeline (Indirect Rendering + Renderer Feature)
- [ ] Implement `VegetationClassify.compute` tree-classify, branch-tier, and node-decision kernels
- [ ] Implement `VegetationCanopyLit.shader`
- [ ] Implement `VegetationTrunkLit.shader`
- [ ] Implement `VegetationFarMeshLit.shader`
- [ ] Implement `VegetationDepthOnly.shader`
- [ ] Implement `VegetationIndirectRenderer` with per-slot indirect draw registry and visible-instance buffers
- [ ] Implement GPU survivor decode when feasible, with the non-blocking CPU fallback bridge from GPU decision buffers into final indirect args
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
