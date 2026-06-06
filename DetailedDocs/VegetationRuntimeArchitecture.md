# Vegetation Runtime Architecture

Purpose: current runtime ownership and render-flow authority for the vegetation package.

Status: active. Runtime rendering is compiled-page based through `VegetationRenderWorld` and one URP RenderGraph grouped-indirect backend. The old C# BRG backend is deleted; do not restore it as a per-API path or fallback.

## Runtime Principle

Runtime consumes compiled packet data. It does not consume live branch authoring, create per-container renderers, generate branch work during camera/shadow preparation, or route through multiple renderer backends.

```text
compiled provider
-> VegetationRenderWorld
-> RenderGraph-owned jobified preparation
   -> page/cell broad phase
   -> packet selection under global budgets
   -> compaction and indirect args generation
   -> completed-frame slot handoff
-> RenderGraph compute preparation contract
   -> grouped instance/args buffer upload without job completion
-> RenderGraph depth/color/shadow grouped indirect draws
```

The current preparation pass is a vertical slice: RenderGraph recording schedules the next preparation job, but records only a preparation slot that was already completed before graph execution. The RenderGraph compute pass uploads compacted instance/args buffers and writes graph-visible frame metadata without calling `JobHandle.Complete()`. This removes the previous off-graph preparation path from raster execution and graph recording, while leaving a later GPU-kernel implementation as the next generation after validation.

## Data Owners

| Data | Owner |
| --- | --- |
| Source tree/branch authoring | `VegetationTreeAuthoring`, `TreeBlueprintSO`, `BranchPrototypeSO` |
| Generated branch/trunk meshes | editor bake tools |
| Compiled page records | `FoliagePageAsset` |
| Shared mesh/material/pass groups | `FoliageAssemblyAsset` |
| Runtime provider registration | `VegetationRuntimeContainer`, `Vegetation.SubScene` bootstrap |
| Culling, budgets, visible-instance output, per-cascade shadow args, grouped-indirect buffers, wind constants | `VegetationRenderWorld` scheduled preparation job |
| URP RenderGraph scheduling, buffer import, completed-frame upload, shadow atlas append | `VegetationRendererFeature` and `VegetationRenderGraphPreparationContract` |

## Provider Registration

Classic scene:

```text
VegetationRuntimeContainer.OnEnable()
-> RefreshRuntimeRegistration()
-> VegetationRenderWorld.RegisterProvider()
```

Closed SubScene:

```text
SubSceneAuthoring baker
-> SubSceneVegetationPageBaked buffers
-> SubSceneVegetationBootstrapSystem
-> VegetationRenderWorld.RegisterProvider()
-> SubSceneVegetationRuntimeState unregisters on unload
```

Provider registration is compiled-page only. Missing generated pages log and skip; there is no live-authoring fallback.

## Camera Flow

```text
VegetationRendererFeature.RecordRenderGraph
-> VegetationRenderWorld.ScheduleRenderGraphPrepareForCamera()
   -> schedules page/cell frustum culling
   -> schedules nearest-visible-cell packet selection
   -> schedules near-detail residency/upload budget checks
   -> schedules grouped instance compaction and indirect args generation
-> renderGraph.ImportBuffer(instanceBuffer / argsBuffer)
-> VegetationRenderGraphPreparationContract.Record()
   -> uploads an already-completed preparation slot
   -> writes graph-visible frame counters
-> RenderGraph depth/color raster pass
   -> consumes instance/args/contract buffers
   -> submits grouped DrawMeshInstancedIndirect
```

Depth/color render functions submit only the already prepared frame. They must not call camera prepare and must not use `AllowGlobalStateModification`.

## Shadow Flow

