# Progress

Purpose: current milestone, current blockers, next tasks. Nothing else.

## Current Milestone

- Milestone: `Milestone 2 - Production Runtime Cleanup`
- Scope authority: [Milestone2.md](../DetailedDocs/Milestone2.md)
- Runtime authority: [VegetationRuntimeArchitecture.md](../DetailedDocs/VegetationRuntimeArchitecture.md)
  now includes full ASCII bake, registration, color/depth, and shadow pipelines with payload ownership and resident-memory surfaces
- Finished baseline: [Milestone1.md](../DetailedDocs/Milestone1.md)
- Latest completed cleanup: branch prototype authoring now persists only the split-tier runtime mesh chain (`branchL1/2/3CanopyMesh` + `branchL1/2/3WoodMesh`); obsolete shell-node authoring/runtime contracts and sample per-node shell assets were removed.

## Current Blockers

- shadow currently reuses the same default budget shape as color, so explicit-frustum/shadow preparation doubles fixed residency without proving it needs to
- camera and explicit-frustum preparation still keep two full GPU pipelines per active container instead of the target pooled prepared-view residency
- visible-instance clamping is still slot-order biased
- active-slot submission and actual-usage telemetry are latest async readback snapshots, so first prepared frames can still fall back to registered slots and reported counts can lag the frame being rendered
- dense-forest and shadow validation are still pending on the split-budget path

## Next Tasks

Goal: finish the post-ownership-split runtime cleanup now that split budgets and actual-work dispatch match the prepared-view design.

1. Tune shadow budgets separately from color now that actual usage telemetry exists.
2. Reduce duplicated camera/shadow GPU residency toward the pooled prepared-view ownership target.
3. Remove slot-order bias from visible-instance clamping.
4. Run dense-forest and shadow validation on the split-budget path, including async active-slot warm-up behavior.

Target Result: simplified memory footprint for runtime  path, true separation of draw calls for shadow path and depth/main color path and production ready support for a container with 10_000 trees, that has near prioritization and actual hard budgeting.
