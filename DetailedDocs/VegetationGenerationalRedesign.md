# Vegetation Generational Redesign

Purpose: current authority for the foliage redesign after the hard cutover.

Status: completed for the requested cutover scope. The active path is a single compiled packet renderer with opaque geometry, page/cell culling, global budgets, shadow packet selection, shader wind, BatchRendererGroup submission on supported raw-buffer graphics APIs, RenderGraph grouped-indirect submission on Direct3D12 and unsupported/faulted BRG setup, classic-scene providers, and closed SubScene providers. The retired tree-first renderer and its demo compute surface are no longer active package code.

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
-> BatchRendererGroup batches by compiled FoliageAssetGroup on supported non-D3D12 APIs
-> RenderGraph grouped-indirect passes on Direct3D12 and unsupported/faulted BRG APIs
-> page/cell split-frustum broad phase
-> packet selection under color/shadow budgets
-> shader wind binding
-> BRG or grouped indirect draw command output
```

BRG submission uses one batch per compiled `FoliageAssetGroup`. Each batch owns its own `GraphicsBuffer` containing built-in per-instance matrices plus `_VegetationPackedLeafTint` and `_VegetationWind`. The grouped-indirect RenderGraph path remains fallback-only for unsupported or faulted BRG initialization and keeps zero `startInstance` plus `_VegetationInstanceDataBaseOffset` for backend-stable lookup.

Direct3D12 on Unity `6000.3.15f1` is on the RenderGraph grouped-indirect backend. The previous custom BRG attempts proved the failing path: Unity enters native `InjectShadowDrawCommands` immediately after vegetation BRG batch registration, before our culling callback publishes any draw-command IDs, and still crashes when BRG light views and registered `ShadowCaster` passes are disabled. The supported D3D12 path is therefore RenderGraph grouped-indirect color/depth/shadow submission. Performance work should reduce prepare/upload cost in that path instead of adding more D3D12 BRG workarounds.

Ownership is intentionally narrow:

| Surface | Owner |
| --- | --- |
| Authoring references | `VegetationRuntimeContainer` |
| Compiled pages | `FoliagePageAsset` |
| Shared asset groups | `FoliageAssemblyAsset` |
| Culling, budgets, frame selection, BRG batches, fallback buffers/submission | `VegetationRenderWorld` |
| URP integration and fallback scheduling | `VegetationRendererFeature` |

## Performance Regression Root Cause

The redesign achieved the data cutover, but the runtime backend stayed on custom RenderGraph passes. `VegetationRendererFeature` executed `PrepareForCamera` / `PrepareForFrustums` inside render pass execution, then repacked selected instances and called `GraphicsBuffer.SetData` on the main thread. That made URP's render graph wait on our feature instead of letting Unity batch/cull the vegetation through its renderer-owned path.

The design mistake was treating BRG as a later experiment even though the compiled pages were already shaped like BRG batches. The correct production path on supported APIs is now:

1. compiled asset group -> one BRG batch
2. batch-owned instance data buffer -> matrices, packed tint, wind
3. BRG culling callback -> page/cell split visibility and packet selection
4. BRG draw commands -> Unity/URP owns submission

The remaining weak point is that packet selection inside the BRG culling callback is still immediate CPU work, not Burst/jobified command generation. The callback now reads a main-thread-built immutable culling snapshot, compacts visible instance ranges before publishing draw commands, clears Unity's custom culling result slot, and retired BRG generations are deferred before disposal, so the next performance hardening target is jobified selection and draw-command generation rather than another live runtime-data bridge.
| Closed SubScene registration | `Vegetation.SubScene` bootstrap/state |

## Runtime Packets

The compiler emits only these active representation kinds:

1. `TreeL0`
2. `TreeL1`
3. `TreeL2`
4. `CellHLOD`
5. `PageHLOD`

`TreeL0` uses source branch placement at near distance. `TreeL1` and `TreeL2` use generated split canopy/wood tier meshes. `CellHLOD` and `PageHLOD` must collapse each tree to its baked `impostorMesh` with `impostorMaterial` as opaque instanced far packets. They must not replay branch/trunk L3 packets and must not generate unique aggregate meshes per page or cell.

HLOD packets are always resident. Near-detail packets are marked as near-detail residency and are selected only inside the active near range and budget envelope. `VegetationRenderWorld` request-loads near-detail payloads at cell granularity under resident/upload byte budgets and falls back to HLOD when those budgets block residency. All visible trees must still render; active work and instance budgets may deny near-detail packets, but they must not cull the far impostor fallback.

## Shadows

Public shadow modes are:

1. `Off`
2. `CheapTree`

`CheapTree` submits compiled shadow packet metadata under a separate shadow work budget. Shadow selection is subordinate to the packet policy used by the render world. The shadow pass must not invent a separate higher-detail or enlarged caster path.

Current limitations:

1. Main directional light only.
2. Additional-light vegetation shadow atlases are not implemented.
3. Offscreen caster policy is conservative and needs dense-scene validation.

## Wind

Wind is shader-side. The compiler writes static `FoliageWindMetadata` per instance: phase, trunk bend weight, branch flutter weight, and anchor height. Branch, canopy, and HLOD packets use the owning tree bounds for wind phase and anchor height, not their own packet min-Y, so branch packets inherit trunk sway at their world height. Branch and canopy packets keep full trunk-following bend weight. Trunk-following displacement uses one shared time phase. Branch flutter metadata must not be added to the shared wind-direction displacement; it is only valid as small canopy-local vertex flutter over the trunk-following position, with the same deformation applied to forward, depth, and shadow passes. Runtime updates global wind and leaf-flutter constants from `VegetationFoliageFeatureSettings` and binds them for grouped draws.

Runtime must not rebuild wind metadata per frame.

## Culling And Budgets

The production baseline uses BRG `BatchCullingContext` split planes for coarse page/cell visibility on supported raw-buffer BRG APIs. The RenderGraph grouped-indirect path uses Unity `CullingGroup` for camera broad phase and one batched page/cell cascade-frustum pass for main-light shadows when BRG cannot initialize, fault-disables, or runs on Direct3D12. Shadow cascades must share one selected packet policy instead of repacking static instances per cascade.

Rejected baseline choices:

1. No per-tree sphere feed into `CullingGroup`.
2. No HZB dependency before the packet renderer is production-validated.
3. No per-container budgets.
4. No CPU readback driven submission list.

Budget owners:

1. `ColorWorkBudget`
2. `ShadowWorkBudget`
3. `MaxVisiblePacketInstances`
4. grouped command capacity
5. `NearDetailResidentByteBudget`
6. `NearDetailUploadByteBudget`

Dense scenes must admit visible cells from nearest to farthest relative to the active camera. Nearest means distance to compiled cell bounds, not distance to the cell center, so large or elongated cells do not demote camera-adjacent trees. A near cell tries its best distance tier first, then progressively cheaper near-detail tiers, and only then falls back to `CellHLOD` / `PageHLOD` baked impostor packets under budget pressure.

## Authoring Boundary

Authoring stores reusable source data:

1. trunk source mesh and trunk generated simplification mesh
2. branch prototypes
3. branch placements
4. generated split canopy/wood tier meshes
5. baked tree impostor mesh and material
6. LOD profile
7. generated mesh bake settings

Runtime does not read editor-only authoring graphs directly. It reads compiled pages.

The validator enforces:

1. readable meshes
2. opaque materials
3. LOD order
4. generated branch tier presence and bounds
5. trunk generated mesh presence and bounds
6. baked tree impostor mesh and material presence
7. budget and scale sanity

## Compiler Responsibilities

`FoliageCompiledAssetCompiler` is the only supported authoring-to-runtime upgrade path.

It precomputes:

1. page records
2. cell records
3. tree records
4. static branch placement transforms
5. bounds and culling spheres
6. packet instance ranges
7. asset group ids
8. pass indices
9. shadow packet modes
10. HLOD packet instances that collapse to baked tree impostor meshes
11. wind metadata
12. packet costs
13. command and memory upper bounds

Runtime should schedule these compiled ranges. It should not rediscover static structure.

The compiler must not bake page/cell HLOD mesh assets by combining all trees in a chunk. That design duplicates geometry per page and per cell, makes far density scale asset size with tree count, and hides the actual resident mesh cost from runtime budgets.

## Deleted Surfaces

The hard cutover removed the old renderer family instead of keeping a parallel migration path.

Deleted categories:

1. retired tree-first runtime owner, registry, and runtime tests
2. retired per-container GPU decision path
3. retired active runtime discovery
4. retired independent proxy shadow authoring path
5. retired demo GPU voxel compute surface
6. retired far/proxy preview and inspector controls
7. retired legacy packet enum outputs

There is no compatibility toggle and no bridge renderer to maintain.

## External Design Notes

Unity `CullingGroup` remains acceptable only for the fallback grouped-indirect camera path. The production BRG path must use `BatchCullingContext` split planes so camera and light visibility are emitted through Unity's renderer-owned culling/submission flow.

The useful idea from `unityHISM` is the data shape: compact chunk blobs, fixed sub-batch windows, culling-owned command emission, and command compaction. Those map cleanly to compiled pages, packet ranges, and BRG batches. They do not justify restoring multiple renderer paths.

## Current Verification Contract

The cutover is considered intact only when all of these stay true:

1. active package code has no retired renderer classes, shader properties, compute dispatch, or shadow proxy pass controls
2. active package has no `.compute`, `.cginc`, or `.hlsl` files outside the shipped shader contract
3. sample assets contain only active baked tree impostor meshes/materials for far HLOD, not retired runtime proxy paths
4. compiler tests prove packet ranges are contiguous and legacy packet kinds are absent
5. compiler tests prove no generated aggregate HLOD meshes are emitted
6. authoring validation tests prove baked impostor inputs and current LOD order are enforced
7. Unity full compile is clean after file additions/deletions
8. Rider MSBuild compile is clean after solution regeneration
9. active BRG path initializes successfully on supported raw-buffer BRG graphics APIs, while Direct3D12 uses the RenderGraph grouped-indirect backend without registering vegetation BRG batches

## Remaining Production Work

This redesign cutover is complete, but the package is not fully production-proven yet.

Required next work:

1. Burst/jobified BRG culling and draw-command generation
2. screen-error LOD with hysteresis and budget pressure
3. procedural placement output as compiled page providers
4. externalized async near-detail payload providers for disk/Addressables-backed pages
5. dense forest validation at 100k loaded instances and 1M streamed instances
6. mobile and VR validation
7. main-light shadow stress tests with wind enabled
8. explicit compatible-material validation for project-local shaders

Do not spend effort restoring retired renderer surfaces. The only acceptable forward path is strengthening the compiled packet renderer.
