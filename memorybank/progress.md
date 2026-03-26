# Progress

Purpose: track immediate tasks and current milestone status.

## Current Milestone: Milestone 1 - MVP Assembled Vegetation

Authority: [Milestone1.md](../DetailedDocs/Milestone1.md)

### Phase A: Foundation (Authoring Data Model + Validation Tests)
- [ ] Create folder structure + `Vegetation.asmdef`
- [ ] Implement `BranchPrototypeSO` with `woodMesh`, `foliageMesh`, and `leafColorTint`
- [ ] Implement `TreeBlueprintSO`, `LODProfileSO`, and `BranchPlacement`
- [ ] Match the real source asset contract directly: readable two-mesh branch prototypes
- [ ] Author first playable source assets from `branch_leaves_fullgeo` and `pine_branch_dense_needles`
- [ ] Create one demo `TreeBlueprintSO` and one `LODProfileSO`
- [ ] Implement `VegetationTreeAuthoring`
- [ ] Implement authoring validation logic (readability, opacity, budgets, bounds, scale)
- [ ] Write authoring validation EditMode tests
- [ ] Compile check + run tests

### Phase B: Shell Generation (Canopy Shell + Impostor Baking + Tests)
- [ ] Implement `Voxelizer` on foliage geometry
- [ ] Implement shell extraction (surface voxels to mesh)
- [ ] Implement `MeshSimplifier` (edge-collapse to budget)
- [ ] Implement `CanopyShellGenerator`
- [ ] Implement `ImpostorMeshGenerator` from merged tree-space shell L2 assembly
- [ ] Wire shell / impostor baking into SOs
- [ ] Write shell generation EditMode tests
- [ ] Compile check + run tests

### Phase C: Editor Preview
- [ ] Implement `VegetationEditorPreview` (R0 = trunk + branch wood + branch foliage + shellL0)
- [ ] Implement `VegetationTreeAuthoringEditor` (custom inspector)
- [ ] Wire bake buttons
- [ ] Manual visual verification
- [ ] Compile check

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
- Placement tools
- Cell streaming
