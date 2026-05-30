# Milestone 2

Status: active. The compiled renderer is complete; this milestone now tracks production hardening.

## Goal

Turn the shipped baseline into a production-usable opaque foliage package.

## Current Production Baseline

The renderer is now one compiled packet path:

```text
VegetationTreeAuthoring
-> FoliageAssemblyAsset + FoliagePageAsset[]
-> VegetationRuntimeContainer or closed SubScene provider
-> VegetationRenderWorld
-> BatchRendererGroup batches by compiled FoliageAssetGroup on supported non-D3D12 APIs
-> RenderGraph grouped-indirect passes on Direct3D12 and unsupported/faulted BRG APIs
-> page/cell broad phase
-> packet budgets
-> shader wind
-> BRG or grouped indirect draw commands
```

Direct3D12 now runs the RenderGraph grouped-indirect backend. D3D12 diagnostics proved the Unity `6000.3.15f1` native shadow extraction crash happens immediately after custom vegetation BRG batch registration, before vegetation BRG culling output, even with BRG light views and registered shadow caster passes disabled.

The renderer does not maintain a parallel tree-first runtime path.

## Completed

1. Explicit prepared-view ownership landed; camera and shadow passes no longer share one renderer-global mutable frame.
2. Global color and shadow packet budgets are owned by `VegetationRenderWorld`.
3. Compiler contract landed: `FoliageAssemblyAsset`, `FoliagePageAsset`, `FoliageRepresentationPacket`, `FoliageAssetGroup`, build reports, and `VegetationRuntimeContainer` inspector compilation.
4. Compiler metadata landed: `PageHLOD`/`CellHLOD` always-resident instance packets that collapse trees to baked impostor meshes, `TreeL0/L1/L2` near-detail packets, static wind metadata, compiled shadow packet modes, shadow bounds, compile-required opaque input validation, resident mesh payload estimates, and near-detail byte caps.
5. Classic-scene runtime registration landed. `VegetationRuntimeContainer` registers generated assembly/pages with `VegetationRenderWorld`; `VegetationRendererFeature` consumes that world directly.
6. Public shadow settings are `VegetationShadowMode.Off` and `VegetationShadowMode.CheapTree`.
7. Closed `SubScene` bootstrap bakes compiled assembly/page references and registers/unregisters providers with `VegetationRenderWorld`.
8. The retired tree-first runtime family, old runtime tests, independent proxy shadow authoring surfaces, and demo compute surface were physically deleted.
9. Sample/demo authoring assets were cut over to baked impostor HLOD inputs and generated mesh settings.
10. Authoring preview and bake controls now expose only the active branch/trunk tier workflow.
11. Near-detail packet residency is budgeted in `VegetationRenderWorld`: visible near cells request cell-level residency under resident/upload byte budgets and fall back to HLOD when blocked.
12. Generated page/cell aggregate HLOD mesh assets were cut out; HLOD now uses baked per-tree impostor meshes and recompilation deletes stale generated HLOD mesh assets for the container.
13. Grouped indirect instance lookup is backend-stable: indirect args use zero `startInstance`, and `VegetationRenderWorld` binds `_VegetationInstanceDataBaseOffset` per group so DirectX does not read trunk payload records for canopy draws.
14. BRG production slice landed on supported raw-buffer BRG APIs: `VegetationRenderWorld` creates one `BatchRendererGroup` batch per compiled `FoliageAssetGroup`, uploads batch-owned matrix/tint/wind metadata once per graph rebuild, emits compacted visible instance indices from the BRG culling callback, and `VegetationRendererFeature` skips custom RenderGraph camera/depth/color passes when BRG initializes. Direct3D12 is explicitly routed to the RenderGraph grouped-indirect backend because Unity native shadow extraction crashes from the presence of custom vegetation BRG batches.

## Current Blockers

1. Runtime LOD selection is still distance-band based inside `VegetationRenderWorld`; screen-error and hysteresis remain production hardening.
2. Procedural placement outputs do not yet compile directly into page providers.
3. Externalized async near-detail payload providers for disk/Addressables-backed pages are not implemented.
4. BRG culling/draw-command generation is not Burst/jobified yet; the RenderGraph stall is removed only on supported raw-buffer BRG APIs. Direct3D12 performance still depends on optimizing the RenderGraph grouped-indirect path without registering custom vegetation BRG batches.
5. Dense-scene, mobile, VR, shadow, and wind validation are still pending on the compiled render-world path.

## Next Tasks

1. Optimize the Direct3D12 RenderGraph grouped-indirect path by reducing prepare/upload work, then separately Burst/jobify BRG culling and draw-command generation for supported BRG APIs.
2. Replace distance-only packet selection with screen-error plus hysteresis and budget pressure.
3. Add procedural placement output as compiled page providers.
4. Add externalized async near-detail payload providers when pages move out of direct ScriptableObject references.
5. Run production verification without HZB across 100k loaded instances, 1M streamed instances, mobile profile, VR stereo profile, shadows, and wind.

## Authority

- runtime: [VegetationRuntimeArchitecture.md](VegetationRuntimeArchitecture.md)
- redesign: [VegetationGenerationalRedesign.md](VegetationGenerationalRedesign.md)
- shipped baseline: [Milestone1.md](Milestone1.md)
