# VoxGeoFol Vegetation

Unity 6 URP package for opaque-only compiled foliage. Runtime providers consume generated foliage page assets through one global render world, not live tree authoring objects.

## Summary

Production flow:

```text
VegetationTreeAuthoring
-> FoliageAssemblyAsset + FoliagePageAsset[]
-> VegetationRuntimeContainer or SubScene compiled provider
-> VegetationRenderWorld
-> URP RenderGraph preparation contract
-> RenderGraph grouped-indirect depth/color/shadow passes
-> page/cell broad phase
-> compiled packet selection under active budgets
-> CheapTree shadow packets through RenderGraph shadow atlas append
-> shader wind
-> grouped indirect draw commands
```

There is no API-specific vegetation backend selection in the current runtime. The active C# path is RenderGraph grouped-indirect on every graphics API. Cull/select/budget/compaction and indirect-args generation are scheduled as a jobified preparation slice during RenderGraph recording. RenderGraph only records already-completed preparation frames; its compute preparation pass uploads those completed buffers and writes the contract before raster vegetation draws consume it.

The renderer is opaque-only. It does not support alpha clip, transparency, masked foliage, runtime material cloning, or `LODGroup`.

## Highlights

1. Authoring still uses reusable tree/branch data: `VegetationTreeAuthoring -> TreeBlueprintSO -> BranchPlacement[] -> BranchPrototypeSO`.
2. Runtime rendering uses compiled `FoliageRepresentationPacket` ranges, not per-frame branch expansion.
3. Far density uses compiled `PageHLOD` / `CellHLOD` packets that collapse each tree to its baked impostor mesh as an instance.
4. Near detail uses compiled `TreeL0` / `TreeL1` / `TreeL2` packets.
5. Shadows use `VegetationShadowMode.Off` or `VegetationShadowMode.CheapTree`; there is no independent proxy promotion path.
6. Wind is shader-side: compiled per-instance metadata plus global wind constants.
7. Multiple classic-scene containers and closed SubScenes register into one `VegetationRenderWorld` budget/submission surface.

## Requirements

1. Unity `6000.3` or newer.
2. URP `17.3.0` or newer-compatible project setup.
3. Compute shader support for the RenderGraph preparation contract.
4. `VegetationRendererFeature` added to the active URP renderer.
5. Opaque URP-compatible vegetation materials using the package grouped-indirect instance contract for forward/depth/shadow/wind.
6. Generated `FoliageAssemblyAsset` and `FoliagePageAsset[]` assigned to each active `VegetationRuntimeContainer`.
7. For closed SubScenes, `SubSceneAuthoring` must sit on the same GameObject as the compiled `VegetationRuntimeContainer`.

## How To Use

1. Create one or more `VegetationRuntimeContainer` roots.
2. Place `VegetationTreeAuthoring` components under the container that owns them.
3. Select the container root GameObject and press `Fill Registered Authorings + Compile Pages` in the `VegetationRuntimeContainer` inspector.
4. Use `Fill Registered Authorings` alone when you only need to refresh the serialized child authoring list.
5. Confirm Unity logs a successful compile line such as `Foliage compiled '<container name>'`.
6. Confirm generated assets exist under `Assets/VoxGeoFol.Generated/Vegetation/CompiledPages/<container name>/`:
   `*_FoliageAssembly.asset` and one or more `*_Page_*.asset` files.
7. Confirm the container now has `compiledAssembly` and `compiledPages` assigned in its inspector.
8. Add `VegetationRendererFeature` to the active URP renderer.
9. Enter Play Mode or render a Scene/Game view.

Call `RefreshRuntimeRegistration()` after replacing generated page assets. Transform, hierarchy, blueprint, placement, or generated-mesh edits require recompiling the page assets.

## Compile Validation Failures

The page compiler only blocks on data it must have to emit renderable opaque packet assets: missing meshes, unreadable meshes, missing materials, non-opaque materials, invalid branch references, invalid scales, page/cell packet caps, near-detail byte caps, and shadow bounds.

