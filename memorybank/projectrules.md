# Project Specific Rules

Purpose: compact cross-module rules, runtime authorities, and wiring hubs.

## Global Rules

0. Budgeting and optimization on anything asset related. By settings: cutoff and hard budgets, pooling, level of detail, trimming, full occlusion, and incremental per-batch processing.
1. `WorldECSLoop` is the gameplay authority for the hard ECS slice. It should not own gameplay data or authoring state.
2. ECS core must not read `UnityEngine.Time`; tick and delta are passed explicitly.
3. ECS core must not use `UnityEngine.Random`; randomness must be injected explicitly if introduced later.
4. OOP controllers are request-driven or consume-driven only; they do not mutate gameplay state directly.
5. Never use direct C# events in ECS core.
6. Never store mutable runtime data in ScriptableObjects.
7. Use explicit runtime APIs in EditMode tests; do not rely on Unity lifecycle callbacks.
8. Input remains dependency-inverted: gameplay consumes explicit `EcsFrameInput` timing only.
9. Concrete authoring MonoBehaviours and ScriptableObjects are read-only inputs to runtime resolution.
10. Prefer structs where applicable, but do not use struct types as hot dictionary keys when avoidable.
11. No hidden assumptions: missing setup must throw explicit errors.
12. All integration boundaries need concise `[INTEGRATION]` summaries.
13. All static runtime stores must have deterministic reset coverage through `StaticServicesReset`.
14. OOP communications to ECS happen through transient request entities, either queued directly by helpers or created by loop-owned bus ingress.
15. MVC separation for UI remains in effect.
16. Prefer `Refresh` and `Simulate` naming over `Update` for explicit runtime APIs.
17. Keep authoring, runtime ECS data, and Unity binding logic separate.
18. Hot paths should be allocation-aware and follow Arch-ECS best practices.
19. Persistent runtime entities keep stable archetypes. Do not add or remove phase markers on persistent entities.
20. Use slim transient `*Request` entities for phase handoff and ECS/OOP integration handoff.
21. Entity order inside a phase is irrelevant. Systems must not rely on request creation order, query traversal order, or chunk order.
22. Direct request entity creation from OOP is allowed because it happens outside query iteration. This is the gameplay ingress path.
23. Direct value writes are allowed only when no archetype change occurs; structural mutations inside systems must use local command buffers.
24. Systems own full runtime logic and all world mutation they trigger.
25. Async completion must be validated before mutating persistent ECS state.
26. OOP-facing request and query helpers belong in `WorldEcsLoopContextExtensions`; keep `WorldECSLoop` and `WorldEcsLoopDriver` thin.
27. `WorldEcsLoopDriver` owns Unity frame accumulation only; simulation cadence must come from its explicit serialized tick rate.
28. Each feature is enclosed in a folder under `Loop/Features` with authoring, components, systems, contract, and extension slices as needed.
29. Only one live world ecs-loop exists at a time. The global authoring bus ingress assumes a single active loop.

## Arch ECS Notes

- Loop construction is two-step:
  - build `WorldEcsLoopContext`
  - pass that context into `WorldECSLoop`
- `WorldEcsLoopContext` owns:
  - `World`
  - `SkillFormulaCatalog`
  - optional shared `SkillDatabase`
  - read-only `AssetManagement`
  - `PresentationCatalog`
  - `Resources<PresentationLease>`
  - cached signatures
  - `SimId -> Entity`
  - `ProjectileId -> Entity`
  - loop-lifetime bus subscriptions for OOP character ingress
- Warm-up is explicit:
  - `World.EnsureCapacity(signature, amount)`
  - create and destroy warmup entities for known persistent and request archetypes
- Request ingress is entity-first:
  - direct queue APIs create canonical request entities immediately
  - `CharacterEcsLifecyclePublisher` publishes spawn and release messages to `AllBuses.Global`
  - `WorldEcsLoopContext` converts those messages into canonical ECS request entities
  - transient character and bind requests may carry existing scene instance roots; persistent ECS entities never do
- Unity-side cadence is fixed-step:
  - `WorldEcsLoopDriver` accumulates rendered frame delta
  - it emits zero or more simulation ticks using its serialized tick rate
  - each simulated tick writes fixed-step `EcsFrameInput` into the context before `RefreshSimulation()`
- Persistent runtime entities keep stable archetypes:
  - characters: `SimId + CharacterStats + CharacterScaling + CharacterSkillbook + PresentationBindingState`
  - projectiles: `ProjectileId + ProjectileRuntimeData + PresentationBindingState`
- `CharacterSpawnRequest` is self-contained:
  - `SimId`
  - `CharacterTypeId`
  - `Position`
  - `Forward`
  - `CharacterStats`
  - `CharacterScaling`
  - `CharacterSkillbook`
- `SpawnCharacterSystem` must not read cached authoring registries or spawn-template catalogs.

## Runtime Tooling

- `AssetManagement` is the read-only presentation authoring root:
  - serialized authoring for character and projectile presentation assets
  - immutable `PresentationCatalog` source for ECS bootstrap
  - spawn-root and budget configuration for presentation runtime systems
- `PresentationRuntimeSystem` is the view-side presentation runtime owner:
  - live `Resources<PresentationLease> + Handle<PresentationLease>` usage
  - pooling, loading, acquisition, release, and stale async cancellation
  - bind and release request consumption
  - character state and damage batch application
  - knowledge of currently stored live presentation items
- `CharacterAuthoringResolver` is the only runtime authoring-to-request converter for characters.
- `CharacterEcsLifecyclePublisher` is the OOP publisher for streamed and scene-owned characters.
- `CharacterViewBinder` and `ProjectileViewBinder` remain feature-local adapters driven only through explicit bind, refresh, and unbind APIs.
- `AddressablesPresentationPrefabLoader` remains the shared async loader:
  - Addressables when an address key is configured
  - fallback prefabs when authoring provides them directly
  - UniTask for async runtime flow

## Wiring Hubs

- `WorldECSLoop`
  - thin simulation and projection facade over a prebuilt `WorldEcsLoopContext`
- `WorldEcsLoopDriver`
  - thin Unity integration layer that accumulates frame time and issues fixed-step simulation ticks
- `WorldEcsLoopContext`
  - owns runtime services, unified presentation catalog, archetype signatures, lookups, world lifetime, and loop-scoped bus ingress
- `WorldEcsLoopContextExtensions`
  - context-side request and query helpers used by the loop facade and OOP callers
- `CharacterAuthoringResolver`
  - validates `Character` authoring against the shared `SkillDatabase` and builds self-contained spawn requests
- `CharacterEcsLifecyclePublisher`
  - publishes OOP spawn and release messages for streamed and scene-owned characters
- `AssetManagement`
  - read-only presentation authoring root for characters and projectiles
- `PresentationRuntimeSystem`
  - loop-owned view system that owns live leases, pools, bind/release processing, and view application
- `CharacterViewBinder`
  - local Unity/OOP reaction surface for characters
- `ProjectileViewBinder`
  - local Unity/OOP reaction surface for projectiles
- `CharacterInputPollingBridge`
  - polls active controlled characters from bound ECS lease handles and re-enters ECS through `CharacterSkillUseRequest`

## Verification

- EditMode suite in [`Assets/EditorTests`](../Assets/EditorTests) is the primary behavioral safety net.
- `CI/CITestOutput.xml` is authoritative for test results.
- `CI/CompileErrorsAfterUnityRun.txt` is authoritative for Unity compile errors.
- Use `Fully Compile by Unity` when files were added, removed, or renamed.
- Use the Rider MSBuild compile path for quick feedback only.
