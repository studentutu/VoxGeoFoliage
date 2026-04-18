# Milestone 2

Status: active

## Goal

Turn the shipped baseline into a production-usable package.

## In Scope

- split runtime ownership into persistent container state and pooled per-view prepared state
- fix camera/shadow ownership instead of keeping duplicated full pipelines
- split budgets:
  - visible instances
  - expanded branch work items
  - approximate work units
  - registered draw-slot cap
- custom-material compatibility contract
- wind
- masked-quad `GPUVoxelizer` bake path

## Out Of Scope

- cross-container prioritization
- occlusion overhaul
- new placement systems

## Immediate Runtime Work

- Completed: explicit prepared-view handles landed and camera/shadow no longer share one renderer-global bound frame.
- Completed: runtime budgets are now split into visible instances, expanded branch work items, approximate work units, and registered draw-slot cap.
- Completed: branch count/emit now dispatches from actual generated expanded-branch work via GPU-built indirect dispatch args.
1. Enforce baseline-fit failure instead of silently dropping visible non-far trees.
2. Replace registered-slot submission with the live active-slot surface.
3. Validate the cheap-shadow defaults against the split-budget path.

## Authority

- runtime: [VegetationRuntimeArchitecture.md](VegetationRuntimeArchitecture.md)
- shipped baseline: [Milestone1.md](Milestone1.md)
