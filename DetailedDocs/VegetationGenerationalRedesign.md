# Vegetation Generational Redesign

Purpose: redesign authority for the compiled foliage runtime.

Status: active baseline reset. The runtime has one compiled packet renderer with opaque geometry, page/cell culling, global budgets, shadow packet selection, shader wind, URP RenderGraph grouped-indirect submission, classic-scene providers, and closed SubScene providers. The C# BRG backend was deleted and must not return as a fallback or API-specific path.

## Target

The project goal is fast opaque foliage:

1. No transparency, alpha clip, or masked runtime materials.
2. Authoring remains reusable branch/tree data.
3. Runtime consumes compiled page assets, not live branch authoring.
4. LOD, shadows, and wind are selected from compiled packet metadata.
5. Budgets are global to the render world, not per container.
6. Shadow casters cannot use a richer or larger independent representation than the selected packet policy allows.
7. Static bounds, packet ranges, pass ids, costs, wind metadata, and HLOD packet instances are editor-compiled.
8. Runtime hot paths stay free of synchronous GPU readback and per-frame branch expansion.

## Production Architecture

```text
VegetationTreeAuthoring
-> FoliageCompiledAssetCompiler
-> FoliageAssemblyAsset + FoliagePageAsset[]
-> VegetationRuntimeContainer or SubScene compiled provider
-> VegetationRenderWorld
-> RenderGraph-owned jobified preparation
   -> page/cell broad phase
   -> packet selection under color/shadow budgets
   -> compaction and indirect args generation
   -> completed-frame slot handoff
-> URP RenderGraph compute preparation contract
   -> grouped instance/args buffer upload without job completion
-> RenderGraph grouped-indirect depth/color/shadow passes
-> shader wind
-> grouped indirect draw command output
```

Ownership is intentionally narrow:

| Surface | Owner |
| --- | --- |
| Authoring references | `VegetationRuntimeContainer` |
| Compiled pages | `FoliagePageAsset` |
| Shared asset groups | `FoliageAssemblyAsset` |
| Culling, budgets, frame selection, per-cascade shadow args, grouped-indirect buffers/submission | `VegetationRenderWorld` and its scheduled preparation job |
| URP RenderGraph scheduling, buffer import, completed-frame upload, shadow atlas append | `VegetationRendererFeature` and `VegetationRenderGraphPreparationContract` |

## Performance Regression Root Cause

The broken shape was not API-specific. Vegetation prepare/upload was executed from vegetation render functions and therefore forced URP RenderGraph execution to synchronize around feature-owned main-thread work. That path repacked selected instances and uploaded buffers while the render graph should have been executing graph-owned work.

The RenderGraph vertical slices fix the contract:

1. BRG C# runtime code is deleted.
2. `VegetationRendererFeature` has one RenderGraph path.
3. Camera and shadow preparation happen before raster render functions.
4. Instance and args buffers are imported into RenderGraph.
5. `VegetationRenderGraphPreparationContract` records a compute pass that raster passes consume.
6. Color/depth raster passes no longer request RenderGraph global-state mutation.
7. Cull/select/budget/compaction/args generation runs in a scheduled preparation job.
8. The RenderGraph compute pass uploads already-completed compacted buffers and never completes the scheduled preparation job during graph execution.

This is still not the final 10x performance implementation. The current slice removes the synchronized main/render-thread prepare/upload path, but it is one scheduled job plus command-buffer upload and one-frame completed-frame latency. The next generation is to move broad phase, packet admission, compaction, and args writes into compute kernels after the packet renderer contract is validated.

## Runtime Packets

The compiler emits only these active representation kinds:

1. `TreeL0`
2. `TreeL1`
3. `TreeL2`
4. `CellHLOD`
5. `PageHLOD`

`TreeL0` uses source branch placement at near distance. `TreeL1` and `TreeL2` use generated split canopy/wood tier meshes. `CellHLOD` and `PageHLOD` collapse each tree to its baked `impostorMesh` with `impostorMaterial` as opaque instanced far packets. They must not replay branch/trunk L3 packets and must not generate unique aggregate meshes per page or cell.