Authoring quality rules still appear in the `VegetationTreeAuthoring` inspector, but they do not block `FoliageAssemblyAsset` / `FoliagePageAsset` generation:

1. `foliageMesh triangle count X exceeds budget Y`
   The source branch foliage mesh is over `Triangle Budget Foliage`. This is an authoring/performance warning for the asset, not a page compiler blocker.
2. `Wood triangle counts must not increase`
   The generated branch wood tiers are stale or heavier than expected. Use `Regenerate All Generated Meshes` when you want to repair the LOD asset chain, but page compilation can still proceed.

## Testing The Current Implementation

Use this smoke test for the compiled render-world path:

1. Open a scene with a `VegetationRuntimeContainer` and child `VegetationTreeAuthoring` instances.
2. Select the container and run `Fill Registered Authorings + Compile Pages` from its inspector.
3. Confirm generated `FoliageAssemblyAsset` / `FoliagePageAsset` references are assigned on the container.
4. In the active `VegetationRendererFeature`, keep `ShadowMode` at `CheapTree` and set visible budgets high enough to see near detail.
5. Use a small `NearDetailResidentByteBudget` or `NearDetailUploadByteBudget` to force near-detail HLOD fallback; restore larger budgets to verify near-detail returns.
6. Enable `EnableDiagnostics` to log packet counts, HLOD counts, near-detail resident bytes, loaded bytes, and eviction counters.
7. Stand inside a dense patch and verify the renderer spends near-detail budget from nearest visible cells outward: close cells use their best affordable `TreeL0/L1/L2` tier, then farther or over-budget cells degrade to `CellHLOD` / `PageHLOD`.

## Compiled Page Assets

Generated assets contain:

1. `FoliageAssemblyAsset`
   Shared mesh/material/pass groups and compiler report, including estimated resident mesh payload.
2. `FoliagePageAsset`
   Page records, cell records, tree records, static packet instances, and representation packets.
3. `FoliageRepresentationPacket`
   Contiguous static instance range for one representation and asset group.

The compiler emits:

1. Always-resident `PageHLOD` / `CellHLOD` packets that use `TreeBlueprintSO.impostorMesh` and `impostorMaterial`.
2. Near-detail `TreeL0` / `TreeL1` / `TreeL2` packets.
3. Compiled shadow packet modes: `Hlod`, `SameAsColor`, `CheapTree`, `None`.
4. Compiled wind metadata: phase, trunk bend weight, branch flutter weight, anchor height. Branch, canopy, and HLOD packets use the owning tree bounds for phase and anchor so they inherit trunk sway at the same world height. Branch and canopy packets keep full trunk-following bend weight. Runtime shader shared displacement uses trunk-following sway only; canopy shaders use branch flutter metadata only as small local vertex flutter, with matching forward/depth/shadow deformation. Local leaf-flutter strength, frequency multiplier, spatial scale, and secondary strength are runtime renderer-feature settings.
5. Validation for required opaque inputs, page/cell caps, contiguous packet ranges, shadow bounds, and near-detail bytes.

The compiler intentionally emits only current packet kinds used by the render world.

The compiler does not generate per-page or per-cell aggregate HLOD mesh assets and does not replay branch/trunk L3 packets for far density. Recompiling a container deletes stale generated HLOD mesh assets left from older compiler output under that container's `HLODMeshes` folder.

## Shader Compatibility

Package vegetation shaders support the grouped-indirect runtime path and regular editor preview MeshRenderers. `_VegetationInstanceData` is the active grouped-indirect instance payload, and `_VegetationInstanceDataBaseOffset` keeps backend instance IDs local to each draw. Legacy DOTS shader variants still exist in shader code, but there is no active BRG C# runtime backend.

## Key Settings

