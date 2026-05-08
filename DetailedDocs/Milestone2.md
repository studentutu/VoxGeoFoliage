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
- production shadow target:
  - `ShadowMode.Off`
  - `ShadowMode.CheapTree`
  - near active `L0/L1` shadows use the same accepted color geometry
  - farther `L2/TreeL3` shadows use cheap tree-only casters
  - independent enlarged shadow-proxy LODs are not production behavior

## Out Of Scope

- cross-container prioritization
- occlusion overhaul
- new placement systems
- additional-light vegetation shadow atlases
- fully correct offscreen vegetation casters

## Immediate Runtime Work

- Completed: explicit prepared-view handles landed and camera/shadow no longer share one renderer-global bound frame.
- Completed: runtime budgets are now split into visible instances, expanded branch work items, approximate work units, and registered draw-slot cap.
- Completed: branch count/emit now dispatches from actual generated expanded-branch work via GPU-built indirect dispatch args.
- Completed: visible non-far color `TreeL3` baseline now fails explicitly instead of silently dropping trees.
- Completed: prepared-frame telemetry now reports actual visible instances, generated branch work, and budget-cap-hit flags through latest async readback snapshots.
- Completed: submission now uses the live active-slot surface from latest completed emitted-slot readback, with registered-slot fallback only during async warm-up.
- Superseded: enabled legacy shadow promotion is near-only, but the target contract is now `ShadowMode.CheapTree`: near active `L0/L1` uses same-as-color shadow casters, farther `L2/TreeL3` uses cheap tree-only casters, and independent `ShadowProxyL0/L1` production promotion is removed.
- Completed: branch authoring/runtime cleanup now persists only `branchL1/2/3CanopyMesh` plus `branchL1/2/3WoodMesh`; obsolete shell-node authoring/runtime contracts and sample per-node shell assets were removed.
1. Replace `RenderMainLightShadows` / `AllowExpandedTreePromotionInShadows` with `ShadowMode.Off` and `ShadowMode.CheapTree`.
2. Implement `CheapTree` tier behavior: same-as-color near active `L0/L1`, cheap tree-only farther `L2/TreeL3`, no impostor cast shadow by default.
3. Remove production use of independent `ShadowProxyL0/L1` promotion and add validation that cheap tree shadow casters do not exceed the visible tier silhouette or `TreeL3` cost without benchmark approval.
4. Tune shadow budgets separately from color against dense-forest scenes.
5. Reduce duplicated camera/shadow GPU residency instead of keeping two full pipelines per active container.
6. Remove slot-order bias from visible-instance clamping.

## Authority

- runtime: [VegetationRuntimeArchitecture.md](VegetationRuntimeArchitecture.md)
- shipped baseline: [Milestone1.md](Milestone1.md)
