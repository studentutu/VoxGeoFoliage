# Progress

Purpose: current milestone, current blockers, next tasks. Nothing else.

## Current Milestone

- Milestone: `Milestone 2 - Production Runtime Cleanup`
- Scope authority: [Milestone2.md](../DetailedDocs/Milestone2.md)
- Runtime authority: [VegetationRuntimeArchitecture.md](../DetailedDocs/VegetationRuntimeArchitecture.md)
  now includes full ASCII bake, registration, color/depth, and shadow pipelines with payload ownership and resident-memory surfaces
- Finished baseline: [Milestone1.md](../DetailedDocs/Milestone1.md)

## Current Blockers

- visible non-far color trees can still fail baseline acceptance silently
- registered draw slots are still the live submission surface
- dense-forest and shadow validation are still pending on the split-budget path

## Next Tasks

Goal: finish the post-ownership-split runtime cleanup now that split budgets and actual-work dispatch match the prepared-view design.

1. Enforce baseline-fit failure for visible non-far color trees.
2. Replace registered-slot submission with the live active-slot surface.
3. Run dense-forest and shadow validation on the split-budget path.

Target Result: simplified memory footprint for runtime  path, true separation of draw calls for shadow path and depth/main color path and production ready support for a container with 10_000 trees, that has near prioritization and actual hard budgeting.
