# Progress

Purpose: track the active milestone, the current blockers, and the next concrete tasks only.

## Current Milestone

- Milestone: `Milestone 2 - Wind and Production Improvements`
- Scope authority: [Milestone2.md](../DetailedDocs/Milestone2.md)
- Runtime/data authority: [UnityAssembledVegetation_FULL.md](../DetailedDocs/UnityAssembledVegetation_FULL.md)
- Finished baseline: [Milestone1.md](../DetailedDocs/Milestone1.md)

## Immediate Tasks

### Completed

- Landed the urgent tree-first runtime redesign from `urgentRedesign.md`.
- Added the required `treeL3Mesh` authoring/runtime contract and branch split canopy/wood tiers for `L1/L2/L3`.
- Removed urgent-path runtime ownership of `SceneBranches[]` and prototype shell-node buffers from registration, GPU classification, and submission.
- Replaced the frame path with tree-first acceptance, nearest-first promotion into branch-expanded `L2/L1/L0`, promoted-tree-only compact branch work generation, and final indirect submission over registered draw slots after bind.
- Added urgent-path telemetry for visible trees, accepted `TreeL3`, promoted trees, rejected promotions, expanded branch work-item count, accepted tier-cost usage, and non-zero emitted slots.
- Removed the per-frame CPU active-slot compaction from `BindGpuResidentFrame()` after it regressed the hot path to a constant multi-millisecond stall. Non-zero emitted slots remain diagnostics-only telemetry until submission compaction can return without a synchronous bind-path readback.
- Added shader-level shadow support to the bundled package materials: main-light shadow attenuation in the forward pass and `ShadowCaster` passes for canopy, trunk, and far-mesh shaders. Production indirect runtime shadow-atlas submission is still unresolved follow-up work.
- Recompiled through both `Compile by Rider MSBuild` and `Fully Compile by Unity` with no compile errors.

### Next Validation

- Run the dense-forest one-container scene and confirm the telemetry/visual behavior against `DetailedDocs/urgentRedesign.md`: every visible non-far tree holds at least `TreeL3`, promotions happen nearest-first, and the current registered-slot submission surface remains acceptable while non-zero emitted slots stay diagnostics-only telemetry.

### Milestone 2 Breakdown

- Freeze the public runtime material compatibility contract: direct-compatible materials, explicit adapter/binding path, and hard-fail validation for unsupported materials.
- Remove `VegetationIndirectMaterialFactory` as the public material authority so runtime does not silently replace authored materials with package-only shaders.
- Enforce shader contract for the custom materials, test on the package material (enforce to use package supportVegetation.hlsl, not yet implemented).
- Resolve draw-slot identity from the final runtime-compatible material pair, not from stale pre-conversion assumptions.
- Freeze the first-pass hierarchical wind contract: global wind inputs, species-level profile, per-tree phase seed, and tier-specific deformation rules.
- Implement wind support in the compatible material path so custom project materials are not locked out of the wind system.
- Freeze the `GPUVoxelizer` masked-quad bake input contract and implement canopy-shell generation from quad and alpha-masked branch inputs.
- Run a clean URP package-consumer smoke pass and tighten docs around custom materials, masked-quad bake support, depth-pass requirements, and runtime setup.

## Improvement right after Milestone

- Dithered LOD transitions
- DFS hierarchy migration plus subtree spans
- Scale quantization optimization
- Full HiZ depth pyramid occlusion for tree and branch work after prioritization is shipped and measured

## Deferred

- Feature-grade placement tools
- Optional texture/quad-card branch baking follow-up
