# Project Specific Rules

Purpose: compact cross-module rules, runtime authorities, and wiring hubs.

## Global Rules

1. Budgeting and optimization on anything asset related. By settings: cutoff and hard budgets, pooling, level of detail, trimming, full occlusion, and incremental per-batch/per-frame processing.
2. OOP controllers are request-driven or consume-driven only; they do not mutate gameplay state directly.
3. Never use direct C# events with ECS.
4. Never store mutable runtime data in ScriptableObjects.
5. Do not rely on Unity lifecycle callbacks and must use explicit runtime APIs in EditMode tests.
6. Prefer structs where applicable, but do not use struct types as hot dictionary keys when avoidable (IL2cpp generic exponential growth issue with value types in generics).
7. No hidden assumptions: missing setup must fail explicitly. Exception: URP/render-graph runtime paths must log and disable/skip instead of throwing inside execution callbacks.
8. All integration boundaries need concise `[INTEGRATION]` summaries.
9. All static runtime stores must have deterministic `Reset` coverage through `StaticServicesReset`, and editor lifecycle teardown must cover play-mode exit, assembly reload, and editor quit so GPU resources are released before Unity recompiles or shuts down.
10. OOP communications to ECS happen through transient request entities, either queued directly by helpers or created by loop-owned bus ingress.
11. MVC separation for UI remains in effect.
12. Prefer `Refresh` and `Simulate` naming over `Update` for explicit runtime APIs.
13. Keep authoring, runtime data, and Unity binding logic separate.
14. Separate what should done at edit-mode and runtime.
15. Hot paths should be allocation-aware and follow DRY/KISS/SingleResponsibility best practices.
16. Reusable public-facing features should live in embedded packages under `Packages/`; repo-local-only features can remain under `Assets/Scripts/Features`.
17. No useless maintenance. When refactoring/redesigning scripts are either completely dropped or fully migrated to new api, no obsolete wrappers allowed!
18. No useless abstractions, no bloatware.
19. Avoid constant asset creation and editor refresh in tests! It makes test run multiple times slower!
20. Avoid constant asset creation if full algorithm is not finished! Create finished asset once but do not refresh editor constantly! Only do `AssetDatabase.SaveAssets()` once all meshes are created. All operations with `AssetDatabase` is generally very-very long!

--

## Vegetation System Rules

