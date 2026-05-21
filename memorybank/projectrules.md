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

1. All vegetation geometry is opaque-only - no transparency, no alpha clip, no masked materials.
2. Trees are assembled from reusable branch prototypes, not single monolithic meshes.
3. Temporary canopy voxel hierarchies are bake-internal only. `BranchPrototypeSO` and the runtime contract must persist only the split-tier mesh chain: `branchL1/2/3CanopyMesh` plus `branchL1/2/3WoodMesh`.
4. Compiled packet rendering is the production direction: runtime `L0` uses source branch geometry at branch-placement granularity, runtime `L1/L2` use separate baked canopy and wood meshes per branch tier, and far density is represented by compiled `CellHLOD` / `PageHLOD` packets that collapse each tree to its baked `impostorMesh` instance.
5. All editor-baked voxel artifacts must stay inside authoritative source occupancy; emitted canopy tier meshes, generated branch wood attachments, and simplified trunk mesh `trunkL3Mesh` must stay inside their authoritative source bounds.
6. Generated shell and trunk meshes prefer topology-preserving simplification first: merge adjacent coplanar voxel faces, keep voxel silhouette/bounds, and only then enter bounded fallback when authored settings still miss budgets.
7. Shell and trunk simplification must remain optional through authoring settings so developers can inspect raw voxel output in the editor and tests can stay on the fast path.
8. Production runtime consumes compiled pages through `VegetationRenderWorld`: page/cell broad phase, packet selection, active budgets, wind binding, and grouped indirect submission. The retired tree-first GPU count/emit path was deleted and must not be recreated.
9. URP vegetation feature hard rule: never put synchronous GPU readback (`ComputeBuffer.GetData`, `GraphicsBuffer.GetData`, blocking async-readback waits) into render-pass setup, per-camera prepare, per-frustum prepare, or submission callbacks. Submission compaction and telemetry must stay GPU-side, async, or explicitly offline-only.
10. URP vegetation feature hard rule: never let multiple URP consumers overwrite one shared mutable resident frame in the same render cycle. Camera depth/color can reuse the same prepared camera frame when frame/settings match; main-light shadow cascades must be prepared once as a batched cascade-frustum set and rendered into each cascade from that shared packet frame.
11. Packet acceptance hard rule: never reintroduce single-thread O(N^2) nearest-tree admission or runtime branch-work generation. Runtime chooses compiled packets; it does not generate branch work.
12. `Graphics.RenderMeshIndirect` / `DrawMeshInstancedIndirect` through the vegetation renderer feature is the current backend. Submission is grouped by compiled `FoliageAssetGroup` (`Mesh + Material + pass contract`). `MaterialPropertyBlock` is allowed only for per-draw binding of the shared compiled instance buffer, explicit grouped instance-buffer base offset, and global wind constants; do not use it for per-instance variation. Indirect-args buffers must use `GraphicsBuffer.Target.IndirectArguments` only; do not combine it with `Raw` because DirectX rejects that target combination. Indirect-args metadata must preserve signed `BaseVertexLocation` correctly end-to-end and must keep `startInstance` zero; shaders must index `_VegetationInstanceData` through `_VegetationInstanceDataBaseOffset` instead of relying on backend base-instance behavior. BRG or `virtualmesh` experiments are post-production candidates over the same packet data, not alternate authoring/runtime policy.
13. Per-instance color variation via `RSUV` only (packed uint) - no MaterialPropertyBlock, no DOTS-instanced properties for color. Single variation mechanism preserving SRP batching.
14. Branch scale is in steps of 0.25 (e.g. 0.25, 0.5, 0.75, 1.0, 1.25...); no scale quantization optimization yet.
15. Runtime broad phase is page/cell only. `VegetationRenderWorld` uses `CullingGroup` for camera page/cell visibility and explicit page/cell cascade-frustum masks for batched main-light shadow selection. Do not feed per-tree spheres to `CullingGroup`, and do not prepare/repack shadow packets once per cascade. Empty shadow cascades/groups must be skipped using the prepared cascade mask. HZB remains a later accelerator only after packet renderer production validation.
16. Authoring data lives in ScriptableObjects; runtime data lives in GPU buffers or explicit runtime caches; no runtime data on MonoBehaviours beyond orchestration/wiring.
17. Editor preview is transient child GameObjects with `HideFlags.DontSave | HideFlags.NotEditable` - never serialized.
18. Shell generation and trunk simplification are editor-only operations.
19. Generated shell and trunk geometry must be persisted as standalone `.mesh` assets under a writable project folder: prefer an owner-local `GeneratedMeshes/` folder under `Assets/`, otherwise fall back to `Assets/VoxGeoFol.Generated/Vegetation/Meshes/`. Do not rely on transient meshes or sub-assets that can be lost.
20. All vegetation code lives under `Packages/com.voxgeofol.vegetation/` with `Runtime/Authoring`, `Editor`, `Runtime/Shaders`, `Runtime/Rendering`, `Tests/Editor`, and `Samples~/` subfolders as needed.
21. No Unity `LODGroup` - LOD selection is fully owned by authored distance bands `l0Distance/l1Distance/l2Distance/hlodDistance/absoluteCullDistance` plus compiled packet selection. `LODGroup` is incompatible with indirect hierarchy-driven rendering.
22. Canopy and HLOD shaders are minimal vertex-lit: no albedo texture for shell output, no normal map, no emission, no specular, no bump maps. Trunk shader uses albedo texture but no normal map.
23. The bundled package shaders must carry the package indirect-instance contract for forward, depth, shadow-caster, and shader wind usage. Runtime shadow support is main-light directional shadows only through `VegetationShadowMode.Off` and `VegetationShadowMode.CheapTree`. `Off` skips vegetation shadow-caster submission. `CheapTree` submits compiled shadow packets. Vegetation shadow injection must append into URP's main shadow atlas through a render-graph raster depth attachment with `AccessFlags.ReadWrite`; do not manually `SetRenderTarget` from an unsafe pass. Indirect vegetation shadow draws must explicitly bind the active cascade view/projection shader globals before each cascade draw and restore camera globals afterward. Use GPU-adjusted projection when writing shader globals; raw projection globals can flip the draw. Do not rely on URP's post-shadow camera state for indirect caster projection; that turns Game View/build shadows into camera-relative artifacts. No additional-light shadow atlas integration yet.
24. Trunk stays full for runtime `L0` and `L1`; runtime `L2` uses simplified trunk/canopy packet data; far density uses compiled `CellHLOD` / `PageHLOD` baked impostor instance packets, not generated aggregate mesh assets and not branch/trunk L3 replay.
25. Generated meshes that miss budgets still persist and remain wired, but validation marks the owning asset invalid.
26. `VegetationRuntimeContainer` runtime registration is compiled-page based. Transform edits, hierarchy edits, blueprint edits, placement edits, or generated-mesh edits require recompiling generated page assets, then `RefreshRuntimeRegistration()`.
27. Each `VegetationRuntimeContainer` owns only active `VegetationTreeAuthoring` references from its serialized list, and every referenced authoring must stay inside that container hierarchy. Nested child containers claim their own descendants when editor fill tooling rebuilds the list, so streaming/addressable chunks must be structured by container hierarchy instead of scene-global discovery.
28. `VegetationRuntimeContainer` has no CPU fallback. Classic-scene production rendering requires compiled assembly/page assets; missing compiled pages must log and unregister/skip rather than falling back to live authoring.
29. `VegetationRenderWorld` is the production runtime owner. `VegetationRuntimeContainer` and `SubSceneAuthoring` are compiled page providers only.
30. Compiler input may use `VegetationTreeAuthoringRuntime` as an editor-safe snapshot, but runtime registration and renderer discovery must not regain live-authoring or tree-first dependencies.
31. Closed `SubScene` support must not pull DOTS into the main `Vegetation` asmdef. Bakers, baked components, and bootstrap systems live in `Vegetation.SubScene` and register compiled page providers with `VegetationRenderWorld`.
32. Runtime registration must not pre-populate `SceneBranches[]`, duplicate per-scene temporary bake hierarchy data, upload temporary canopy hierarchy buffers, or regenerate branch placement work. Temporary canopy voxel hierarchies are bake-only.
33. Runtime budgets must stay hard-bounded and owned by `VegetationRenderWorld`: color work budget, shadow work budget, selected packet instances, grouped command capacity, cell-level near-detail resident bytes, and cell-level near-detail upload bytes.
34. Dense-scene budgeting is camera-distance ordered: `VegetationRenderWorld` must admit visible cells from nearest to farthest, try the best distance tier first, degrade through cheaper near-detail tiers, and only then use `CellHLOD` / `PageHLOD` baked impostor packets. Visible trees must not disappear just because near-detail work or instance budgets are exhausted.
35. Container capacity is not a budget owner. Multiple classic-scene containers register providers into one global render world and share one packet budget/submission surface.
36. All runtime-review telemetry must stay behind `VegetationFoliageFeatureSettings.EnableDiagnostics`.
37. Runtime rendering resource owners must be exception-safe on partial construction. If graphics buffers are allocated and later setup fails, the constructor path must release everything already created before rethrowing. Runtime should not manufacture per-slot material copies on the package-compatible path. Diagnostics in render prep/submission must not allocate in the steady-state loop; deduplicate on scalar state before building strings. `VegetationRenderWorld.Prepare` must keep profiler submarkers for graph refresh, broad phase, selection, upload layout, instance copy, args, and buffer upload so regressions are attributable.
38. Shader and indirect draw code must guard runtime-derived buffer indices before buffer reads or indirect-args writes. Invalid packet, asset-group, pass, or instance indices must drop work rather than issuing undefined GPU memory access.
39. Runtime material compatibility is an explicit shader contract: forward/depth/shadow passes must include the vegetation indirect instance layout, `_VegetationInstanceDataBaseOffset`, and wind constants. The `_VegetationInstanceData` structured buffer must be declared only for variants that have both Unity procedural instancing and `_VOXGEOFOL_INDIRECT_RENDERING`; regular MeshRenderer/editor-preview draws must not require a runtime packet buffer even if Unity selects an instancing variant. `VegetationRenderWorld` is the only owner that enables `_VOXGEOFOL_INDIRECT_RENDERING` around grouped indirect draws. Runtime must not manufacture per-slot material copies for compatibility.
40. Shader wind is shipped on the compiled render-world path. Runtime changes update global wind constants and local leaf-flutter constants; static phase/weight/anchor data comes from compiled `FoliageWindMetadata` and must not be rebuilt per frame. The compiler must use the owning tree bounds for wind phase and anchor height across trunk, branch, canopy, and HLOD packets; packet bounds remain for culling, not wind anchoring. Branch and canopy packets must keep full trunk-following bend weight. Shader trunk-following displacement must use one compiled tree phase plus global time. Compiled branch flutter metadata must not be added to shared wind-direction displacement; otherwise attached branch tops can sway with a larger envelope than the trunk. Canopy shaders may consume branch flutter metadata only as small local vertex flutter over the trunk-following position, with amplitude/speed/spatial variation controlled by `VegetationFoliageFeatureSettings`, and forward/depth/shadow passes must deform consistently.
41. Cutover 1/2 compiled assets are active classic-scene and closed SubScene renderer input, not a parallel path. `FoliageAssemblyAsset` and `FoliagePageAsset` are generated from current container authoring by editor tooling.

