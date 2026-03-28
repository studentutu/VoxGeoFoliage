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
17. Each feature is enclosed in a folder under `Assets/Scripts/Features` with authoring, components, systems, contract, and extension folders as needed.

--

## Vegetation System Rules

1. All vegetation geometry is opaque-only - no transparency, no alpha clip, no masked materials.
2. Trees are assembled from reusable branch prototypes, not single monolithic meshes.
3. Canopy shells (L0/L1/L2) are branch-local voxelized meshes, not billboard cards.
4. Impostor (far LOD) is a simplified opaque mesh with billboard Y-rotation, not a textured card.
5. GPU classification is a single compute dispatch per frame (frustum + LOD + backside minimization + draw emission).
6. BRG (BatchRendererGroup) is the rendering backend; no MaterialPropertyBlock usage.
7. Per-instance color variation via `RSUV` only (packed uint) - no MaterialPropertyBlock, no DOTS instanced properties for color. Single variation mechanism preserving SRP batching.
8. Branch scale is in steps of 0.25 (e.g. 0.25, 0.5, 0.75, 1.0, 1.25...); no scale quantization optimization yet.
9. Spatial partitioning via uniform grid; cell visibility is CPU frustum test + CullingGroup API (optional occlusion layer, 1024 sphere limit per instance).
10. Authoring data lives in ScriptableObjects; runtime data in GPU buffers; no runtime data on MonoBehaviours.
11. Editor preview is transient child GameObjects with `HideFlags.DontSave` - never serialized.
12. Shell generation and impostor baking are editor-only operations (not runtime).
13. All vegetation code lives under `Assets/Scripts/Features/Vegetation/` with `Authoring/Runtime/Editor/Shaders/Rendering` subfolders.
14. No Unity `LODGroup` - LOD selection is fully GPU-driven via compute classification; `LODGroup` is incompatible with BRG indirect rendering.
15. Canopy/impostor shaders are minimal vertex-lit: no albedo texture, no normal map, no emission, no specular. Trunk shader uses albedo texture but no normal map.
16. Trunk is rendered in all tiers `R0-R2`; only `R3` (impostor) omits the trunk mesh.

## Wiring Hubs

- `VegetationRuntimeManager` - runtime orchestrator: gathers authoring instances, builds spatial grid, creates GPU buffers, drives per-frame classification
- `VegetationRendererFeature` - URP integration: schedules compute dispatch + depth prepass + color pass
- `VegetationBRGManager` - BRG lifecycle: mesh/material registration, draw command emission
- `VegetationAuthoringValidator` - Task 1 authoring contract authority: explicit validation for readability, opacity, budgets, bounds, scale, and LOD ordering
- `VegetationTreeAuthoringEditor` - editor integration: preview controls, bake buttons, validation display

--

## Verification

- EditMode suite in [`Assets/EditorTests`](../Assets/EditorTests) is the primary behavioral safety net.
- Vegetation authoring coverage currently starts in [`Assets/EditorTests/Vegetation`](../Assets/EditorTests/Vegetation).
- `CI/CITestOutput.xml` is authoritative for test results.
- `CI/CompileErrorsAfterUnityRun.txt` is authoritative for Unity compile errors.
- Use `Fully Compile by Unity` when files were added, removed, or renamed.
- Use the Rider MSBuild compile path for quick feedback only.

--