```text
VegetationRendererFeature.RecordShadowRenderGraph
-> validate main directional light and cascade atlas
-> extract cascade frustums during graph recording
-> VegetationRenderWorld.ScheduleRenderGraphPrepareForFrustums()
   -> schedules page/cell split-frustum masks
   -> schedules compiled shadow packet mapping
   -> schedules shadow work budget
   -> schedules grouped instance compaction and indirect args generation
-> renderGraph.ImportBuffer(instanceBuffer / argsBuffer)
-> VegetationRenderGraphPreparationContract.Record()
   -> uploads an already-completed preparation slot
-> RenderGraph shadow raster pass
   -> mainShadowsTexture depth attachment with ReadWrite access
   -> per-cascade grouped DrawMeshInstancedIndirect
```

The shadow pass still pushes cascade view/projection, shadow bias, and depth bias as global state because URP's main-light shadow atlas append currently requires that compatibility surface. This is the remaining RenderGraph global-state exception and should be isolated until a cleaner URP shadow contract exists.

`VegetationShadowMode.Off` skips vegetation shadow-caster submission.

`VegetationShadowMode.CheapTree` submits compiled cheap/HLOD shadow packets only. Runtime must reject `SameAsColor` shadow packets for the shadow pass because that path replays near-detail color geometry and defeats the cheap shadow contract. Shadow selection is subordinate to compiled packet metadata and must not create an independent richer or enlarged caster representation.

## Near-Detail Residency

HLOD packets are always resident. Near-detail packets are selected inside the active near range and budget envelope. `VegetationRenderWorld` request-loads near-detail payloads at cell granularity under:

1. `NearDetailResidentByteBudget`
2. `NearDetailUploadByteBudget`

Visible near cells request their cell payload before selecting `TreeL0/L1/L2` packets. Packet admission is nearest-cell first relative to the active camera using distance to compiled cell bounds, not distance to the cell center. The renderer selects the best distance tier when it fits, then tries cheaper near-detail tiers, then falls back to `CellHLOD` / `PageHLOD`. Visible trees must degrade, not disappear, when detail budgets fail.

## Buffers And Submission

`VegetationRenderWorld` owns and releases:

1. grouped visible packet instance buffer
2. grouped indirect args buffer
3. job-owned page/cell culling and preparation state
4. provider graph caches
5. diagnostics counters

Submission is grouped by `FoliageAssetGroup`: mesh, material, submesh, pass, and shader contract. Indirect args use `GraphicsBuffer.Target.IndirectArguments`, keep `startInstance` zero, and use `_VegetationInstanceDataBaseOffset` for backend-stable lookup.

Shader and indirect draw code must validate packet, group, pass, and instance indices before buffer reads or args writes. Invalid data drops work.

## Wind

The compiler writes static `FoliageWindMetadata` per packet instance. Runtime binds global wind direction, wind strength, wind frequency, leaf flutter settings, and the shared instance buffer. Wind changes must not rebuild compiled packet payloads.

## Material Contract

Vegetation materials must be opaque and compatible with the grouped-indirect instance layout. Required passes:

1. forward
2. depth
3. shadow-caster

The package shaders provide the reference contract:

1. `VegetationCanopyLit.shader`
2. `VegetationTrunkLit.shader`
3. `VegetationFarMeshLit.shader`
4. `VegetationDepthOnly.shader`
5. `VegetationIndirectCommon.hlsl`

Runtime must not manufacture per-slot material copies for compatibility.

## Runtime Constraints

1. No synchronous GPU readback in render-pass setup, camera prepare, shadow prepare, or submission.
2. No per-container budgets.
3. No branch-work generation in runtime prepare.
4. No hidden live-authoring fallback.
5. No parallel renderer bridge.
6. No C# BRG runtime backend.
7. No HZB dependency before the packet renderer is production-validated.
8. Main-light directional shadows only for the current package.

## Remaining Runtime Work

1. Replace the current single scheduled preparation job with GPU compute kernels for cull/admission/compaction/args after validating the packet renderer contract.
2. Replace distance-only packet selection with screen-error plus hysteresis and budget pressure.
3. Add procedural placement providers compiled directly to page assets.
4. Add externalized async near-detail payload providers for disk/Addressables-backed pages.
5. Validate dense scene, mobile, VR, shadow, and wind profiles.