HLOD packets are always resident. Near-detail packets are marked as near-detail residency and are selected only inside the active near range and budget envelope. `VegetationRenderWorld` request-loads near-detail payloads at cell granularity under resident/upload byte budgets and falls back to HLOD when those budgets block residency. Visible trees must degrade, not disappear.

## Shadows

Public shadow modes are:

1. `Off`
2. `CheapTree`

`CheapTree` submits compiled cheap/HLOD shadow packet metadata under a separate shadow work budget. Runtime shadow selection must reject `SameAsColor` packets because replaying color geometry is not a cheap shadow path. The shadow pass must not invent a separate higher-detail or enlarged caster path.

Current limitations:

1. Main directional light only.
2. Additional-light vegetation shadow atlases are not implemented.
3. Shadow submit still mutates URP-compatible global cascade state while appending into the main shadow atlas.
4. Offscreen caster policy is conservative and needs dense-scene validation.

## Wind

Wind is shader-side. The compiler writes static `FoliageWindMetadata` per instance: phase, trunk bend weight, branch flutter weight, and anchor height. Runtime updates global wind and leaf-flutter constants from `VegetationFoliageFeatureSettings` and binds them for grouped draws. Runtime must not rebuild wind metadata per frame.

## Culling And Budgets

The current baseline uses page/cell culling only:

1. camera path: scheduled page/cell frustum tests in the RenderGraph preparation job
2. shadow path: batched cascade frustum planes

Rejected baseline choices:

1. No per-tree sphere feed into runtime broad phase.
2. No HZB dependency before the packet renderer is production-validated.
3. No per-container budgets.
4. No CPU readback driven submission list.
5. No BRG fallback backend.

Budget owners:

1. `ColorWorkBudget`
2. `ShadowWorkBudget`
3. `MaxVisiblePacketInstances`
4. grouped command capacity
5. `NearDetailResidentByteBudget`
6. `NearDetailUploadByteBudget`

Dense scenes must admit visible cells from nearest to farthest relative to the active camera. A near cell tries its best distance tier first, then progressively cheaper near-detail tiers, and only then falls back to `CellHLOD` / `PageHLOD` baked impostor packets under budget pressure.

## Deleted Surfaces

Deleted categories:

1. retired tree-first runtime owner, registry, and runtime tests
2. retired per-container GPU decision path
3. retired active runtime discovery
4. retired independent proxy shadow authoring path
5. retired demo GPU voxel compute surface
6. retired far/proxy preview and inspector controls
7. retired legacy packet enum outputs
8. C# BRG runtime backend and culling callbacks

There is no compatibility toggle and no bridge renderer to maintain.

## Current Verification Contract

The compiled renderer baseline is considered intact only when all of these stay true:

1. active package runtime has no `BatchRendererGroup` / BRG backend code
2. active runtime has exactly one RenderGraph grouped-indirect backend
3. raster passes consume the compute preparation contract before drawing
4. color/depth raster passes do not use RenderGraph global-state mutation
5. compiler tests prove packet ranges are contiguous and legacy packet kinds are absent
6. compiler tests prove no generated aggregate HLOD meshes are emitted
7. authoring validation tests prove baked impostor inputs and current LOD order are enforced
8. Unity full compile is clean after file additions/deletions

## Remaining Production Work

Required next work:

1. Replace the current scheduled preparation job with GPU compute kernels for broad phase, packet admission, compaction, and indirect-args writes.
2. Replace distance-only packet selection with screen-error plus hysteresis and budget pressure.
3. Add procedural placement output as compiled page providers.
4. Add externalized async near-detail payload providers for disk/Addressables-backed pages.
5. Validate dense forest at 100k loaded instances and 1M streamed instances.
6. Validate mobile and VR profiles.
7. Stress-test main-light shadows with wind enabled.
8. Add explicit compatible-material validation for project-local shaders.

Do not spend effort restoring retired renderer surfaces. The only acceptable forward path is strengthening the single RenderGraph compiled packet renderer.
