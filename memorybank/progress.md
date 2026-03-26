# Progress

Purpose: track immediate tasks and current milestone status.

## Current Milestone: Milestone 1 — MVP Assembled Vegetation

Authority: [Milestone1.md](../DetailedDocs/Milestone1.md)

### Phase A: Foundation (Authoring Data Model + Validation Tests)
- [ ] Create folder structure + Vegetation.asmdef
- [ ] Implement `BranchPrototypeSO`, `TreeBlueprintSO`, `LODProfileSO`, `BranchPlacement`
- [ ] Implement `VegetationTreeAuthoring` MonoBehaviour
- [ ] Implement authoring validation logic (static validator class)
- [ ] Write authoring validation EditMode tests
- [ ] Compile check + run tests

### Phase B: Shell Generation (Canopy Shell + Impostor Baking + Tests)
- [ ] Implement `Voxelizer` (triangle-voxel intersection)
- [ ] Implement shell extraction (surface voxels → mesh)
- [ ] Implement `MeshSimplifier` (edge-collapse to budget)
- [ ] Implement `CanopyShellGenerator` (orchestrates pipeline)
- [ ] Implement `ImpostorMeshGenerator`
- [ ] Wire into SOs
- [ ] Write shell generation EditMode tests
- [ ] Compile check + run tests

### Phase C: Editor Preview
- [ ] Implement `VegetationEditorPreview` (spawn/destroy per tier)
- [ ] Implement `VegetationTreeAuthoringEditor` (custom inspector)
- [ ] Wire bake buttons
- [ ] Manual visual verification
- [ ] Compile check

### Phase D: Spatial Grid + CPU Classification
- [ ] Implement `VegetationSpatialGrid`
- [ ] Implement `VegetationClassifier` (CPU mirror)
- [ ] Write spatial grid EditMode tests
- [ ] Write classification EditMode tests
- [ ] Compile check + run tests

### Phase E: GPU Pipeline (Shaders + BRG + Renderer Feature)
- [ ] Implement `VegetationClassify.compute`
- [ ] Implement `VegetationSimpleLit.shader`
- [ ] Implement `VegetationImpostorLit.shader`
- [ ] Implement `VegetationDepthOnly.shader`
- [ ] Implement `VegetationBRGManager`
- [ ] Implement `VegetationRendererFeature` + `VegetationRenderPass`
- [ ] Implement `VegetationRuntimeManager`
- [ ] End-to-end manual test
- [ ] Compile check + manual verification

## Deferred
- Occlusion culling (HiZ depth pyramid)
- LOD transition dithering/cross-fade
- Runtime streaming / dynamic loading
- Hierarchical Sub-Branch Canopy Shells
- Hierarchical wind system
- Scale quantization optimization
- Placement tools
- Cell streaming
