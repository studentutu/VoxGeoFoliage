# Project Specific Rules

Purpose: compact cross-module rules, runtime authorities, and wiring hubs.

## Global Rules

1. Budgeting and optimization on anything asset related. By settings: cutoff and hard budgets, pooling, level of detail, trimming, full occlusion, and incremental per-batch/per-frame processing.
2. ECS core must not read `UnityEngine.Time`; tick and delta are passed explicitly.
3. ECS core must not use `UnityEngine.Random`; randomness must be injected explicitly if introduced later.
4. OOP controllers are request-driven or consume-driven only; they do not mutate gameplay state directly.
5. Never use direct C# events with ECS.
6. Never store mutable runtime data in ScriptableObjects.
7. Do not rely on Unity lifecycle callbacks and must use explicit runtime APIs in EditMode tests.
8. Prefer structs where applicable, but do not use struct types as hot dictionary keys when avoidable.
9. No hidden assumptions: missing setup must throw explicit errors.
10. All integration boundaries need concise `[INTEGRATION]` summaries.
11. All static runtime stores must have deterministic `Reset` coverage through `StaticServicesReset`.
12. OOP communications to ECS happen through transient request entities, either queued directly by helpers or created by loop-owned bus ingress.
13. MVC separation for UI remains in effect.
14. Prefer `Refresh` and `Simulate` naming over `Update` for explicit runtime APIs.
15. Keep authoring, runtime ECS data, and Unity binding logic separate. 
16. Separate what should done at edit-mode and runtime.
17. Hot paths should be allocation-aware and follow DRY/SingleResponsibility best practices.
18. Reusable public-facing features should live in embedded packages under `Packages/`; repo-local-only features can remain under `Assets/Scripts/Features`.
19. No useless maintenance. Scripts are either completely dropped or fully migrated to new api, no obsolete wrappers!
20. No useless abstractions, no bloatware.
21. Avoid constant asset creation and editor refresh in tests! It makes test run multiple times slower!
22. Avoid constant asset creation if full algorithm is not finished! Create finished asset once but do not refresh editor constantly! Only do AssetDatabase.SaveAssets() once all meshes are created. All operations with AssetDatabase is generally very-very long!

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
9. GPU runtime work is staged: GPU cell visibility, tree classification, branch tier selection, hierarchy survival decisions, and indirect draw submission. Current shipped Phase E still decodes the final frontier on CPU; CPU reference is the default prepared-frame source and the optional GPU readback bridge stays non-blocking and delayed.
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
25. `VegetationRuntimeManager` registration is a frozen runtime snapshot. After manager enable, transform edits on registered `VegetationTreeAuthoring` objects and other registration-affecting scene or authoring changes are not live-synced; explicit `RefreshRuntimeRegistration()` is required.
26. Current `GpuDecisionReadback` manager behavior stays non-blocking and delayed. CPU bootstrap while readback is pending is intentionally disabled right now, so the runtime may keep stale uploaded data or render nothing until a completed readback exists.
27. Detailed per-instance `LastFrameOutput` debug capture is diagnostics-only; normal runtime flow keeps upload-ready slot payloads without full debug-visible instance mirrors.

## Wiring Hubs

- `VegetationRuntimeManager` - runtime orchestrator: gathers `VegetationTreeAuthoring` instances, freezes the runtime registry, maintains runtime tree indices, prepares one per-camera visible frame snapshot, owns the optional GPU-decision readback bridge, and uploads the latest visible-slot output into `VegetationIndirectRenderer`; current contract is explicit snapshot registration, not live transform or content sync
- `VegetationRuntimeRegistryBuilder` / `VegetationRuntimeRegistry` - Phase D contract authority: flatten authored tree/blueprint/placement/prototype payloads, build exact draw-slot registries, allocate per-branch node-decision slices, and expose the stable per-slot handoff surface for Phase E
- `VegetationSpatialGrid` - Phase D spatial partition authority: deterministic tree-to-cell registration by tree-sphere center, conservative cell resident bounds, and visible-cell query output
- `VegetationCpuReferenceEvaluator` / `VegetationDecisionDecoder` - Phase D CPU mirror authority: tree mode classification, branch tier selection, shell-node decisions, trunk selection, BFS frontier decode, and per-slot visible-instance/bounds output
- `VegetationGpuDecisionPipeline` - Phase D GPU parity authority: uploads the frozen registry into compute buffers and mirrors the CPU decision contracts when Unity exposes the expected kernels; environments that import the shader without kernels must fail explicitly instead of silently faking GPU success
- `DebugVegentationClassifyDemo` - editor-side Scene-view debug authority: rebuilds the live runtime registry for the current scene, runs the real `VegetationClassify.compute` GPU path against the active camera frustum, decodes the resulting visible frontier, and draws target-tree tree/branch/node/slot output directly in the editor
- `VegetationRendererFeature` - URP integration: schedules the indirect vegetation depth/color passes and consumes prepared runtime-manager output; it does not own classification or decode rules
- `VegetationIndirectRenderer` - indirect rendering authority: runtime material copies, visible instance buffers, indirect args, rebuilt per-slot `worldBounds`, and `RenderMeshIndirect` submission
- `DebugVegetationDemo` - editor-side Phase E verification authority: drives `VegetationRuntimeManager` frame preparation for a preview camera and draws the decoded visible instances plus the uploaded indirect batch bounds/counts
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
