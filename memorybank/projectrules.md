# Project Specific Rules

Purpose: compact cross-module rules, runtime authorities, and wiring hubs.

## Global Rules

1. Budgeting and optimization on anything asset related. By settings: cutoff and hard budgets, pooling, level of detail, trimming, full occlusion, and incremental per-batch/per-frame processing.
2. OOP controllers are request-driven or consume-driven only; they do not mutate gameplay state directly.
3. Never use direct C# events with ECS.
4. Never store mutable runtime data in ScriptableObjects.
5. Do not rely on Unity lifecycle callbacks and must use explicit runtime APIs in EditMode tests.
6. Prefer structs where applicable, but do not use struct types as hot dictionary keys when avoidable.
7. No hidden assumptions: missing setup must throw explicit errors.
8. All integration boundaries need concise `[INTEGRATION]` summaries.
9. All static runtime stores must have deterministic `Reset` coverage through `StaticServicesReset`.
10. OOP communications to ECS happen through transient request entities, either queued directly by helpers or created by loop-owned bus ingress.
11. MVC separation for UI remains in effect.
12. Prefer `Refresh` and `Simulate` naming over `Update` for explicit runtime APIs.
13. Keep authoring, runtime ECS data, and Unity binding logic separate. 
14. Separate what should done at edit-mode and runtime.
15. Hot paths should be allocation-aware and follow DRY/SingleResponsibility best practices.
16. Reusable public-facing features should live in embedded packages under `Packages/`; repo-local-only features can remain under `Assets/Scripts/Features`.
17. No useless maintenance. Scripts are either completely dropped or fully migrated to new api, no obsolete wrappers!
18. No useless abstractions, no bloatware.
19. Avoid constant asset creation and editor refresh in tests! It makes test run multiple times slower!
20. Avoid constant asset creation if full algorithm is not finished! Create finished asset once but do not refresh editor constantly! Only do `AssetDatabase.SaveAssets()` once all meshes are created. All operations with `AssetDatabase` is generally very-very long!

--

## Vegetation System Rules

