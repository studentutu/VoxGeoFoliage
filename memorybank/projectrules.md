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

## Wiring Hubs

--

## Verification

- EditMode suite in [`Assets/EditorTests`](../Assets/EditorTests) is the primary behavioral safety net.
- `CI/CITestOutput.xml` is authoritative for test results.
- `CI/CompileErrorsAfterUnityRun.txt` is authoritative for Unity compile errors.
- Use `Fully Compile by Unity` when files were added, removed, or renamed.
- Use the Rider MSBuild compile path for quick feedback only.

--
