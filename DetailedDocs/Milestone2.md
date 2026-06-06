# Milestone 2

Status: active. The compiled renderer is in production cleanup with a single RenderGraph runtime path.

## Goal

Turn the shipped baseline into a production-usable opaque foliage package without reintroducing parallel renderer paths.

## Current Production Baseline

The renderer is one compiled packet path:

```text
VegetationTreeAuthoring
-> FoliageAssemblyAsset + FoliagePageAsset[]
-> VegetationRuntimeContainer or closed SubScene provider
-> VegetationRenderWorld
-> RenderGraph-owned jobified preparation
   -> page/cell broad phase
   -> packet budgets
   -> compaction and indirect args generation
   -> completed-frame slot handoff
-> URP RenderGraph compute preparation contract
   -> grouped instance/args buffer upload without job completion
-> RenderGraph grouped-indirect depth/color/shadow passes
-> shader wind
-> grouped indirect draw commands
```

The C# BRG backend was deleted. The active runtime should not branch by graphics API and should not maintain a fallback renderer.

## Completed

1. Explicit prepared-view ownership landed; camera and shadow passes no longer share one renderer-global mutable frame.
2. Global color and shadow packet budgets are owned by `VegetationRenderWorld`.
3. Compiler contract landed: `FoliageAssemblyAsset`, `FoliagePageAsset`, `FoliageRepresentationPacket`, `FoliageAssetGroup`, build reports, and `VegetationRuntimeContainer` inspector compilation.
4. Compiler metadata landed: `PageHLOD`/`CellHLOD` always-resident instance packets that collapse trees to baked impostor meshes, `TreeL0/L1/L2` near-detail packets, static wind metadata, compiled shadow packet modes, shadow bounds, compile-required opaque input validation, resident mesh payload estimates, and near-detail byte caps.
5. Classic-scene runtime registration landed. `VegetationRuntimeContainer` registers generated assembly/pages with `VegetationRenderWorld`; `VegetationRendererFeature` consumes that world directly.
6. Public shadow settings are `VegetationShadowMode.Off` and `VegetationShadowMode.CheapTree`.
7. Closed `SubScene` bootstrap bakes compiled assembly/page references and registers/unregisters providers with `VegetationRenderWorld`.
8. The retired tree-first runtime family, old runtime tests, independent proxy shadow authoring surfaces, demo compute surface, and C# BRG backend were physically deleted.
9. Sample/demo authoring assets were cut over to baked impostor HLOD inputs and generated mesh settings.
10. Near-detail packet residency is budgeted in `VegetationRenderWorld`: visible near cells request cell-level residency under resident/upload byte budgets and fall back to HLOD when blocked.
11. Generated page/cell aggregate HLOD mesh assets were cut out; HLOD now uses baked per-tree impostor meshes and recompilation deletes stale generated HLOD mesh assets for the container.
12. Grouped indirect instance lookup is backend-stable: indirect args use zero `startInstance`, and `VegetationRenderWorld` binds `_VegetationInstanceDataBaseOffset` per group.
13. RenderGraph vertical slice landed: `VegetationRendererFeature` imports prepared buffers, records `VegetationRenderGraphPreparationContract`, and makes depth/color/shadow raster passes consume the compute contract.
14. Depth/color RenderGraph passes no longer call vegetation prepare inside render functions and no longer opt into global-state mutation.
15. Cull/select/budget/compaction/indirect-args generation moved into a scheduled preparation job; RenderGraph now consumes only already-completed preparation slots and uploads compacted instance/args buffers without completing jobs during graph execution.
16. Shadow compaction now writes per-cascade grouped-indirect args and instance spans, instead of submitting one whole group-wide shadow args range into every matching cascade.
17. Runtime `CheapTree` shadows now admit only compiled cheap/HLOD shadow packets; `SameAsColor` packets are rejected so shadows cannot replay near-detail color geometry.

## Current Blockers

1. Runtime LOD selection is still distance-band based inside `VegetationRenderWorld`; screen-error and hysteresis remain production hardening.
2. Procedural placement outputs do not yet compile directly into page providers.
3. Externalized async near-detail payload providers for disk/Addressables-backed pages are not implemented.
4. Shadow submit still mutates URP-compatible global cascade state while appending into the main shadow atlas.
5. Dense-scene, mobile, VR, shadow, and wind validation are still pending on the compiled render-world path.
6. The current preparation implementation is one scheduled job plus command-buffer buffer upload and one-frame completed-frame latency; replacing it with true GPU compute kernels is still pending after packet renderer validation.

## Next Tasks

1. Replace the scheduled preparation job with GPU compute kernels for cull, admission, compaction, and indirect-args writes after validating the packet renderer contract.
2. Replace distance-only packet selection with screen-error plus hysteresis and budget pressure.
3. Add procedural placement output as compiled page providers.
4. Add externalized async near-detail payload providers when pages move out of direct ScriptableObject references.
5. Run production verification without HZB across 100k loaded instances, 1M streamed instances, mobile profile, VR stereo profile, shadows, and wind.

## Authority

- runtime: [VegetationRuntimeArchitecture.md](VegetationRuntimeArchitecture.md)
- redesign: [VegetationGenerationalRedesign.md](VegetationGenerationalRedesign.md)
- shipped baseline: [Milestone1.md](Milestone1.md)