1. All vegetation geometry is opaque-only - no transparency, no alpha clip, no masked materials.
2. Trees are assembled from reusable branch prototypes, not single monolithic meshes.
3. Canopy shells are hierarchical branch-local voxelized meshes: `BranchPrototypeSO.shellNodesL0` stores the canonical authored L0 bounding ownership tree, while `shellNodesL1` and `shellNodesL2` store compact runtime-authored tiers rebuilt from owned `L0` occupancy at their authored resolutions. Runtime meaning is shifted: authored `shellNodesL0/L1/L2` map to runtime `L1/L2/L3`.
4. Expanded-tree rendering keeps branch structure: runtime `L0` uses source branch geometry at branch-placement granularity, while preserving backside simplification rule, while runtime `L1/L2/L3` emit exact surviving hierarchy frontiers. Runtime `L2/L3` use baked simplified branch wood and trunk beside the shell frontier.
5. All editor-baked voxel artifacts must stay inside authoritative source occupancy; canopy shell node `localBounds` persist authored octant ownership bounds, while emitted shell meshes, voxelized branch wood attachments, simplified `L2/L3` trunk-mesh `trunkL3Mesh`, and far-mesh `impostorMesh` must stay inside their authoritative source bounds.
6. Generated shell, trunk, and far meshes prefer topology-preserving simplification first: merge adjacent coplanar voxel faces when possible, keep voxel silhouette/bounds, and only then enter bounded fallback when authored settings still miss budgets.
7. Shell, trunk, and far-mesh simplification must remain optional through authoring settings so developers can inspect raw voxel output in the editor and tests can stay on the fast path.
8. `impostorMesh` is a coarse opaque far mesh, not a billboard and not a textured card. Runtime `Impostor` mode submits that one mesh only, and bake may optionally reduce it to a front-side-only surface. Separate material for imposter exists to make sure it always rotated to the viewer (so backside geometry can be culled in baking)
9. GPU runtime work is staged: GPU cell visibility, tree classification, branch tier selection, hierarchy survival decisions, GPU-resident frontier emission, and indirect draw submission. Current shipped Phase E runtime path is `GpuResident` only. Legacy CPU/decode runtime rendering was removed from `Runtime/Rendering` and must not be reintroduced into the production container path.
10. Current Phase D shell-node survival is conservative and explicit: if a shell node is outside the frustum it is `Reject`; if it is visible and has children it is `ExpandChildren`; only visible leaves are `EmitSelf`. Intra-tier screen-size collapse is deferred, not hidden.
11. `Graphics.RenderMeshIndirect` via the vegetation renderer feature is the rendering backend; no `BatchRendererGroup` and no MaterialPropertyBlock usage. The system does not promise one literal global draw call; it submits multiple indirect calls grouped by exact mesh/material draw slots. After MVP - try disable manual `Graphics.RenderMeshIndirect` and use experimental unity package `virtualmesh` (https://github.com/Unity-Technologies/com.unity.virtualmesh).
12. Per-instance color variation via `RSUV` only (packed uint) - no MaterialPropertyBlock, no DOTS-instanced properties for color. Single variation mechanism preserving SRP batching.
13. Branch scale is in steps of 0.25 (e.g. 0.25, 0.5, 0.75, 1.0, 1.25...); no scale quantization optimization yet.
14. Spatial partitioning via uniform grid; GPU is the primary MVP visibility path. CPU frustum test plus optional `CullingGroup` is temporary fallback only when the GPU path is unavailable or explicitly disabled.
15. Authoring data lives in ScriptableObjects; runtime data lives in GPU buffers or explicit runtime caches; no runtime data on MonoBehaviours beyond orchestration/wiring.
16. Editor preview is transient child GameObjects with `HideFlags.DontSave | HideFlags.NotEditable` - never serialized.
17. Shell generation, trunk-L3 generation, and far-mesh baking are editor-only operations.
18. Generated shell, trunk, and far geometry must be persisted as standalone `.mesh` assets under a writable project folder: prefer an owner-local `GeneratedMeshes/` folder under `Assets/`, otherwise fall back to `Assets/VoxGeoFol.Generated/Vegetation/Meshes/`. Do not rely on transient meshes or sub-assets that can be lost.
19. All vegetation code lives under `Packages/com.voxgeofol.vegetation/` with `Runtime/Authoring`, `Editor`, `Runtime/Shaders`, `Runtime/Rendering`, `Tests/Editor`, and `Samples~/` subfolders as needed.
20. No Unity `LODGroup` - LOD selection is fully owned by authored distance bands `l0Distance/l1Distance/l2Distance/impostorDistance/absoluteCullDistance` plus GPU classification/expansion. `l3Distance` is removed from the current authoring contract. `LODGroup` is incompatible with BRG or indirect hierarchy-driven rendering.
21. Canopy and far-mesh shaders are minimal vertex-lit: no albedo texture for shell output, no normal map, no emission, no specular, no bump maps. Trunk shader uses albedo texture but no normal map.
22. Trunk stays full for runtime `L0` and `L1`; runtime `L2` and `L3` use simplified `trunkL3Mesh`; far trees use `impostorMesh` only.
23. MVP runtime hierarchy flattening is BFS with contiguous immediate-child blocks defined by `firstChildIndex + childMask`; after MVP switch to DFS preorder with subtree spans.
24. Generated meshes that miss budgets still persist and remain wired, but validation marks the owning asset invalid.
25. `VegetationRuntimeContainer` registration is a frozen runtime snapshot. After container enable, transform edits on registered `VegetationTreeAuthoring` objects and other registration-affecting scene or authoring changes are not live-synced; explicit `RefreshRuntimeRegistration()` is required.
26. Each `VegetationRuntimeContainer` owns only active `VegetationTreeAuthoring` references from its serialized list, and every referenced authoring must stay inside that container hierarchy. Nested child containers claim their own descendants when editor fill tooling rebuilds the list, so streaming/addressable chunks must be structured by container hierarchy instead of scene-global discovery.
27. `VegetationRuntimeContainer` has no production CPU fallback. Missing compute support, missing `VegetationClassify.compute`, or shader-import failures are hard blockers that must fail explicitly.
28. Production `VegetationRuntimeContainer` flow does not expose exact CPU-visible instance mirrors; available runtime diagnostics are profiler markers plus conservative uploaded indirect-batch snapshots with unknown exact per-slot instance counts.
29. Runtime diagnostics ownership is renderer-feature scoped, not container scoped. `VegetationFoliageFeatureSettings.EnableDiagnostics` affects every active `VegetationRuntimeContainer` rendered by that feature.

## Wiring Hubs

- `VegetationRuntimeContainer` - runtime orchestration hub: validates and consumes its explicit serialized `VegetationTreeAuthoring` list, freezes the runtime registry, maintains runtime tree indices, prepares one GPU-resident per-camera frame, and binds the latest indirect draw resources into `VegetationIndirectRenderer`; nested child containers own their own descendants and current contract is explicit snapshot registration, not live transform or content sync
- `VegetationRuntimeRegistryBuilder` / `VegetationRuntimeRegistry` - Phase D contract authority: flatten authored tree/blueprint/placement/prototype payloads, build exact draw-slot registries, allocate per-branch node-decision slices, and expose the stable per-slot handoff surface for Phase E
- `VegetationSpatialGrid` - Phase D spatial partition authority: deterministic tree-to-cell registration by tree-sphere center, conservative cell resident bounds, and visible-cell query output
- `VegetationGpuDecisionPipeline` - Phase D/Phase E GPU authority: uploads the frozen registry into compute buffers and emits GPU-resident indirect instance payloads plus indirect args directly into shared draw buffers; environments that import the shader without kernels must fail explicitly instead of silently faking GPU success
- `VegetationRendererFeature` - URP integration: stores the shared `VegetationClassify.compute` reference and diagnostics toggle in `VegetationFoliageFeatureSettings`, schedules the indirect vegetation depth/color passes, and consumes prepared `VegetationRuntimeContainer` output; it does not own classification or decode rules
- `VegetationIndirectRenderer` - indirect rendering authority: runtime material copies, shared GPU-resident instance/args buffers, conservative uploaded-batch snapshots for diagnostics, and `RenderMeshIndirect` submission
- `VegetationAuthoringValidator` - Task 1/C.5 authoring contract authority: explicit validation for readability, opacity, budgets, bounds, scale, 5-band LOD ordering, trunk-L3 reduction/bounds, and BFS child order in ascending octant-bit order
- `CanopyShellGenerator` - editor-side branch-shell authority: bakes canonical `shellNodesL0`, derives compact `shellNodesL1` / `shellNodesL2` from owned `L0` occupancy, applies optional reduction and mesh-only fallback, and refreshes bounded voxelized `shellL1WoodMesh` / `shellL2WoodMesh`
- `TrunkL3MeshGenerator` - editor-side tree trunk authority: bakes `trunkL3Mesh` from `trunkMesh`, clips every candidate back to the original `trunkMesh.bounds`, and persists the latest generated result even when validation still blocks the asset
- `MeshVoxelizerHierarchyBuilder` / `MeshVoxelizerHierarchyDemo` - shared hierarchy authority and manual inspection utility: backed by `CPUVoxelizer` volumes, splits canonical `L0` surface voxels into octant nodes, then derives separate compact `L1/L2` hierarchies from that owned occupancy
- `CPUVoxelizer` / `CpuVoxelSurfaceMeshBuilder` - shared CPU voxel backend authority: builds indexed voxel volumes and bounded surface-only meshes, including the optional coplanar-face merge path now used by canopy, wood, trunk-L3, and far-mesh generation
- `GeneratedMeshSimplificationUtility` - editor-side simplification authority: selects the best generated mesh candidate, runs bounded voxel-resolution retries, rebuilds Unity `MeshLodUtility` input meshes with explicit `SetTriangles` index buffers before fallback generation, still skips unsupported meshes safely, and uses Unity `MeshLodUtility` as the last-resort fallback for non-blocking baking
- `ImpostorMeshGenerator` - editor-side tree far-mesh authority: merges trunk + original placed branch `woodMesh`/`foliageMesh` in tree space, uses the same reduction/fallback path, and stores `impostorMesh` without requiring baked canopy shells
- `GeneratedMeshAssetUtility` - editor-side Phase B asset persistence authority: writes generated shell, trunk, and far meshes as explicit `.mesh` files into writable project asset folders beside the owner asset when possible, while honoring explicit relative-folder overrides from the authoring asset
- `CheckVoxelMeshSimplification` / `CheckUnityMeshLodGenerations` - repo-local manual comparison demos under `Assets/Scripts`: let developers compare source meshes, raw voxel surfaces, reduced voxel surfaces, and Unity `MeshLodUtility` output on existing meshes without touching package bake data
- `VegetationTreeAuthoringEditorUtility` - editor-side Phase C authority: bake entry points, aggregated validation, and `L0/L1/L2/L3/Impostor` authoring summary for `VegetationTreeAuthoring`
- `VegetationTreeAuthoringEditorPanel` - editor-side Phase C.5 gate surface: shows the blocker summary for missing `trunkL3Mesh`, invalid trunk bounds/reduction, and BFS octant-order failures before runtime MVP work proceeds
- `VegetationEditorPreview` - editor-side Phase C preview authority: rebuilds transient branch-root hierarchies for runtime `L0/L1/L2/L3/Impostor` plus shell-only inspection states and selects `shellNodesL0`, `shellNodesL1`, or `shellNodesL2` directly based on the active shell tier
- `VegetationTreeAuthoringEditor` - editor integration: inspector-side preview controls, bake buttons, validation display, and window launcher
- `VegetationTreeAuthoringWindow` - editor integration: dedicated window for driving the same preview and bake utilities outside the Inspector

--

## Verification

- EditMode suite in [`Assets/EditorTests`](../Assets/EditorTests) is the primary behavioral safety net.
- Vegetation authoring coverage currently starts in [`Packages/com.voxgeofol.vegetation/Tests/Editor`](../Packages/com.voxgeofol.vegetation/Tests/Editor).
- `CI/CITestOutput.xml` is authoritative for test results.
- `CI/CompileErrorsAfterUnityRun.txt` is authoritative for Unity compile errors.
- Use `Fully Compile by Unity` when files were added, removed, or renamed.
- Use the Rider MSBuild compile path for quick feedback only.

--
