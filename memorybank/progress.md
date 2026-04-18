# Progress

Purpose: current milestone, current blockers, next tasks. Nothing else.

## Current Milestone

- Milestone: `Milestone 2 - Production Runtime Cleanup`
- Scope authority: [Milestone2.md](../DetailedDocs/Milestone2.md)
- Runtime authority: [VegetationRuntimeArchitecture.md](../DetailedDocs/VegetationRuntimeArchitecture.md)
  now includes full ASCII bake, registration, color/depth, and shadow pipelines with payload ownership and resident-memory surfaces
- Finished baseline: [Milestone1.md](../DetailedDocs/Milestone1.md)

## Current Blockers

- runtime still uses shared camera/shadow submission ownership
- runtime still aliases one budget across memory, work items, and work cost
- branch count/emit still dispatches against configured capacity instead of actual generated work
- registered draw slots are still the live submission surface

## Next Tasks

Goal: split prepared-view ownership out of AuthoringContainerRuntime and stop sharing one mutable renderer-bound frame across camera and shadow.

1. Introduce explicit prepared-view handles and remove renderer-global bound-frame ownership.
2. Split runtime budgets into visible instances, expanded work items, approximate work units, and registered draw-slot cap.
3. Dispatch branch count/emit from actual generated work count.
4. Enforce baseline-fit failure for visible non-far color trees.
5. Run dense-forest and shadow validation after the ownership split lands.

Target Result: simplified memory footprint for runtime  path, true separation of draw calls for shadow path and depth/main color path and production ready support for a container with 10_000 trees.
