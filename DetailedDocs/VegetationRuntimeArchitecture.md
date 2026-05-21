# Vegetation Runtime Architecture

Purpose: current runtime ownership and render-flow authority for the vegetation package.

Status: active. Runtime rendering is compiled-page based through `VegetationRenderWorld`.

## Runtime Principle

Runtime consumes compiled packet data. It does not consume live branch authoring, create per-container renderers, or generate branch work during camera/shadow preparation.

```text
compiled provider
-> VegetationRenderWorld
-> page/cell broad phase
-> packet selection
-> grouped instance upload
-> grouped indirect args
-> URP depth/color/shadow draws
```

## Data Owners

| Data | Owner |
| --- | --- |
| Source tree/branch authoring | `VegetationTreeAuthoring`, `TreeBlueprintSO`, `BranchPrototypeSO` |
| Generated branch/trunk meshes | editor bake tools |
| Compiled page records | `FoliagePageAsset` |
| Shared mesh/material/pass groups | `FoliageAssemblyAsset` |
| Runtime provider registration | `VegetationRuntimeContainer`, `Vegetation.SubScene` bootstrap |
| Frame culling, budgets, upload, submission | `VegetationRenderWorld` |
| URP scheduling | `VegetationRendererFeature` |

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
-> VegetationRenderWorld.PrepareForCamera(camera, settings)
   -> refresh provider graph when dirty
   -> update page/cell CullingGroup data
   -> request visible near-detail cell residency under resident/upload byte budgets
   -> select visible cells nearest-to-farthest under color budget
   -> degrade each near cell through cheaper near-detail tiers before HLOD
   -> pack grouped instance payload
   -> write grouped indirect args
-> submit color grouped indirect draws
-> optionally submit dedicated depth grouped indirect draws when EnableDepthPass is enabled
```

The active broad phase is page/cell `CullingGroup`. It is intentionally coarse and stable. Do not feed per-tree spheres into it.

`VegetationFoliageFeatureSettings.EnableDepthPass` controls whether the dedicated vegetation depth pass is scheduled. Leave it enabled when the active URP stack needs vegetation depth before opaques, depth texture, or depth-dependent effects; disable it when the color pass depth write is sufficient.

## Shadow Flow

```text
main light cascade set
-> VegetationRenderWorld.PrepareForFrustums(cascadeFrustums, settings)
   -> explicit page/cell cascade-frustum mask tests
   -> request near-detail cell residency under resident/upload byte budgets when near shadow packets are selected
   -> compiled shadow packet mapping
   -> shadow work budget
   -> one grouped instance payload shared by all cascades
   -> one grouped indirect args surface shared by all cascades
