# Progress

Purpose: current milestone, current blockers, next tasks. Nothing else.

## Current Milestone

- Milestone: `Milestone 2 - Production Runtime Cleanup`
- Scope authority: [Milestone2.md](../DetailedDocs/Milestone2.md)
- Runtime authority: [VegetationRuntimeArchitecture.md](../DetailedDocs/VegetationRuntimeArchitecture.md)
  now includes full ASCII bake, registration, color/depth, and shadow pipelines with payload ownership and resident-memory surfaces
- Strategic redesign authority: [VegetationGenerationalRedesign.md](../DetailedDocs/VegetationGenerationalRedesign.md)
  defines the current non-HZB production baseline: compiled page assets -> global render world -> BRG batches on supported raw-buffer APIs or RenderGraph grouped-indirect on Direct3D12/fallback setup -> page/cell culling -> packet selection/budgets -> shader wind -> draw commands. Target scale is 100k to 1M loaded instances with streaming. No parallel renderer, no maintained tree-first bridge, no telemetry-gated CPU submission list, and no HZB before packet renderer production verification. Branch placement matrices, bounds, packet costs, shadow mapping, wind metadata, command bounds, and page/cell split decisions belong to the editor compiler.
- Finished baseline: [Milestone1.md](../DetailedDocs/Milestone1.md)
- Current runtime baseline: classic-scene and closed `SubScene` providers register generated `FoliageAssemblyAsset` / `FoliagePageAsset[]` with the global `VegetationRenderWorld`. Supported raw-buffer APIs use BRG batches with batch-owned matrix/tint/wind metadata; Direct3D12 uses RenderGraph grouped-indirect color/depth/shadow. Fallback shaders declare `_VegetationInstanceData` only for procedural non-DOTS variants. Public shadow settings are `Off` and `CheapTree`; HLOD collapses to the baked tree impostor mesh, not branch/trunk L3 replay.

## Current Blockers

- runtime LOD selection is still distance-band based inside `VegetationRenderWorld`; screen-error/hysteresis remains future production hardening
- externalized async near-detail payload providers for disk/Addressables-backed pages are still pending; current residency is budgeted inside the render world over compiled page assets
- dense-forest, mobile, VR, and large shadow validation are still pending on the compiled render-world path
- performance regression root cause is identified: old RenderGraph execution repacked and uploaded selected instances on the main thread. BRG fixes that on supported raw-buffer APIs by using batch-owned instance buffers and renderer-owned culling output. Direct3D12 remains on grouped-indirect RenderGraph because Unity `6000.3.15f1` crashes in native `InjectShadowDrawCommands` when custom vegetation BRG is registered.

## Next Tasks

Goal: harden the production packet renderer and improve D3D12 grouped-indirect RenderGraph performance without reintroducing retired renderer paths.

1. Optimize Direct3D12 RenderGraph grouped-indirect performance by reducing prepare/upload work, then Burst/jobify BRG culling and draw-command generation for supported BRG APIs.
2. Replace distance-only packet selection with screen-error plus hysteresis and budget pressure.
3. Add procedural placement output as compiled page providers.
4. Add externalized async near-detail payload providers when pages move out of direct ScriptableObject references.
5. Run production verification without HZB across 100k loaded instances, 1M streamed instances, mobile profile, VR stereo profile, shadows, and wind.

Target Result: one production packet renderer, no maintained bridge, no runtime branch-work generator, no active-slot readback submission, and production-ready opaque vegetation with global budgets, shadows, wind, and streaming.