1. `VegetationFoliageFeatureSettings.ShadowMode`
   `Off` skips vegetation shadow-caster submission. `CheapTree` submits compiled cheap/HLOD shadow packets only; `SameAsColor` packets are not submitted by the runtime shadow pass.
2. `VegetationFoliageFeatureSettings.EnableDepthPass`
   Enables the dedicated vegetation depth pass. Disable only when the active URP feature stack can rely on color-pass depth writes.
3. `VegetationFoliageFeatureSettings.NearDetailDistance`
   Distance clamp for selecting compiled near-detail tree packets before falling back to cell/page HLOD.
4. `VegetationFoliageFeatureSettings.ColorWorkBudget`
   Active packet work budget for depth/color preparation.
5. `VegetationFoliageFeatureSettings.ShadowWorkBudget`
   Active packet work budget for shadow preparation.
6. `VegetationFoliageFeatureSettings.MaxVisiblePacketInstances`
   Upper bound for packed visible packet instances in the prepared grouped frame.
7. `VegetationFoliageFeatureSettings.NearDetailResidentByteBudget`
   Total near-detail packet payload bytes that may stay resident in the render world. HLOD packets do not count against this budget.
8. `VegetationFoliageFeatureSettings.NearDetailUploadByteBudget`
   Near-detail packet payload bytes that may be loaded into residency during one render-world prepare.
9. `VegetationFoliageFeatureSettings.WindStrength`, `WindFrequency`, `WindDirection`
   Global shader wind constants. They do not rebuild packet payloads.
10. `VegetationFoliageFeatureSettings.LeafFlutterStrength`, `LeafFlutterFrequencyMultiplier`, `LeafFlutterSpatialScale`, `LeafFlutterSecondaryStrength`
   Canopy-local leaf flutter constants. They do not rebuild packet payloads.
11. `VegetationFoliageFeatureSettings.EnableDiagnostics`
   Logs render-world packet/group/instance counters plus near-detail resident/load/eviction counters.

## Runtime Terminology

| Term | Status | Purpose |
| --- | --- | --- |
| `VegetationRuntimeContainer` | Active provider | Holds authoring references for compilation and generated compiled page references for runtime registration. |
| `VegetationRenderWorld` | Active runtime owner | Owns providers, page/cell culling, packet selection, active budgets, wind binding, grouped-indirect buffers, and diagnostics. |
| `FoliageAssemblyAsset` | Active compiled input | Shared asset groups and build report for one compiled provider. |
| `FoliagePageAsset` | Active compiled input | Page-local cells, trees, HLOD packets, near-detail packets, static instances, and metadata. |
| `FoliageAssetGroup` | Active submission group | Exact mesh/material/pass identity used for grouped-indirect draws. |
| `FoliageRepresentationPacket` | Active scheduling unit | Draw-ready packet selected by page/cell visibility, LOD distance, and active budgets. |
| `FoliagePacketResidency` | Active metadata | Distinguishes always-resident HLOD packets from near-detail packets controlled by render-world resident/upload budgets. |
| `FoliageWindMetadata` | Active metadata | Static per-instance wind phase/weights/anchor consumed by shaders. |

## Current Lifecycle

### Editor Compile

```text
VegetationRuntimeContainer.registeredAuthorings
-> FoliageCompiledAssetCompiler
-> FoliageAssemblyAsset
-> FoliagePageAsset[]
-> container compiledAssembly / compiledPages
```

### Runtime Registration

```text
VegetationRuntimeContainer.OnEnable()
-> RefreshRuntimeRegistration()
-> VegetationRenderWorld.RegisterProvider()
-> global page/cell/asset-group graph rebuilt lazily before prepare
```

### SubScene Registration

```text
SubSceneAuthoring baker
-> baked compiled assembly/page references
-> SubSceneVegetationBootstrapSystem
-> VegetationRenderWorld.RegisterProvider()
-> SubSceneVegetationRuntimeState unregisters on unload
```

### Per-Camera Rendering

