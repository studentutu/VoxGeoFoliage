# Progress

Purpose: track immediate tasks and current milestone status.

## Current Milestone: Milestone 1 - MVP Assembled Vegetation

Authority: [Milestone1.md](../DetailedDocs/Milestone1.md)

## Completed Summary

- Phase A foundation is complete: the vegetation package folder structure, authoring ScriptableObjects, validation logic, demo source assets, `VegetationTreeAuthoring`, and authoring EditMode tests are in place. Authoring now keeps placements and bounds directly on the ScriptableObjects; only generated shell and impostor meshes are regenerated.
- Phase B shell and impostor baking is complete: hierarchical branch-shell authoring, the `VoxelizerV2` CPU volume backend cutover, direct original-tree impostor extraction, generated mesh persistence into owner-local `GeneratedMeshes/`, and updated EditMode coverage all landed. Latest compile validations for this phase passed on `2026-04-01`.
- Phase C editor tooling is implemented: preview and bake entry points were moved into the editor assembly, the custom inspector and dedicated window are wired, and preview-related EditMode coverage is in place. Compile passed on `2026-03-29`.
- The authoring panel validation UI is now less noisy: warning/error counts are surfaced in the summary block, and the full validation issue list is collapsible in the shared inspector/window panel. Rider MSBuild compile passed on `2026-04-04`.
- Phase C shell-boundary and compact-hierarchy follow-up is implemented: canonical `shellNodesL0` now drives ownership and split decisions, compact `shellNodesL1` / `shellNodesL2` are rebuilt from owned `L0` occupancy at their authored resolutions, generated shell/wood/impostor meshes are clipped back to authoritative source bounds, shell fallback stays mesh-only after voxel generation, and over-budget generation still logs `Debug.LogError` without aborting the bake.
- Unity `MeshLodUtility` fallback root-cause handling is now in place: vegetation fallback and local demo scripts rebuild the LOD input mesh with explicit `SetTriangles` index buffers before calling `MeshLodUtility`, still skip unsupported meshes cleanly when needed, and log warnings instead of surfacing Unity errors. Rider MSBuild compile passed on `2026-04-04`.
- Repo-local simplification inspection tooling is now in place: `Assets/Scripts/CheckVoxelMeshSimplification.cs` can compare an existing source mesh against raw voxel output, reduced voxel output, and Unity `MeshLodUtility` output from the same inspector-driven demo component. Full Unity compile passed on `2026-04-03`.
- Public package preparation is complete: the vegetation feature now lives in `Packages/com.voxgeofol.vegetation`, distributable demo content ships from `Samples~/Vegetation Demo`, the repo-local mirror remains under `Assets/Tree`, and generated meshes stay in writable `Assets/` space.
- Architecture authority was reconciled on `2026-04-05`: Milestone 1 now targets URP plus `Graphics.RenderMeshIndirect`, runtime `L0/L1/L2/L3 + Impostor`, hybrid `CPU/GPU` cell visibility and survivor decode with GPU preferred, non-blocking CPU fallback when used, and persisted-invalid generated meshes.
- Authoring contract alignment landed on `2026-04-05`: `LODProfileSO` now uses authored distance bands, `TreeBlueprintSO` now exposes `trunkL3Mesh`, validator bounds checks now cover simplified wood/trunk/impostor meshes, and preview/summary naming now matches runtime `L0/L1/L2/L3/Impostor`.

## Open Tasks

### Phase B / C Alignment Follow-up
- [ ] Add `trunkL3Mesh` generation and persistence so the authored outputs fully match the current runtime contract.

### Phase C Follow-up Verification and Tuning
- [ ] Run manual in-Editor visual verification for runtime `L0`, `L1`, `L2`, `L3`, shell-only views, and `Impostor` on the real demo assets.
- [ ] Compare baked `L1/L2` compact hierarchies against the `branch_leaves_quadcards` reference assets to judge silhouette retention and canopy mass.
- [ ] Tune `ShellBakeSettings` and `ImpostorBakeSettings` on the sample assets until validation budgets pass without depending on last-resort `MeshLodUtility` fallback.
- [ ] Decide whether the compact-tier merge/prune heuristic needs tighter equivalence checks after manual review identifies weak merges.
- [ ] Rerun the Unity EditMode vegetation suite once the Unity Editor can be closed for the long runner path.

Current follow-up focus is verification plus runtime-alignment follow-up. The authoring implementation now exposes the runtime naming contract directly, uses authored distance bands, and validates strict authoritative bounds across shell, wood, simplified trunk, and impostor meshes; the remaining mismatch is `trunkL3Mesh` generation/persistence and the hybrid runtime path with GPU-preferred visibility/decode plus CPU fallback.



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
