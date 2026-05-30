# Vegetation Runtime Architecture

Purpose: current runtime ownership and render-flow authority for the vegetation package.

Status: active. Runtime rendering is compiled-page based through `VegetationRenderWorld`.

## Runtime Principle

Runtime consumes compiled packet data. It does not consume live branch authoring, create per-container renderers, or generate branch work during camera/shadow preparation.

```text
compiled provider
-> VegetationRenderWorld
-> BatchRendererGroup batches by compiled FoliageAssetGroup on supported non-D3D12 APIs
-> RenderGraph grouped-indirect passes on Direct3D12 and unsupported/faulted BRG APIs
-> page/cell split-frustum broad phase
-> packet selection
-> visible instance index output
-> URP BRG or RenderGraph depth/color/shadow draws
```

Direct3D12 on Unity `6000.3.15f1` uses the RenderGraph grouped-indirect backend. Crash logs prove D3D12 dies in native `InjectShadowDrawCommands` after custom vegetation BRG batch registration but before the vegetation culling callback publishes draw commands, even with BRG light views disabled and shadow caster passes removed from registered materials. Do not reintroduce D3D12 vegetation BRG registration until Unity's native shadow extraction path is fixed or the project has a reproducible safe BRG slice.

## Data Owners

| Data | Owner |
| --- | --- |
| Source tree/branch authoring | `VegetationTreeAuthoring`, `TreeBlueprintSO`, `BranchPrototypeSO` |
| Generated branch/trunk meshes | editor bake tools |
| Compiled page records | `FoliagePageAsset` |
| Shared mesh/material/pass groups | `FoliageAssemblyAsset` |
| Runtime provider registration | `VegetationRuntimeContainer`, `Vegetation.SubScene` bootstrap |
| BRG batch resources, culling, budgets, visible-instance output, fallback upload/submission | `VegetationRenderWorld` |
| URP integration and fallback scheduling | `VegetationRendererFeature` |

## Compiled Assets

`FoliageAssemblyAsset` stores shared asset groups and compiler report data.

`FoliagePageAsset` stores:

1. page records
2. cell records
3. tree records
4. static packet instances
5. representation packets
6. HLOD packet instances that collapse trees to shared baked impostor mesh references
7. packet residency metadata
8. compiled shadow modes
9. wind metadata

`FoliageRepresentationPacket` is the runtime scheduling unit. It references a contiguous static instance range and one `FoliageAssetGroup`.

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
VegetationRendererFeature
-> VegetationRenderWorld.RefreshBatchRenderer(settings)
   -> refresh provider graph when dirty
   -> supported non-D3D12 raw-buffer APIs create one BRG batch per compiled FoliageAssetGroup
   -> Direct3D12, unsupported APIs, or faulted BRG setup return false
-> supported BRG APIs: Unity BRG camera culling callback
   -> page/cell split-frustum tests from BatchCullingContext
   -> select visible cells nearest-to-farthest under color budget using callback-local scratch
   -> degrade each near cell through cheaper near-detail tiers before HLOD
   -> emit visible instance indices and direct BRG draw commands
-> Direct3D12/fallback: grouped-indirect RenderGraph camera/depth passes
   -> page/cell CullingGroup broad phase
   -> grouped DrawMeshInstancedIndirect calls
```

If BRG raw-buffer setup fails, fault-disables, or runs on Direct3D12, `RefreshBatchRenderer` returns false and the same provider graph flows through the grouped-indirect RenderGraph camera path instead.

The active BRG broad phase is page/cell bounds against `BatchCullingContext` split planes. The fallback grouped-indirect path still uses page/cell `CullingGroup` for camera passes. Both paths are intentionally coarse and stable. Do not feed per-tree spheres into either broad phase.

`VegetationFoliageFeatureSettings.EnableDepthPass` only affects the fallback grouped-indirect RenderGraph depth pass. The BRG path relies on Unity/URP pass scheduling for materials that expose the package depth pass.

## Shadow Flow

```text
main light cascade set
-> supported BRG APIs: Unity BRG light culling callback
   -> page/cell split-frustum mask tests from BatchCullingContext
   -> callback-local packet selection without mutating render-world residency/upload state
   -> compiled shadow packet mapping
   -> shadow work budget
   -> visible instance index output
   -> split visibility masks on BRG draw commands
