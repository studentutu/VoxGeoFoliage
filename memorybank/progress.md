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
- `2026-04-12`: Urgent dense-forest redesign was tightened back to the real blocker in `DetailedDocs/urgentRedesign.md`. Current shipped overflow policy is still slot-order-based. The approved direction is guaranteed tree presence first, then nearest-tree and nearest-branch promotion buckets. Occlusion was explicitly removed from the urgent path because it does not fix the near-tree capacity bug.
- `2026-04-12`: Runtime documentation was corrected to match the actual shipped hierarchy state. Trees are still flat branch spans at runtime, and branch-shell BFS metadata is not yet used for true child-link traversal in the shader. The redesign now explicitly calls for `PresenceProxyOnly` / `PromotedExpanded` tree states plus promoted-tree compaction before branch kernels.
- `2026-04-12`: `DetailedDocs/urgentRedesign.md` now stages targeted branch-shell BFS correctly: only after tree-level prioritization and promoted-tree compaction, and only for promoted shell branches in `L1/L2/L3`. This is now the approved path for making the existing branch-shell BFS metadata pay for itself.
- `2026-04-12`: Documentation clarity pass landed. `Packages/com.voxgeofol.vegetation/README.md` and `DetailedDocs/urgentRedesign.md` now define the ambiguous runtime terms explicitly and include the full current lifecycle from `VegetationRuntimeContainer.registeredAuthorings` through runtime registry flattening, GPU classification/emission, draw slots, and final URP indirect submissions.
- `2026-04-12`: Ownership-model clarification landed. Current authority docs now state that `SceneBranches[]` is a bounded static registration snapshot, not a final submission owner or exponential frame-growth structure. They also split static registry owners from per-frame worklists and rename the shipped hierarchy format explicitly to branch-shell BFS instead of tree BFS.
- `2026-04-12`: Current runtime-review telemetry landed behind `VegetationFoliageFeatureSettings.EnableDiagnostics`. `AuthoringContainerRuntime` now logs per-container `SceneBranches[]`, `BranchPrototypes[]`, and `ShellNodesL1/L2/L3[]` counts plus exact allocated GPU bytes for branch, branch-decision, prototype, and shell-node buffers.
- `2026-04-12`: Current runtime-review telemetry was extended for architecture review. `AuthoringContainerRuntime` now also logs `TreeBlueprints[]`, `visibleInstanceCapacityBytes`, and one-shot prepared-frame readback totals for `nonZeroEmittedSlots`, `emittedVisibleInstances`, and `emittedVisibleInstanceBytes`, all still behind `VegetationFoliageFeatureSettings.EnableDiagnostics`.
- `2026-04-12`: `DetailedDocs/urgentRedesign.md` now records the measured dense repeated-branch failure shape explicitly: one `6087`-tree container produced `316524` scene branches, about `129.2 MiB` of branch-review buffers, `768` draw slots, and a suspicious `759/1/1` shell-tier shape. The current conclusion is now fixed in docs: `SceneBranches[]` is bounded and owned, but too heavy to trust as the long-term dense-forest static owner in this repeated-branch case.
- `2026-04-12`: Closed-`SubScene` runtime registration support landed. `AuthoringContainerRuntime` is now the single runtime owner, `VegetationRuntimeContainer` is only the classic-scene lifecycle provider, `VegetationTreeAuthoringRuntime` is the shared registration contract, `VegetationRendererFeature` consumes active runtime owners from `VegetationActiveAuthoringContainerRuntimes`, and `Vegetation.SubScene` bootstraps the same runtime owner from baked `SubSceneAuthoring` data.
- `2026-04-12`: Unity full compile now passes after the `Vegetation.SubScene` asmdef was added and wired to `Unity.Entities.Hybrid` plus `Unity.Mathematics`.
- Current production gap 1: hierarchical wind is still not implemented.
- Current production gap 2: runtime material ownership is still hard-coded through `VegetationIndirectMaterialFactory`, which rebuilds runtime materials from package shader names and copies only a narrow property subset.
- Current production gap 3: canopy-shell generation still does not support the intended `GPUVoxelizer` path from quad and alpha-masked branch inputs.
- Current package-consumer risk: project-local custom materials and masked-quad foliage inputs are not first-class yet because runtime rendering and bake tooling still expose a narrow source contract.

## Immediate Tasks

### Urgent runtime redesign

- Replace slot-order overflow with the `urgentRedesign.md` pipeline: guaranteed tree presence proxy first, then nearest-tree promotion.
- Add explicit per-frame tree acceptance records before per-slot packing.
- Add `PresenceProxyOnly` / `PromotedExpanded` tree work states and compact promoted trees before branch kernels.
- Replace shell-tier flat scans with targeted branch-shell BFS frontier traversal for promoted shell branches only.
- Add dense-forest validation for one container with around `6000` tightly packed trees and camera inside the forest.
- Re-run dense-forest diagnostics with the new `blueprints`, `nonZeroEmittedSlots`, and emitted visible-instance byte fields, then review whether the observed `759/1/1` shell-tier shape is intentional or a bake/runtime defect.
- Add overflow telemetry last: visible trees, proxy trees, promoted trees, rejected promotions, requested detail, accepted detail, non-zero accepted slots, and per-bucket usage.

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
