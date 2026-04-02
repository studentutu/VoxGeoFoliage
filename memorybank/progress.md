# Progress

Purpose: track immediate tasks and current milestone status.

## Current Milestone: Milestone 1 - MVP Assembled Vegetation

Authority: [Milestone1.md](../DetailedDocs/Milestone1.md)

## Completed Summary

- Phase A foundation is complete: the vegetation package folder structure, authoring ScriptableObjects, validation logic, demo source assets, `VegetationTreeAuthoring`, and authoring EditMode tests are in place. Authoring now keeps placements and bounds directly on the ScriptableObjects; only generated shell and impostor meshes are regenerated.
- Phase B shell and impostor baking is complete: hierarchical `BranchPrototypeSO.shellNodes`, the `VoxelizerV2` CPU volume backend cutover, direct original-tree impostor extraction, generated mesh persistence into owner-local `GeneratedMeshes/`, and updated EditMode coverage all landed. Latest compile validations for this phase passed on `2026-04-01`.
- Phase C editor tooling is implemented: preview and bake entry points were moved into the editor assembly, the custom inspector and dedicated window are wired, and preview-related EditMode coverage is in place. Compile passed on `2026-03-29`.
- Public package preparation is complete: the vegetation feature now lives in `Packages/com.voxgeofol.vegetation`, distributable demo content ships from `Samples~/Vegetation Demo`, the repo-local mirror remains under `Assets/Tree`, and generated meshes stay in writable `Assets/` space.

## Open Tasks

### Phase C Follow-up Mesh simplification, hard poly count reduction.
- [ ] Missing mesh simplification utility (find a suitablabe drop-in replacement from Unity asset store or github or create one)
    - simplification can use the same voxelizer just with single depth and different resolution (resolution should be provided, example 10 for CPUVoxelizer gives decent simplified result for L1 mesh, 50 for L0 mesh, branches R1 with 50, R2 with 20, imposter with 10), those settings should be moved to branch scriptable object and for imposter to tree scriptable object in order to be updated from editor. 
    - should only wield vertices and minimize number of triangles on voxel faces
    - should not change voxel geometry (ideally just wield vertices on the same plane)
    - optionally trim backfaces (needed for imposter)
- [ ] Missing mesh simplification for R1/R2 branches
    - when simplifying ensure references Scriptable objects are updated with new simplified mesh.
- [ ] Missing mesh simplification on each of the node of the canopy shell
    - we have validations, but no simplification. See validations in `VegetationTreeAuthoringEditorPanel`
    - we need to make simplification on each node while it is still being generated (to minimize number of triangles as much as possible, wield vertices)
- [ ] Missing mesh simplification for imposter mesh (should only be front facing mesh, no backside)
    - when simplifying ensure references Scriptable objects are updated with new simplified mesh.
- [ ] Support voxelization by Texture (by an alpha-cutout masked texture). See usage of `GPUVoxelizer` in `GPUTextureDemo` (uses `Voxelizer.compute`)
- [ ] Run manual in-Editor visual verification for preview tiers and bake flows.
- [ ] Ask developer to compare L1/L2/L3 shells to the `branch_leaves_quadcards` version.

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
