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

1. Introduce explicit prepared-view handles.
2. Stop dispatching branch count/emit against `maxVisibleInstanceCapacity`.
3. Enforce baseline-fit failure instead of silently dropping visible non-far trees.
4. Keep shadow cheap by default.

## Authority

- runtime: [VegetationRuntimeArchitecture.md](VegetationRuntimeArchitecture.md)
- shipped baseline: [Milestone1.md](Milestone1.md)
