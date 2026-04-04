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
16. Hot paths should be allocation-aware and follow DRY/SingleResponsibility best practices.
17. Reusable public-facing features should live in embedded packages under `Packages/`; repo-local-only features can remain under `Assets/Scripts/Features`.
18. No useless maintenance. Scripts are either completely dropped or fully migrated to new api, no obsolete wrappers!
19. No useless abstractions, no bloatware.
20. Avoid constant asset creation and editor refresh in tests! It makes test run multiple times slower!
21. Avoid constant asset creation if full algorithm is not finished! Create finished asset once but do not refresh editor constantly! Only do AssetDatabase.SaveAssets() once all meshes are created. All operations with AssetDatabase is generally very-very long!

--

## Vegetation System Rules

1. All vegetation geometry is opaque-only - no transparency, no alpha clip, no masked materials.
2. Trees are assembled from reusable branch prototypes, not single monolithic meshes.
3. Canopy shells are hierarchical branch-local voxelized meshes: `BranchPrototypeSO.shellNodes` stores an adaptive octree where every occupied node owns its own `L0/L1/L2` shell chain.
4. Shell preview tiers keep branch structure: L0 reuses source wood, while L1/L2 use baked simplified wood meshes attached beside the leaf-frontier shell nodes.
5. Generated shell and impostor meshes now prefer topology-preserving simplification first: merge adjacent coplanar voxel faces, keep voxel silhouette/bounds, and only then enter bounded fallback when the authored settings still miss budgets.
6. Shell and impostor simplification must remain optional through authoring settings so developers can inspect raw voxel output in the editor and tests can stay on the fast path.
7. Impostor (far LOD) is a simplified opaque mesh with billboard Y-rotation, not a textured card.
8. GPU classification is a single compute dispatch per frame (frustum + LOD + backside minimization + draw emission).
9. BRG (BatchRendererGroup) is the rendering backend; no MaterialPropertyBlock usage.
10. Per-instance color variation via `RSUV` only (packed uint) - no MaterialPropertyBlock, no DOTS instanced properties for color. Single variation mechanism preserving SRP batching.
11. Branch scale is in steps of 0.25 (e.g. 0.25, 0.5, 0.75, 1.0, 1.25...); no scale quantization optimization yet.
12. Spatial partitioning via uniform grid; cell visibility is CPU frustum test + CullingGroup API (optional occlusion layer, Unity hard limit of 1024 sphere limit per culling group).
13. Authoring data lives in ScriptableObjects; runtime data in GPU buffers; no runtime data on MonoBehaviours.
14. Editor preview is transient child GameObjects with `HideFlags.DontSave | HideFlags.NotEditable` - never serialized.
15. Shell generation and impostor baking are editor-only operations (not runtime).
16. Generated shell and impostor geometry must be persisted as standalone `.mesh` assets under a writable project folder: prefer an owner-local `GeneratedMeshes/` folder under `Assets/`, otherwise fall back to `Assets/VoxGeoFol.Generated/Vegetation/Meshes/`. Do not rely on transient meshes or sub-assets that can be lost.
17. All vegetation code lives under `Packages/com.voxgeofol.vegetation/` with `Runtime/Authoring`, `Editor`, `Runtime/Shaders`, `Runtime/Rendering`, `Tests/Editor`, and `Samples~/` subfolders as needed.
18. No Unity `LODGroup` - LOD selection is fully GPU-driven via compute classification; `LODGroup` is incompatible with BRG indirect rendering.
19. Canopy/impostor shaders are minimal vertex-lit: no albedo texture, no normal map, no emission, no specular. Trunk shader uses albedo texture but no normal map.
20. Trunk is rendered in all tiers `R0-R2`; only `R3` (impostor) omits the trunk mesh.

## Wiring Hubs

- `VegetationRuntimeManager` - runtime orchestrator: gathers authoring instances, builds spatial grid, creates GPU buffers, drives per-frame classification
- `VegetationRendererFeature` - URP integration: schedules compute dispatch + depth prepass + color pass
- `VegetationBRGManager` - BRG lifecycle: mesh/material registration, draw command emission
- `VegetationAuthoringValidator` - Task 1 authoring contract authority: explicit validation for readability, opacity, budgets, bounds, scale, and LOD ordering
- `CanopyShellGenerator` - editor-side Phase C follow-up branch authority: uses `MeshVoxelizerHierarchyBuilder` to voxelize readable foliage, applies optional coplanar-face reduction on `L0/L1/L2`, performs bounded non-blocking fallback for over-budget shell levels, and refreshes voxelized `shellL1WoodMesh` / `shellL2WoodMesh`
- `MeshVoxelizerHierarchyBuilder` / `MeshVoxelizerHierarchyDemo` - shared hierarchy authority and manual inspection utility: backed by `CPUVoxelizer` volumes, splits L0 surface voxels into octant nodes, and emits one mesh triplet per node while preserving the hierarchy contract
- `CPUVoxelizer` / `CpuVoxelSurfaceMeshBuilder` - shared CPU voxel backend authority: builds indexed voxel volumes and surface-only meshes, including the optional coplanar-face merge path now used by canopy, wood, and impostor generation
- `GeneratedMeshSimplificationUtility` - editor-side simplification authority: selects the best generated mesh candidate, runs bounded voxel-resolution retries, and uses Unity `MeshLodUtility` as the last-resort fallback for non-blocking baking
- `ImpostorMeshGenerator` - editor-side tree authority: merges trunk + original placed branch `woodMesh`/`foliageMesh` in tree space, uses the same reduction/fallback path, and stores `impostorMesh` without requiring baked canopy shells
- `GeneratedMeshAssetUtility` - editor-side Phase B asset persistence authority: writes generated shell/impostor meshes as explicit `.mesh` files into writable project asset folders beside the owner asset when possible, while honoring explicit relative-folder overrides from the authoring asset
- `CheckVoxelMeshSimplification` / `CheckUnityMeshLodGenerations` - repo-local manual comparison demos under `Assets/Scripts`: let developers compare source meshes, raw voxel surfaces, reduced voxel surfaces, and Unity `MeshLodUtility` output on existing meshes without touching package bake data
- `VegetationTreeAuthoringEditorUtility` - editor-side Phase C authority: bake entry points, aggregated validation, and per-tier authoring summary for `VegetationTreeAuthoring`
- `VegetationEditorPreview` - editor-side Phase C preview authority: rebuilds transient branch-root hierarchies for all milestone representation tiers
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