1. All vegetation geometry is opaque-only: no transparency, alpha clip, or masked runtime materials.
2. Trees are authored from reusable branch prototypes, but runtime consumes compiled page assets only. Do not add live-authoring discovery, tree-first runtime paths, or compatibility bridge renderers.
3. Temporary canopy voxel hierarchies are bake-internal only. Persist only generated split-tier meshes (`branchL1/2/3CanopyMesh`, `branchL1/2/3WoodMesh`, `trunkL3Mesh`) and keep generated voxel artifacts inside authoritative source bounds.
4. Compiled runtime packets are the source of truth: `TreeL0` uses source branch geometry, `TreeL1/L2` use baked canopy/wood tiers, and `CellHLOD` / `PageHLOD` collapse each tree to its baked impostor mesh/material.
5. All editor-baked voxel artifacts must stay inside authoritative source occupancy.Generated shell and trunk meshes prefer topology-preserving simplification first, then bounded fallback. Shell/trunk simplification must remain optional so raw voxel output stays inspectable.
6. Vegetation package code lives under `Packages/com.voxgeofol.vegetation/`. Generated writable mesh/page assets live under project `Assets/`, never under `Packages/`.
7. Runtime provider registration is compiled-page based. `VegetationRuntimeContainer` and `SubSceneAuthoring` are providers only; `VegetationRenderWorld` is the runtime owner. Missing compiled pages log and skip instead of falling back to live authoring.
8. Transform, hierarchy, blueprint, placement, or generated-mesh edits require recompiling page assets and calling `RefreshRuntimeRegistration()`. Runtime registration must not duplicate bake hierarchy data, upload temporary canopy data, or regenerate branch placement work.
9. Closed `SubScene` support must not pull DOTS into the main `Vegetation` asmdef. Bakers, baked components, and bootstrap systems live in `Vegetation.SubScene` and register compiled page providers with `VegetationRenderWorld`.
10. Runtime broad phase is page/cell only. The current RenderGraph path uses scheduled page/cell frustum tests for camera visibility and batched explicit frustum planes for main-light shadows. Do not feed per-tree spheres to runtime broad phase.
11. Runtime budgets are global to `VegetationRenderWorld`, not per container: color work, shadow work, selected packet instances, grouped command capacity, near-detail resident bytes, and near-detail upload bytes.
12. Dense-scene budgeting is nearest-visible-cell first. Try the best distance tier, then cheaper near-detail tiers, then `CellHLOD` / `PageHLOD`; visible trees must degrade, not disappear, when detail budgets fail.
13. Never put synchronous GPU readback (`ComputeBuffer.GetData`, `GraphicsBuffer.GetData`, blocking async-readback waits) in render-pass setup, camera prepare, shadow prepare, or submission callbacks.
14. Never let multiple URP consumers overwrite one shared mutable resident frame in the same render cycle. Camera depth/color may reuse a matching prepared camera frame; main-light shadow cascades must share one batched shadow preparation.
15. Never reintroduce single-thread O(N^2) nearest-tree admission or runtime branch-work generation. Runtime chooses compiled packets.
16. `BatchRendererGroup`/BRG is deleted from the C# runtime backend. Do not restore it as an API-specific path or fallback mechanism.
17. The single production backend is URP RenderGraph grouped-indirect submission with an explicit compute preparation contract consumed by every vegetation raster pass.
18. RenderGraph execution may only consume already-completed vegetation preparation frames. Never call `JobHandle.Complete()` from the RenderGraph compute contract or raster passes; schedule the next preparation job during record and skip/reuse completed work when no slot is ready.
19. RenderGraph color/depth passes must import the preparation-owned instance and args buffers, depend on the compute contract buffer, and avoid `AllowGlobalStateModification`.
20. Grouped-indirect args must use `GraphicsBuffer.Target.IndirectArguments` only, keep indirect `startInstance` zero, and index `_VegetationInstanceData` through `_VegetationInstanceDataBaseOffset`.
21. Runtime shadow support is main-light directional only through `VegetationShadowMode.Off` and `VegetationShadowMode.CheapTree`. No additional-light vegetation shadow atlas exists yet.
22. `Off` skips vegetation shadow-caster submission. `CheapTree` submits only compiled cheap/HLOD shadow packets under `ShadowWorkBudget`; runtime must reject `SameAsColor` packets for the shadow pass because they replay near-detail color geometry. Shadow selection must be subordinate to compiled packet metadata and must not invent richer or enlarged caster geometry.
23. Shadow budget pressure follows the same degradation policy as color: near detail -> cell HLOD -> page HLOD -> culled. Do not create independent high-detail shadow work after color has degraded.
24. Shadow preparation must write compacted instance spans and indirect args per `(cascade, asset group)`. Do not draw one whole group-wide shadow args range into every matching cascade.
25. Grouped-indirect shadow injection must append into URP's main shadow atlas through a RenderGraph raster depth attachment with `AccessFlags.ReadWrite`.
26. Shadow draws currently bind GPU-adjusted cascade view/projection globals per cascade and restore camera globals afterward. Do not rely on URP's post-shadow camera state.
27. Per-instance color variation uses one packed uint path read from `_VegetationInstanceData`. Do not use `MaterialPropertyBlock` for per-instance variation.
28. Runtime material compatibility is explicit: forward/depth/shadow passes must include the grouped-indirect instance layout, global wind constants, and `_VegetationInstanceDataBaseOffset`.
29. Canopy and HLOD shaders are minimal vertex-lit. Trunk shader uses albedo texture but no normal map. Runtime must not manufacture per-slot material copies for compatibility.
30. No Unity `LODGroup`; LOD selection is owned by authored distance bands plus compiled packet selection. HZB remains a later accelerator only after packet renderer production validation.
31. Shader wind is runtime-bound global constants plus compiled `FoliageWindMetadata`. Do not rebuild wind metadata per frame. Use owning tree bounds for wind phase/anchor across trunk, branch, canopy, and HLOD packets; packet bounds remain for culling only.
32. Runtime rendering resource owners must be exception-safe on partial construction. Diagnostics must stay behind `EnableDiagnostics`, avoid steady-state allocations, and preserve profiler markers for graph refresh, broad phase scheduling, selection scheduling, render-graph preparation upload, execute, draw submit, and shadow restore.
33. Shader and indirect draw code must guard runtime-derived buffer indices before buffer reads or indirect-args writes. Invalid packet, asset-group, pass, or instance indices must drop work.

## Wiring Hubs

