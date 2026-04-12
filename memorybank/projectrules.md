# Project Specific Rules

Purpose: compact cross-module rules, runtime authorities, and wiring hubs.

## Global Rules

1. Budgeting and optimization on anything asset related. By settings: cutoff and hard budgets, pooling, level of detail, trimming, full occlusion, and incremental per-batch/per-frame processing.
2. OOP controllers are request-driven or consume-driven only; they do not mutate gameplay state directly.
3. Never use direct C# events with ECS.
4. Never store mutable runtime data in ScriptableObjects.
5. Do not rely on Unity lifecycle callbacks and must use explicit runtime APIs in EditMode tests.
6. Prefer structs where applicable, but do not use struct types as hot dictionary keys when avoidable (IL2cpp generic exponential growth issue with value types in generics).
7. No hidden assumptions: missing setup must throw explicit errors.
8. All integration boundaries need concise `[INTEGRATION]` summaries.
9. All static runtime stores must have deterministic `Reset` coverage through `StaticServicesReset`.
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
3. Canopy shell hierarchies are editor/bake data now. `BranchPrototypeSO.shellNodesL0/L1/L2` still exist for preview and shell inspection, but the urgent production runtime must not depend on shell-node buffers, BFS metadata, or shell-frontier traversal.
4. Expanded-tree rendering keeps branch structure without BFS: runtime `L0` uses source branch geometry at branch-placement granularity, runtime `L1/L2/L3` use separate baked canopy and wood meshes per branch tier, `TreeL3` is the mandatory whole-tree non-far floor, and `Impostor` stays far-only.
5. All editor-baked voxel artifacts must stay inside authoritative source occupancy; canopy shell node `localBounds` persist authored octant ownership bounds, while emitted shell meshes, voxelized branch wood attachments, simplified `L2/L3` trunk-mesh `trunkL3Mesh`, and far-mesh `impostorMesh` must stay inside their authoritative source bounds.
6. Generated shell, trunk, and far meshes prefer topology-preserving simplification first: merge adjacent coplanar voxel faces, keep voxel silhouette/bounds, and only then enter bounded fallback when authored settings still miss budgets.
7. Shell, trunk, and far-mesh simplification must remain optional through authoring settings so developers can inspect raw voxel output in the editor and tests can stay on the fast path.
8. `impostorMesh` is a coarse opaque far mesh, not a billboard and not a textured card. Runtime `Impostor` mode submits that one mesh only, and bake may optionally reduce it to a front-side-only surface. Separate material for imposter exists to make sure it always rotated to the viewer (so backside geometry can be culled in baking)
9. GPU runtime work is staged: GPU cell visibility, tree visibility classification, mandatory `TreeL3` acceptance, nearest-first promotion into `L2/L1/L0`, promoted-tree-only compact branch work generation, indirect count/pack, and non-zero-slot submission. Current runtime path is `GpuResident` only. Legacy CPU/decode runtime rendering was removed from `Runtime/Rendering` and must not be reintroduced into the production container path.
10. `Graphics.RenderMeshIndirect` via the vegetation renderer feature is the rendering backend; no `BatchRendererGroup` and no MaterialPropertyBlock usage. A draw slot is one exact `Mesh + Material + MaterialKind` registry identity. One registered draw slot owns one slot index and one indirect-args record. After Production ready package - try use experimental unity package `virtualmesh` (https://github.com/Unity-Technologies/com.unity.virtualmesh) to see if we are compatible with it.
11. Per-instance color variation via `RSUV` only (packed uint) - no MaterialPropertyBlock, no DOTS-instanced properties for color. Single variation mechanism preserving SRP batching.
12. Branch scale is in steps of 0.25 (e.g. 0.25, 0.5, 0.75, 1.0, 1.25...); no scale quantization optimization yet.
13. Spatial partitioning via uniform grid; GPU is the primary visibility path. CPU frustum test plus optional `CullingGroup` is temporary fallback only when the GPU path is unavailable or explicitly disabled. Dense-forest urgent work must follow `DetailedDocs/urgentRedesign.md`: accepted-content prioritization first, occlusion later only if telemetry proves it is still worth the complexity.
14. Authoring data lives in ScriptableObjects; runtime data lives in GPU buffers or explicit runtime caches; no runtime data on MonoBehaviours beyond orchestration/wiring.
15. Editor preview is transient child GameObjects with `HideFlags.DontSave | HideFlags.NotEditable` - never serialized.
16. Shell generation,branch and trunk-L3 generation, and far-mesh baking are editor-only operations.
17. Generated shell, trunk, and far geometry must be persisted as standalone `.mesh` assets under a writable project folder: prefer an owner-local `GeneratedMeshes/` folder under `Assets/`, otherwise fall back to `Assets/VoxGeoFol.Generated/Vegetation/Meshes/`. Do not rely on transient meshes or sub-assets that can be lost.
18. All vegetation code lives under `Packages/com.voxgeofol.vegetation/` with `Runtime/Authoring`, `Editor`, `Runtime/Shaders`, `Runtime/Rendering`, `Tests/Editor`, and `Samples~/` subfolders as needed.
19. No Unity `LODGroup` - LOD selection is fully owned by authored distance bands `l0Distance/l1Distance/l2Distance/impostorDistance/absoluteCullDistance` plus GPU classification/expansion. `l3Distance` is removed from the current authoring contract. `LODGroup` is incompatible with BRG or indirect hierarchy-driven rendering.
20. Canopy and far-mesh shaders are minimal vertex-lit: no albedo texture for shell output, no normal map, no emission, no specular, no bump maps. Trunk shader uses albedo texture but no normal map.
21. Trunk stays full for runtime `L0` and `L1`; runtime `L2` uses simplified `trunkL3Mesh`; non-promoted visible trees inside the non-far bands use `treeL3Mesh`; far trees use `impostorMesh` only.
22. Generated meshes that miss budgets still persist and remain wired, but validation marks the owning asset invalid.
23. `VegetationRuntimeContainer` registration is a frozen runtime snapshot. After container enable, transform edits on registered `VegetationTreeAuthoring` objects and other registration-affecting scene or authoring changes (position/rotation/scale) are not live-synced; explicit `RefreshRuntimeRegistration()` is required.
24. Each `VegetationRuntimeContainer` owns only active `VegetationTreeAuthoring` references from its serialized list, and every referenced authoring must stay inside that container hierarchy. Nested child containers claim their own descendants when editor fill tooling rebuilds the list, so streaming/addressable chunks must be structured by container hierarchy instead of scene-global discovery.
25. `VegetationRuntimeContainer` has no CPU fallback. Missing compute support, missing `VegetationClassify.compute`, or shader-import failures are hard blockers that must fail explicitly.
26. `AuthoringContainerRuntime` is the single authoritative runtime owner in all modes. `VegetationRuntimeContainer` is only the classic-scene lifecycle provider, and closed `SubScene` support must flow through `SubSceneAuthoring` plus the separate `Vegetation.SubScene` asmdef.
27. Shared registration input is `VegetationTreeAuthoringRuntime`, not live `VegetationTreeAuthoring`. Runtime registration and renderer discovery must not regain direct `MonoBehaviour` dependencies.
28. Closed `SubScene` support must not pull DOTS into the main `Vegetation` asmdef. Bakers, baked components, and bootstrap systems live in `Vegetation.SubScene`; `AuthoringContainerRuntime`, `VegetationTreeAuthoringRuntime`, and renderer integration stay in the main package assembly.
29. Runtime registration must not pre-populate `SceneBranches[]`, duplicate per-scene shell-node world bounds, or upload prototype shell-node buffers for the production path. Persisted shell hierarchies live once per prototype for editor-only shell preview; runtime branch bounds are derived on demand from tree transforms plus reusable blueprint placements.
30. Runtime visible-instance memory must stay hard-bounded.
31. Container capacity is not a global scene coordinator. Multiple active runtime owners each own independent packed instance buffers, indirect args, and GPU pipeline state. Splitting a forest across containers is allowed and is the intended chunking workaround when one container would overflow, but total memory and visible capacity then scale with the number of visible containers.
32. All runtime-review telemetry must stay behind `VegetationFoliageFeatureSettings.EnableDiagnostics`.
33. Runtime rendering resource owners must be exception-safe on partial construction. If material copies, compute buffers, or graphics buffers are allocated and later setup fails, the constructor path must release everything already created before rethrowing. Diagnostics in render prep/submission must not allocate in the steady-state loop; deduplicate on scalar state before building strings.
34. Current runtime material resolution is a known limitation, not a stable public extension point: `VegetationIndirectMaterialFactory` still rewrites draw slots to package shader names. Milestone 2 must replace this with an explicit compatible-material contract or binding path for project-local custom materials.
35. Hierarchical wind is not shipped yet. When it lands, it must stay tier-consistent across `L0/L1/L2/L3/Impostor` and be part of the same public compatible-material contract instead of a package-only hidden shader path.

## Wiring Hubs

- `AuthoringContainerRuntime` - runtime ownership hub: owns runtime registration, runtime tree indices, GPU classification pipeline lifecycle, indirect renderer lifecycle, per-camera preparation, and disposal for both classic-scene and closed-`SubScene` flows
- `VegetationActiveAuthoringContainerRuntimes` - runtime-owner discovery hub: enforces one active runtime owner per deterministic container id, applies provider precedence (`ClassicScene` over `SubScene`), and is the only renderer discovery surface
- `VegetationRuntimeContainer` - classic-scene provider hub: owns serialized settings and `VegetationTreeAuthoring` references, converts them into shared `VegetationTreeAuthoringRuntime` records, and creates/disposes one `AuthoringContainerRuntime` on enable/disable or explicit refresh; nested child containers still own their own descendants
- `SubSceneAuthoring` / `Vegetation.SubScene` - closed-`SubScene` provider hub: bakes the same runtime-safe tree/container payload from `VegetationRuntimeContainer` data and bootstraps one `AuthoringContainerRuntime` when the baked entity loads
- `VegetationRuntimeRegistryBuilder` / `VegetationRuntimeRegistry` - urgent runtime contract authority: tree-first acceptance on `TreeInstances[]`, reusable `TreeBlueprints[]` and `BlueprintBranchPlacements[]`, compact branch prototype tier meshes with separate canopy/wood per level, `SpatialGrid` for visibility, and `DrawSlots[]` for final submission identity. The urgent path must not pre-populate `SceneBranches[]` or runtime shell-node buffers.
- `VegetationSpatialGrid` - Phase D spatial partition authority: deterministic tree-to-cell registration by tree-sphere center, conservative cell resident bounds, and visible-cell query output
- `VegetationGpuDecisionPipeline` - urgent GPU authority: uploads the frozen registry into compute buffers, owns per-frame tree visibility/acceptance state, generates compact promoted-tree branch worklists, counts and packs visible instances into one hard-bounded shared instance buffer per container, and emits indirect args directly; environments that import the shader without kernels must fail explicitly instead of silently faking GPU success
- `Packages/com.voxgeofol.vegetation/README.md` - public runtime contract summary: setup, runtime terminology, and current shipped lifecycle from container-owned authorings through runtime registry flattening, draw slots, GPU emission, and final URP indirect submissions
- `VegetationRendererFeature` - URP integration: stores the shared `VegetationClassify.compute` reference and diagnostics toggle in `VegetationFoliageFeatureSettings`, schedules the indirect vegetation depth/color passes, and consumes prepared `AuthoringContainerRuntime` output from the shared runtime-owner registry; it does not own classification or decode rules
- `VegetationIndirectRenderer` - indirect rendering authority: runtime material copies, shared GPU-resident packed instance/args/slot-start buffers, non-zero-slot compaction, conservative uploaded-batch snapshots for diagnostics, and `RenderMeshIndirect` submission
- `VegetationIndirectMaterialFactory` - current temporary material rewrite helper: rebuilds runtime materials from package shader names; treat this as a Milestone 2 limitation to remove from the public material ownership contract
- `VegetationAuthoringValidator` - authoring contract authority: explicit validation for readability, opacity, budgets, bounds, scale, 5-band LOD ordering, `treeL3Mesh`, branch split canopy/wood tiers, trunk-L3 reduction/bounds, and shell-hierarchy topology for editor-side shell tooling
- `CanopyShellGenerator` - editor-side branch-shell authority: bakes canonical `shellNodesL0`, derives compact `shellNodesL1` / `shellNodesL2` from owned `L0` occupancy, applies optional reduction and mesh-only fallback, and refreshes bounded voxelized `shellL1WoodMesh` / `shellL2WoodMesh`
- `TrunkL3MeshGenerator` - editor-side tree trunk authority: bakes `trunkL3Mesh` from `trunkMesh`, clips every candidate back to the original `trunkMesh.bounds`, and persists the latest generated result even when validation still blocks the asset
- `MeshVoxelizerHierarchyBuilder` / `MeshVoxelizerHierarchyDemo` - shared hierarchy authority and manual inspection utility: backed by `CPUVoxelizer` volumes, splits canonical `L0` surface voxels into octant nodes, then derives separate compact `L1/L2` hierarchies from that owned occupancy
- `CPUVoxelizer` / `CpuVoxelSurfaceMeshBuilder` - shared CPU voxel backend authority: builds indexed voxel volumes and bounded surface-only meshes, including the optional coplanar-face merge path now used by canopy, wood, trunk-L3, and far-mesh generation
- `GeneratedMeshSimplificationUtility` - editor-side simplification authority: selects the best generated mesh candidate, runs bounded voxel-resolution retries, rebuilds Unity `MeshLodUtility` input meshes with explicit `SetTriangles` index buffers before fallback generation, still skips unsupported meshes safely, and uses Unity `MeshLodUtility` as the last-resort fallback for non-blocking baking
- `ImpostorMeshGenerator` - editor-side tree far-mesh authority: merges trunk + original placed branch `woodMesh`/`foliageMesh` in tree space, uses the same reduction/fallback path, and stores `impostorMesh` without requiring baked canopy shells
- `GeneratedMeshAssetUtility` - editor-side Phase B asset persistence authority: writes generated shell, trunk, and far meshes as explicit `.mesh` files into writable project asset folders beside the owner asset when possible, while honoring explicit relative-folder overrides from the authoring asset
- `CheckVoxelMeshSimplification` / `CheckUnityMeshLodGenerations` - repo-local manual comparison demos under `Assets/Scripts`: let developers compare source meshes, raw voxel surfaces, reduced voxel surfaces, and Unity `MeshLodUtility` output on existing meshes without touching package bake data
- `VegetationTreeAuthoringEditorUtility` - editor-side Phase C authority: bake entry points, aggregated validation, and `L0/L1/L2/L3/Impostor` authoring summary for `VegetationTreeAuthoring`
- `VegetationTreeAuthoringEditorPanel` - editor-side Phase C.5 gate surface: shows the blocker summary for missing `trunkL3Mesh`, invalid trunk bounds/reduction, and branch-shell BFS octant-order failures before runtime MVP work proceeds
- `VegetationEditorPreview` - editor-side Phase C preview authority: rebuilds transient branch-root hierarchies for runtime `L0/L1/L2/L3/Impostor` plus shell-only inspection states and selects `shellNodesL0`, `shellNodesL1`, or `shellNodesL2` directly based on the active shell tier
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
