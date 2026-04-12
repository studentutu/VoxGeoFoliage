# Progress

Purpose: track the active milestone, the current blockers, and the next concrete tasks only.

## Current Milestone

- Milestone: `Milestone 2 - Wind and Production Improvements`
- Scope authority: [Milestone2.md](../DetailedDocs/Milestone2.md)
- Runtime/data authority: [UnityAssembledVegetation_FULL.md](../DetailedDocs/UnityAssembledVegetation_FULL.md)
- Finished baseline: [Milestone1.md](../DetailedDocs/Milestone1.md)

## Immediate Tasks

### Urgent runtime redesign

- Replace slot-order overflow with the `urgentRedesign.md` pipeline: guaranteed `TreeL3` floor first, then nearest-tree promotion into branch-expanded `L2/L1/L0`.
- Move urgent-path acceptance ownership to `TreeInstances[]` and remove pre-populated `SceneBranches[]` plus runtime shell-node ownership from the runtime path.
- Add the required tree-level `TreeL3` mesh contract and bake output for the guaranteed floor.
- Freeze `L0` as survived original branches and freeze branch `L1/L2/L3` as separate canopy and wood runtime tiers with no BFS.
- Add explicit per-frame tree acceptance records before per-slot packing.
- Remove pre-populated `SceneBranches[]` and `ShellNodesL1/L2/L3[]` from the runtime path instead of slimming them; keep only reusable blueprint placements, compact branch prototype tier meshes, and a compact promoted-tree branch worklist.
- Replace the urgent frame path with tree-first acceptance plus promoted-tree-only branch count/pack/emit so one visible tree chooses one accepted representation per frame before any expanded branch work exists.
- Add active-slot filtering / non-zero-slot compaction after accepted-content emission so final submissions track the non-zero emitted slot set instead of every registered slot after bind.
- Add dense-forest validation for one container with around `6000` tightly packed trees and camera inside the forest.
- Replace branch-shell BFS bake outputs with no-BFS branch split canopy/wood tiers and update validation around `TreeL3`, branch `L1/L2/L3`, and far impostor ownership.
- Add overflow telemetry last: visible trees, accepted `TreeL3`, promoted trees, rejected promotions, compact expanded-branch work-item count, non-zero accepted slots, and shared-budget usage.

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