- `VegetationRenderWorld` - production runtime hub: owns compiled providers, grouped-indirect instance/args buffers, scheduled page/cell broad phase, packet selection, color/shadow budgets, near-detail resident/upload budgets, completed-frame slot ownership, compaction/args preparation state, shader wind constants, diagnostics counters, and resource reset.
- `VegetationRuntimeContainer` - classic-scene compiled provider hub: owns serialized `VegetationTreeAuthoring` references for editor compilation plus generated `FoliageAssemblyAsset` / `FoliagePageAsset[]` runtime references, exposes the inspector compile button, and registers/unregisters those pages with `VegetationRenderWorld` on enable/disable or explicit refresh.
- `VegetationRendererFeature` - URP integration: schedules the single RenderGraph shadow/depth/color path, schedules vegetation preparation, records the compute preparation contract, imports the render-world buffers into RenderGraph, and exposes `VegetationShadowMode.Off` / `CheapTree`.
- `SubSceneAuthoring` / `Vegetation.SubScene` - closed-`SubScene` compiled provider hub: bakes generated assembly/page object references and registers/unregisters one render-world provider when the SubScene loads/unloads.
- `FoliageAssemblyAsset` / `FoliagePageAsset` / `FoliageRepresentationPacket` - active compiled packet contract: generated assets that own mesh/material/pass groups, precomputed page/cell/tree records, always-resident PageHLOD/CellHLOD instance packets, near-detail `TreeL0/L1/L2` packets, compiled shadow packet modes, wind metadata, static packet instance ranges, and compiler build reports.
- `FoliageCompiledAssetCompiler` - editor compiler authority: validates compile-required inputs once per unique blueprint/prototype, precomputes static branch placement templates, tree/cell bounds, HLOD instance packets that use `TreeBlueprintSO.impostorMesh` / `impostorMaterial`, pass indices, memory/command upper bounds, shadow bounds, near-detail byte caps, and hard cap failures. It must not emit generated aggregate HLOD mesh assets or replay branch/trunk L3 packets for far density. Authoring budget and tier-monotonicity rules stay in the inspector and must not block page/assembly compilation.
- `Packages/com.voxgeofol.vegetation/README.md` - public runtime contract summary: setup, compiled page asset flow, render-world lifecycle, grouped packet submission, and limitations
- `VegetationAuthoringValidator` - authoring contract authority: explicit validation for readability, opacity, budgets, bounds, scale, 5-band LOD ordering, baked impostor mesh/material, trunk simplification mesh, and branch split canopy/wood tier monotonicity and bounds.
- `CanopyShellGenerator` - editor-side branch tier authority: builds temporary voxel hierarchies, applies optional reduction and mesh-only fallback, and persists only `branchL1/2/3CanopyMesh` plus `branchL1/2/3WoodMesh`
- `TrunkL3MeshGenerator` - editor-side tree trunk authority: bakes `trunkL3Mesh` from `trunkMesh`, clips every candidate back to the original `trunkMesh.bounds`, and persists the latest generated result even when validation still blocks the asset
- `MeshVoxelizerHierarchyBuilder` - shared hierarchy authority: backed by `CPUVoxelizer` volumes, splits canonical `L0` surface voxels into octant nodes, then derives separate compact `L1/L2` hierarchies from that owned occupancy
- `CPUVoxelizer` / `CpuVoxelSurfaceMeshBuilder` - shared CPU voxel backend authority: builds indexed voxel volumes and bounded surface-only meshes, including the optional coplanar-face merge path now used by canopy, wood, and trunk generation
- `GeneratedMeshSimplificationUtility` - editor-side simplification authority: selects the best generated mesh candidate, runs bounded voxel-resolution retries, rebuilds Unity `MeshLodUtility` input meshes with explicit `SetTriangles` index buffers before fallback generation, still skips unsupported meshes safely, and uses Unity `MeshLodUtility` as the last-resort fallback for non-blocking baking
- `GeneratedMeshAssetUtility` - editor-side Phase B asset persistence authority: writes generated shell and trunk meshes as explicit `.mesh` files into writable project asset folders beside the owner asset when possible, while honoring explicit relative-folder overrides from the authoring asset
- `CheckVoxelMeshSimplification` / `CheckUnityMeshLodGenerations` - repo-local manual comparison demos under `Assets/Scripts`: let developers compare source meshes, raw voxel surfaces, reduced voxel surfaces, and Unity `MeshLodUtility` output on existing meshes without touching package bake data
- `VegetationTreeAuthoringEditorUtility` - editor-side Phase C authority: bake entry points, aggregated validation, and `L0/L1/L2/L3` authoring summary for `VegetationTreeAuthoring`
- `VegetationTreeAuthoringEditorPanel` - editor-side Phase C.5 gate surface: shows the blocker summary for missing `trunkL3Mesh`, invalid trunk bounds/reduction, and missing or invalid branch split tiers before runtime MVP work proceeds
- `VegetationEditorPreview` - editor-side Phase C preview authority: rebuilds transient branch-root hierarchies for runtime `L0/L1/L2/L3` using only source meshes or the persisted split-tier canopy/wood meshes
- `VegetationTreeAuthoringEditor` - editor integration: inspector-side preview controls, bake buttons, validation display, and window launcher

--

## Verification

- EditMode suite in [`Assets/EditorTests`](../Assets/EditorTests) is the primary behavioral safety net.
- Vegetation authoring coverage currently starts in [`Packages/com.voxgeofol.vegetation/Tests/Editor`](../Packages/com.voxgeofol.vegetation/Tests/Editor).
- `CI/CITestOutput.xml` is authoritative for test results.
- `CI/CompileErrorsAfterUnityRun.txt` is authoritative for Unity and Burst compile errors.
- Use `Fully Compile by Unity` when files were added, removed, or renamed.
- Use the Rider MSBuild compile path for quick feedback only.

--