## Wiring Hubs

- `VegetationRenderWorld` - production classic-scene runtime hub: owns compiled providers, page/cell `CullingGroup` broad phase, explicit shadow-frustum broad phase, near-to-far packet selection with tier degradation, color/shadow work budgets, cell-level near-detail resident/upload byte budgets, grouped indirect args, shared instance buffer upload, shader wind constants, diagnostics counters, and resource reset.
- `VegetationRuntimeContainer` - classic-scene compiled provider hub: owns serialized `VegetationTreeAuthoring` references for editor compilation plus generated `FoliageAssemblyAsset` / `FoliagePageAsset[]` runtime references, exposes the inspector compile button, and registers/unregisters those pages with `VegetationRenderWorld` on enable/disable or explicit refresh.
- `VegetationRendererFeature` - URP integration: schedules compiled render-world shadow/depth/color passes, exposes `VegetationShadowMode.Off` / `CheapTree`, and does not discover active tree-first runtime owners.
- `SubSceneAuthoring` / `Vegetation.SubScene` - closed-`SubScene` compiled provider hub: bakes generated assembly/page object references and registers/unregisters one render-world provider when the SubScene loads/unloads.
- `FoliageAssemblyAsset` / `FoliagePageAsset` / `FoliageRepresentationPacket` - active compiled packet contract: generated assets that own mesh/material/pass groups, precomputed page/cell/tree records, always-resident PageHLOD/CellHLOD instance packets, near-detail `TreeL0/L1/L2` packets, compiled shadow packet modes, wind metadata, static packet instance ranges, and compiler build reports.
- `FoliageCompiledAssetCompiler` - editor compiler authority: validates compile-required inputs once per unique blueprint/prototype, precomputes static branch placement templates, tree/cell bounds, HLOD instance packets that use `TreeBlueprintSO.impostorMesh` / `impostorMaterial`, pass indices, memory/command upper bounds, shadow bounds, near-detail byte caps, and hard cap failures. It must not emit generated aggregate HLOD mesh assets or replay branch/trunk L3 packets for far density. Authoring budget and tier-monotonicity rules stay in the inspector and must not block page/assembly compilation. It is the only current upgrade path from live authoring to compiled page assets.
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