```text
VegetationRendererFeature
-> VegetationRenderWorld.ScheduleRenderGraphPrepareForCamera()
   -> schedules page/cell frustum cull, nearest-cell packet selection, near-detail budgets, compaction, and args generation
-> renderGraph.ImportBuffer(instanceBuffer / argsBuffer)
-> RenderGraph compute preparation contract
   -> completes preparation job
   -> uploads compacted instance/args buffers
   -> writes graph-visible frame counters
-> RenderGraph depth/color raster passes
   -> grouped DrawMeshInstancedIndirect calls
```

Color/depth passes do not call vegetation prepare inside their render functions and do not opt into RenderGraph global-state mutation.

### Main-Light Shadows

```text
VegetationRendererFeature
-> extract main-light cascade frustums during graph recording
-> VegetationRenderWorld.ScheduleRenderGraphPrepareForFrustums()
   -> schedules page/cell split-frustum masks, compiled shadow packet mapping, shadow budget, compaction, and args generation
-> RenderGraph compute preparation contract
   -> uploads an already-completed preparation slot
-> RenderGraph shadow raster pass
-> mainShadowsTexture depth attachment with ReadWrite access
   -> explicit cascade frustum masks
   -> grouped DrawMeshInstancedIndirect calls
```

Shadows use the grouped-indirect RenderGraph shadow pass that appends into URP's main shadow atlas through the raster depth attachment path. The current shadow submit still mutates global shadow matrices/depth bias per cascade and therefore remains the one raster pass that uses RenderGraph global-state permission.

## Important Limitations

1. Near-detail residency is budgeted and request-loaded inside `VegetationRenderWorld` for the grouped-indirect upload path. Compiled page assets and shared mesh/material groups are still ScriptableObject references. Externalized async disk/Addressables payload providers are not implemented.
2. LOD selection is still distance-based in the render world. Screen-error and hysteresis remain future production hardening.
3. Additional-light vegetation shadows are not supported.
4. Offscreen shadow caster policy is still conservative and main-light only.
5. Runtime editing is not live. Recompile page assets and refresh registration after authoring or transform changes.
6. HZB occlusion is intentionally not part of the baseline.
7. The grouped-indirect RenderGraph path is the only C# runtime backend. Do not reintroduce API-specific backend routing.
8. The preparation pass is currently a jobified RenderGraph-owned vertical slice, not a fully GPU-resident culling pipeline. The next generational step is replacing the single scheduled job with compute kernels for broad phase, admission, compaction, and args writes after production validation proves the packet renderer contract.

## Missing Features

1. Externalized async near-detail payload providers for disk/Addressables-backed pages.
2. Procedural placement output as compiled page providers.
3. Screen-error LOD and VR stereo-union LOD.
4. Production mobile/VR/dense-scene validation.
5. Explicit compatible-material validation for project-local shaders.

## Included In This Repo

1. Playground scene: [../../Assets/Scenes/Playground.unity](../../Assets/Scenes/Playground.unity)
2. Package sample content: [Samples~/VegetationDemo](Samples~/VegetationDemo)
3. Runtime architecture notes: [../../DetailedDocs/VegetationRuntimeArchitecture.md](../../DetailedDocs/VegetationRuntimeArchitecture.md)
4. Generational redesign authority: [../../DetailedDocs/VegetationGenerationalRedesign.md](../../DetailedDocs/VegetationGenerationalRedesign.md)
5. Current milestone status: [../../DetailedDocs/Milestone2.md](../../DetailedDocs/Milestone2.md)

## Supported Devices

1. Desktop and laptop GPUs that run Unity 6 URP RenderGraph with compute shader support.
2. Console-class targets with the same feature support.
3. Higher-end mobile and handheld targets after profiling validates budgets.
4. Not targeted: WebGL and very low-end mobile hardware.

## License

Package license: [LICENSE.md](LICENSE.md)

## Kudos

Thanks to [unity-voxel](https://github.com/mattatz/unity-voxel) and its author @mattatz.
