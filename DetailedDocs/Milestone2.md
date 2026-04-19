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
- Completed: visible non-far color `TreeL3` baseline now fails explicitly instead of silently dropping trees.
- Completed: prepared-frame telemetry now reports actual visible instances, generated branch work, and budget-cap-hit flags through latest async readback snapshots.
- Completed: submission now uses the live active-slot surface from latest completed emitted-slot readback, with registered-slot fallback only during async warm-up.
- Completed: enabled shadow promotion is now near-only; trees in the `L2` band stay at `TreeL3`, while only `L1/L0` bands can expand in shadows.
1. Validate and tune the cheap-shadow defaults against the split-budget path.
2. Reduce duplicated camera/shadow GPU residency instead of keeping two full pipelines per active container.
3. Remove slot-order bias from visible-instance clamping.

## Authority

- runtime: [VegetationRuntimeArchitecture.md](VegetationRuntimeArchitecture.md)
- shipped baseline: [Milestone1.md](Milestone1.md)