-> submit shadow grouped indirect draws only for cascades/groups that have selected shadow packets
```

`VegetationShadowMode.Off` skips vegetation shadow-caster submission.

`VegetationShadowMode.CheapTree` submits compiled shadow packets only. Shadow selection is subordinate to compiled packet metadata and must not create an independent richer or enlarged caster representation.

`VegetationRenderWorld.Prepare` carries submarkers for graph refresh, broad phase, packet selection, upload layout, instance copy, args writes, and buffer upload. Keep those markers intact; they are the runtime regression map.

URP owns the main-light shadow atlas. `VegetationRendererFeature` only appends vegetation casters to that atlas, so the render-graph pass must bind `mainShadowsTexture` as a raster depth attachment with read/write access. Manual unsafe `SetRenderTarget` binding is backend-fragile and has failed on DirectX as shadow writes bleeding into color output.

Because vegetation casters are indirect draws instead of URP renderer-list entries, the shadow pass must explicitly push the current cascade view/projection matrices into shader globals before each cascade submission and restore the game camera matrices afterward. Camera matrix leakage into the shadow-caster draw makes the atlas contents follow Game View camera rotation even though Scene View may look correct. When writing those shader globals, use URP's GPU-adjusted projection convention; writing raw projection globals can flip the draw.

## Near-Detail Residency

HLOD packets are always resident. Near-detail packet payloads are selected at cell granularity inside compiled pages and controlled by two render-world budgets:

1. `NearDetailResidentByteBudget`
2. `NearDetailUploadByteBudget`

Visible near cells request their cell payload before selecting `TreeL0/L1/L2` packets. Packet admission is nearest-cell first relative to the active camera using distance to compiled cell bounds, not distance to the cell center. This matters for elongated or line-shaped cells where a tree can be adjacent to the camera while the cell midpoint is still far away. The renderer selects the best distance tier when it fits, then tries cheaper near-detail tiers, then falls back to `CellHLOD` / `PageHLOD`. If the resident budget, upload budget, cell payload size, color work budget, or visible detail instance budget blocks the request, that fallback is mandatory so visible trees are degraded, not dropped. A large compiled page must not block all near detail when individual cells fit the active upload budget.

Residency is least-recently-used at cell granularity. Cells requested during the current prepare are protected from eviction; older unrequested cells are evicted until the resident budget fits.

Compiled page assets are still ScriptableObject references. This is runtime residency and upload budgeting, not an externalized disk/Addressables provider yet.

Shared mesh/material groups are still referenced by the compiled assembly. HLOD must reuse baked tree impostor meshes/materials; page/cell compilation must not generate combined aggregate HLOD mesh assets and must not replay branch/trunk L3 packets for far density.

## Buffers And Submission

`VegetationRenderWorld` owns the runtime buffers and releases them on reset:

1. shared visible packet instance buffer
2. grouped indirect args buffers
3. page/cell culling state
4. provider graph caches
5. diagnostics counters

Submission is grouped by `FoliageAssetGroup`: mesh, material, submesh, pass, and shader contract.

Shader and indirect draw code must validate packet, group, pass, and instance indices before buffer reads or indirect-args writes. Invalid data drops work.

## Wind

The compiler writes static `FoliageWindMetadata` per packet instance. Branch, canopy, and HLOD packet bounds remain packet-owned for culling, but wind phase and anchor height come from the owning tree bounds so detached packet groups inherit trunk sway at the same world height. Branch and canopy packets must keep full trunk-following bend weight. In shader code, trunk-following displacement uses only the compiled phase plus global time. Branch flutter metadata must not be added to that shared wind-direction displacement because that makes attached branch packets sway with a larger envelope than the trunk. Canopy shaders may use branch flutter metadata only as small local vertex flutter over the already trunk-following position, with matching forward, depth, and shadow deformation. Leaf flutter amplitude, speed, spatial variation, and secondary harmonic strength are runtime `VegetationFoliageFeatureSettings` values, not compiler metadata. Runtime binds:

1. global wind direction
2. wind strength
3. wind frequency
4. leaf flutter settings
5. shared instance buffer

Wind changes must not rebuild compiled packet payloads.

## Material Contract

Vegetation materials must be opaque and compatible with the package indirect instance layout. Required passes:

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

`_VegetationInstanceData` must be gated by `_VOXGEOFOL_INDIRECT_RENDERING` in addition to Unity procedural instancing. Unity can select instancing/procedural variants for regular MeshRenderer/editor-preview draws on DirectX; those paths must fall back to object matrices instead of declaring an unbound SRV. `VegetationRenderWorld` enables `_VOXGEOFOL_INDIRECT_RENDERING` only around grouped indirect submission. Grouped draws must not rely on indirect-args `startInstance` for instance-buffer indexing; the render world writes zero `startInstance` and binds `_VegetationInstanceDataBaseOffset` per group so DirectX and Vulkan read the same payload records.

## Runtime Constraints

1. No synchronous GPU readback in render-pass setup, camera prepare, shadow prepare, or submission.
2. No per-container budgets.
3. No branch-work generation in runtime prepare.
4. No hidden live-authoring fallback.
5. No parallel renderer bridge.
6. No HZB dependency before the packet renderer is production-validated.
7. Main-light directional shadows only for the current package.

## Remaining Runtime Work

1. Screen-error LOD with hysteresis and budget pressure.
2. Procedural placement providers compiled directly to page assets.
3. Externalized async near-detail payload providers for disk/Addressables-backed pages.
4. Dense scene, mobile, VR, shadow, and wind validation.