-> supported BRG APIs: URP submits shadow caster BRG draws only for visible splits/groups
-> Direct3D12/fallback: grouped-indirect RenderGraph shadow pass
   -> mainShadowsTexture depth attachment with ReadWrite access
   -> explicit cascade frustum masks
   -> grouped DrawMeshInstancedIndirect calls
```

When BRG cannot initialize, the fallback grouped-indirect shadow pass appends vegetation casters into URP's main shadow atlas through a RenderGraph raster depth attachment.

On Direct3D12 with Unity `6000.3.15f1`, `CheapTree` shadows use the grouped-indirect RenderGraph shadow pass. The D3D12 backend does not register vegetation BRG batches because Unity's native shadow extraction crashes from the presence of the custom BRG before any vegetation callback output exists.

`VegetationShadowMode.Off` skips vegetation shadow-caster submission.

`VegetationShadowMode.CheapTree` submits compiled shadow packets only. Shadow selection is subordinate to compiled packet metadata and must not create an independent richer or enlarged caster representation.

`VegetationRenderWorld.Prepare` markers remain for the fallback path. BRG culling emits draw commands from Unity's callback, but it must use callback-local scratch over a main-thread-built immutable culling snapshot and treat BRG batch buffers as already resident. It must not read live `FoliagePageAsset` ScriptableObjects or mutate shared near-detail residency, upload, or prepared-frame state from the callback. Retired BRG generations stay alive for a short render-frame drain window before native resources are disposed. Keep both marker families intact; they are the regression map.

URP owns the main-light shadow atlas. `VegetationRendererFeature` only appends vegetation casters to that atlas, so the render-graph pass must bind `mainShadowsTexture` as a raster depth attachment with read/write access. Manual unsafe `SetRenderTarget` binding is backend-fragile and has failed on DirectX as shadow writes bleeding into color output.

The manual cascade matrix push/restore is only required by the fallback grouped-indirect shadow pass. BRG shadow caster draws are renderer-owned and must not reintroduce manual shadow-atlas `SetRenderTarget` or camera-matrix mutation.

## Near-Detail Residency

HLOD packets are always resident. In the BRG path, compiled near-detail instances are already uploaded into batch-owned instance buffers at graph rebuild time, so camera/light callbacks select near-detail packets without request-loading residency. In the fallback grouped-indirect path, near-detail packet payloads are selected at cell granularity inside compiled pages and controlled by two render-world budgets:

1. `NearDetailResidentByteBudget`
2. `NearDetailUploadByteBudget`

Visible near cells request their cell payload before selecting `TreeL0/L1/L2` packets. Packet admission is nearest-cell first relative to the active camera using distance to compiled cell bounds, not distance to the cell center. This matters for elongated or line-shaped cells where a tree can be adjacent to the camera while the cell midpoint is still far away. The renderer selects the best distance tier when it fits, then tries cheaper near-detail tiers, then falls back to `CellHLOD` / `PageHLOD`. If the resident budget, upload budget, cell payload size, color work budget, or visible detail instance budget blocks the request, that fallback is mandatory so visible trees are degraded, not dropped. A large compiled page must not block all near detail when individual cells fit the active upload budget.

Residency is least-recently-used at cell granularity. Cells requested during the current prepare are protected from eviction; older unrequested cells are evicted until the resident budget fits.

Compiled page assets are still ScriptableObject references. This is runtime residency and upload budgeting, not an externalized disk/Addressables provider yet.

Shared mesh/material groups are still referenced by the compiled assembly. HLOD must reuse baked tree impostor meshes/materials; page/cell compilation must not generate combined aggregate HLOD mesh assets and must not replay branch/trunk L3 packets for far density.

## Buffers And Submission

`VegetationRenderWorld` owns the runtime buffers and releases them on reset:

1. BRG batch `GraphicsBuffer` instances containing object/world matrices, packed leaf tint, and wind metadata
2. fallback shared visible packet instance buffer
3. fallback grouped indirect args buffers
4. page/cell culling state
5. provider graph caches
6. diagnostics counters

Submission is grouped by `FoliageAssetGroup`: mesh, material, submesh, pass, and shader contract. In the BRG path, one `FoliageAssetGroup` maps to one BRG batch and one batch-owned instance data buffer. The fallback path keeps the older grouped indirect buffer and args layout only when BRG raw-buffer setup cannot run.

Shader, BRG, and fallback indirect draw code must validate packet, group, pass, and instance indices before buffer reads or command/args writes. BRG visible instance ranges must be counted from validated packet entries and group-local instance indices, not raw packet counts. BRG callbacks must use callback-local scratch for visibility, packet selections, group counts, and visible-instance writes, sourced from immutable culling snapshots rather than live ScriptableObjects or mutable world lists. BRG output must compact visible instance ranges before publishing draw commands and clear `BatchCullingOutput.customCullingResult[0]` to zero because Unity native shadow extraction is intolerant of stale callback state. Invalid data drops work.

## Wind

The compiler writes static `FoliageWindMetadata` per packet instance. Branch, canopy, and HLOD packet bounds remain packet-owned for culling, but wind phase and anchor height come from the owning tree bounds so detached packet groups inherit trunk sway at the same world height. Branch and canopy packets must keep full trunk-following bend weight. In shader code, trunk-following displacement uses only the compiled phase plus global time. Branch flutter metadata must not be added to that shared wind-direction displacement because that makes attached branch packets sway with a larger envelope than the trunk. Canopy shaders may use branch flutter metadata only as small local vertex flutter over the already trunk-following position, with matching forward, depth, and shadow deformation. Leaf flutter amplitude, speed, spatial variation, and secondary harmonic strength are runtime `VegetationFoliageFeatureSettings` values, not compiler metadata. Runtime binds:

1. global wind direction
2. wind strength
3. wind frequency
4. leaf flutter settings
5. shared instance buffer

Wind changes must not rebuild compiled packet payloads.

## Material Contract

Vegetation materials must be opaque and compatible with the package BRG/DOTS instance layout plus the fallback indirect instance layout. Required passes:

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

BRG materials must compile `DOTS_INSTANCING_ON`. The package shaders read BRG metadata for built-in matrices plus `_VegetationPackedLeafTint` and `_VegetationWind`. `_VegetationInstanceData` is declared only for Unity procedural-instancing variants that are not DOTS/BRG variants. The fallback grouped-indirect path binds that buffer through command-buffer global state and the per-draw property block, while regular MeshRenderer/editor-preview draws fall back to object matrices and BRG draws use DOTS metadata instead of an unbound SRV.

## Runtime Constraints

1. No synchronous GPU readback in render-pass setup, camera prepare, shadow prepare, or submission.
2. No per-container budgets.
3. No branch-work generation in runtime prepare.
4. No hidden live-authoring fallback.
5. No parallel renderer bridge.
6. No HZB dependency before the packet renderer is production-validated.
7. Main-light directional shadows only for the current package.
8. The RenderGraph grouped-indirect path is the Direct3D12 backend and the fallback when BRG cannot initialize or fault-disables. D3D12 performance work belongs here.

## Remaining Runtime Work

1. Burst/jobify BRG culling and draw-command generation; the current slice removes the RenderGraph stall only on supported raw-buffer BRG APIs, while Direct3D12 still needs grouped-indirect RenderGraph prepare/upload optimization.
2. Screen-error LOD with hysteresis and budget pressure.
3. Procedural placement providers compiled directly to page assets.
4. Externalized async near-detail payload providers for disk/Addressables-backed pages.
5. Dense scene, mobile, VR, shadow, and wind validation.
