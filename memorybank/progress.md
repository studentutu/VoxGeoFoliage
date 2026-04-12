# Progress

Purpose: track the active milestone, the current blockers, and the next concrete tasks only.

## Current Milestone

- Milestone: `Milestone 2 - Wind and Production Improvements`
- Scope authority: [Milestone2.md](../DetailedDocs/Milestone2.md)
- Runtime/data authority: [UnityAssembledVegetation_FULL.md](../DetailedDocs/UnityAssembledVegetation_FULL.md)
- Finished baseline: [Milestone1.md](../DetailedDocs/Milestone1.md)

## Status Snapshot

- `2026-04-11`: Milestone 1 is finished. The shipped runtime path is GPU-resident only through `VegetationRuntimeContainer`, `VegetationGpuDecisionPipeline`, `VegetationIndirectRenderer`, and `VegetationRendererFeature`.
- `2026-04-12`: Runtime scaling hardening landed. Shell-node runtime caches stay prototype-local, branch and shell-node bounds are generated on GPU per frame from transforms, and `VegetationRuntimeContainer.maxVisibleInstanceCapacity` now hard-bounds the shared visible-instance buffer instead of reserving scene-scale per-slot/node memory.
- `2026-04-12`: `VegetationRuntimeContainer.maxVisibleInstanceCapacity` default was raised to `262144`, and changing that serialized value now forces the GPU pipeline to rebuild so higher per-container budgets actually apply without a full registration rebuild.
- `2026-04-12`: Documentation was corrected to state the real runtime scope: `maxVisibleInstanceCapacity` is per container, not a global scene budget. Large forests may be split across multiple containers to avoid one-container overflow, but total scene memory and visible capacity then scale with the number of visible containers because there is still no global coordinator.
- `2026-04-12`: Urgent dense-forest redesign was documented in `DetailedDocs/urgentRedesign.md`. Current shipped overflow policy is still slot-order-based; the approved direction is guaranteed tree presence first, then nearest-tree and nearest-branch promotion buckets.
- Current production gap 1: hierarchical wind is still not implemented.
- Current production gap 2: runtime material ownership is still hard-coded through `VegetationIndirectMaterialFactory`, which rebuilds runtime materials from package shader names and copies only a narrow property subset.
- Current production gap 3: canopy-shell generation still does not support the intended `GPUVoxelizer` path from quad and alpha-masked branch inputs.
- Current package-consumer risk: project-local custom materials and masked-quad foliage inputs are not first-class yet because runtime rendering and bake tooling still expose a narrow source contract.

## Immediate Tasks

### Urgent runtime redesign

- Replace slot-order overflow with the `urgentRedesign.md` pipeline: guaranteed tree presence proxy first, then nearest-tree promotion.
- Add explicit per-frame tree acceptance records before per-slot packing.
- Add dense-forest validation for one container with around `6000` tightly packed trees and camera inside the forest.
- Add overflow telemetry: visible trees, proxy trees, promoted trees, rejected promotions, requested detail, accepted detail, and per-bucket usage.

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
- HiZ depth pyramid occlusion (or other deep occlusion in classification)

## Deferred

- Feature-grade placement tools
- Optional texture/quad-card branch baking follow-up
