# Add SubScene Support - Runtime Registration Only

## Purpose

Support Unity DOTS `SubScene` runtime loading without changing the vegetation renderer itself.

This plan is intentionally narrow:
- cover runtime registration/bootstrap only
- keep the existing render path
- make both classic scene mode and `SubScene` mode use one authoritative runtime owner

**Authority**:
- architecture baseline: [UnityAssembledVegetation_FULL.md](UnityAssembledVegetation_FULL.md)
- current runtime owner: [../Packages/com.voxgeofol.vegetation/Runtime/Rendering/VegetationRuntimeContainer.cs](../Packages/com.voxgeofol.vegetation/Runtime/Rendering/VegetationRuntimeContainer.cs)

---

## Problem

Current registration depends on live `MonoBehaviour` state:
- `VegetationRuntimeContainer.OnEnable()` / `OnDisable()`
- `List<VegetationTreeAuthoring>`
- child `Transform` ownership checks
- `authoring.gameObject.activeInHierarchy`
- static active-container tracking of `VegetationRuntimeContainer`

That fails for closed `SubScene` runtime loading because the runtime cannot depend on live authoring `GameObject`s.

The failure is structural, not a missing refresh call.

---

## Locked Design Decisions

## 1. Single Runtime Owner

Use one authoritative runtime owner in both modes:
- `AuthoringContainerRuntime`

`AuthoringContainerRuntime` becomes the only runtime owner of:
- `VegetationRuntimeRegistry`
- `VegetationIndirectRenderer`
- `VegetationGpuDecisionPipeline`
- registration lifecycle
- runtime disposal

Everything else becomes a provider/adapter.

## 2. Lightweight Providers Only

`VegetationRuntimeContainer` stays in the main package assembly but becomes lightweight.

It is no longer the real runtime owner.

Its job becomes:
- hold serialized authoring data and settings
- collect/store classic-scene authorings (list of VegetationTreeAuthoring stays in tact)
- create/register one `AuthoringContainerRuntime` and converts list of VegetationTreeAuthoring into runtime-safe data on `OnEnable()`
- unregister/dispose it on `OnDisable()`

For `SubScene` mode, introduce:
- `SubSceneAuthoring`

`SubSceneAuthoring` is also just a lightweight provider.

Its job becomes:
- read data from `VegetationRuntimeContainer`
- bake container/tree registration data
- create/register one `AuthoringContainerRuntime` when the baked `SubScene` data loads
- unregister/dispose it when the baked `SubScene` data unloads

## 3. Shared Runtime Tree Data

Introduce:
- `VegetationTreeAuthoringRuntime`

This is the shared runtime-safe tree data representation used by both providers.

It must not be another `MonoBehaviour`.

It should be a plain runtime record/class/struct that carries only what registration needs.

Minimum target shape:

```text
VegetationTreeAuthoringRuntime
- StableTreeId
- DebugName
- WorldTransform
- TreeBlueprintSO reference
- IsActive
```

Both modes must produce the same `VegetationTreeAuthoringRuntime` shape before registration starts.

## 4. Renderer Consumes Runtime Owner, Not Provider

`VegetationRendererFeature` / `VegetationRenderPass` must stop working against `VegetationRuntimeContainer`.

They should consume:
- `AuthoringContainerRuntime`

The render pass should not care whether that runtime owner came from:
- classic `MonoBehaviour` lifecycle
- baked `SubScene` lifecycle

---

## Critical Correction To The Proposition

Do **not** put `AuthoringContainerRuntime` and `VegetationTreeAuthoringRuntime` into the same DOTS-specific assembly as `SubSceneAuthoring`.

That creates the wrong dependency shape.

Why it breaks:
- `AuthoringContainerRuntime` owns logic that depends on the current vegetation runtime code
- `VegetationRendererFeature` and `VegetationRuntimeContainer` need to use `AuthoringContainerRuntime`
- if `AuthoringContainerRuntime` lives in a DOTS assembly, the main vegetation assembly must reference it
- if that DOTS assembly also references the main vegetation assembly, the asmdef graph becomes cyclic

That is not theoretical. It is the first thing that will block implementation.

So the assembly split must be:

## Main vegetation assembly, no DOTS

Keep here:
- `VegetationRuntimeContainer`
- `AuthoringContainerRuntime`
- `VegetationTreeAuthoringRuntime`
- active runtime-owner registry/service
- `VegetationRendererFeature`
- existing registration/rendering code

## Separate DOTS support assembly

Keep here:
- `SubSceneAuthoring`
- bakers
- baked components / blob data
- ECS load/unload bootstrap systems

This keeps the main package DOTS-free while avoiding asmdef cycles.

---

## Single Source Of Truth

Authoring source of truth remains:
- `VegetationRuntimeContainer`
- child `VegetationTreeAuthoring`
- referenced scriptable objects

Do not create a second user-edited source of truth inside `SubSceneAuthoring`.

`SubSceneAuthoring` must read from `VegetationRuntimeContainer`, not redefine the same settings.

That means:
- container settings live only on `VegetationRuntimeContainer`
- tree ownership is still the serialized `registeredAuthorings` list
- classic mode converts that data into `VegetationTreeAuthoringRuntime`
- `SubScene` mode bakes that same data into `VegetationTreeAuthoringRuntime`-equivalent payload

If `SubSceneAuthoring` starts duplicating grid settings, capacity, or authoring ownership rules, the design is already rotting/rejected.

---

## Runtime Architecture

## 1. AuthoringContainerRuntime

`AuthoringContainerRuntime` holds all logic currently inside `VegetationRuntimeContainer`:
- refresh registration
- reset runtime state
- prepare frame for camera
- own runtime registry
- own indirect renderer
- own GPU decision pipeline
- own diagnostics state

Target shape:

```text
AuthoringContainerRuntime
- ContainerId
- Layer
- GridOrigin
- CellSize
- MaxVisibleInstanceCapacity
- IReadOnlyList<VegetationTreeAuthoringRuntime>
- VegetationRuntimeRegistry
- VegetationIndirectRenderer
- VegetationGpuDecisionPipeline?
- Lifecycle methods:
  - Activate()
  - Deactivate()
  - RefreshRuntimeRegistration()
  - ResetRuntimeState()
  - PrepareFrameForCamera(...)
```

Important:
- it should not depend on `MonoBehaviour`
- it should not depend on `Unity.Entities`

## 2. VegetationRuntimeContainer

`VegetationRuntimeContainer` becomes the classic-scene lifecycle provider.

Responsibilities:
- own serialized settings and authoring references
- validate classic-scene ownership assumptions
- build `VegetationTreeAuthoringRuntime` records from live authorings
- construct one `AuthoringContainerRuntime`
- register/unregister it into the active runtime-owner registry

Removed responsibility:
- it no longer owns runtime rendering state directly

## 3. VegetationTreeAuthoringRuntime

This is the bridge type between authoring and registration.

Responsibilities:
- represent one tree instance for runtime registration
- be fully usable without live `GameObject`s
- carry stable debug/runtime identity

Forbidden responsibilities:
- `MonoBehaviour` lifecycle
- editor preview behavior
- asset mutation

## 4. SubSceneAuthoring

`SubSceneAuthoring` is the DOTS-side lifecycle provider.

Responsibilities:
- read container data from `VegetationRuntimeContainer`
- bake container settings plus tree runtime data
- trigger create/register of one `AuthoringContainerRuntime` when baked scene data loads
- trigger unregister/dispose on unload

Forbidden responsibilities:
- storing a second copy of authoring truth
- implementing runtime rendering logic

---

## Active Runtime Owner Registry

The renderer needs one shared discovery point.

Introduce:

```text
VegetationActiveAuthoringContainerRuntimes
- Register(AuthoringContainerRuntime runtime)
- Unregister(AuthoringContainerRuntime runtime)
- GetActive(List<AuthoringContainerRuntime> target)
```

Both providers use this registry:
- `VegetationRuntimeContainer`
- `SubSceneAuthoring` runtime bootstrap

`VegetationRendererFeature` then consumes `AuthoringContainerRuntime` from this registry.

---

## Required Refactors

## Refactor A - Move Runtime Logic Out Of VegetationRuntimeContainer

Current issue:
- `VegetationRuntimeContainer` mixes:
  - serialized authoring ownership
  - lifecycle provider behavior
  - runtime registration logic
  - runtime renderer ownership

Required change:
- move the runtime logic into `AuthoringContainerRuntime`
- leave `VegetationRuntimeContainer` as a lifecycle/data adapter only

## Refactor B - Replace VegetationTreeAuthoring As Registration Input

Current issue:
- `VegetationRuntimeRegistryBuilder.Build(...)` expects `IReadOnlyList<VegetationTreeAuthoring>`

Required change:
- replace that input with `IReadOnlyList<VegetationTreeAuthoringRuntime>`

This is mandatory.

Without that change, `SubScene` support is still fake.

## Refactor C - Replace Provider-Based Render Discovery

Current issue:
- `VegetationRendererFeature` currently discovers `VegetationRuntimeContainer`

Required change:
- render pass discovers `AuthoringContainerRuntime`

This is the runtime abstraction boundary that actually matters.

## Refactor D - Remove Live-GameObject Assumptions From Registration

Current issue:
- registration currently reads:
  - `authoring.transform`
  - `authoring.gameObject.activeInHierarchy`
  - `authoring.ResetRuntimeTreeIndex()`
  - `authoring.RefreshRuntimeTreeIndex(...)`

Required change:
- convert all registration inputs to runtime-safe fields on `VegetationTreeAuthoringRuntime`
- move runtime tree index/debug data to the runtime-safe layer if still needed

---

## SubScene Data Flow

## Bake Time

`SubSceneAuthoring` reads:
- the `VegetationRuntimeContainer` on the same GameObject
- its serialized `registeredAuthorings`
- container settings

It then bakes:
- stable `ContainerId`
- container settings
- baked tree runtime records derived from `VegetationTreeAuthoringRuntime`

Bake-time ownership rule:
- resolve ownership once from the serialized container list
- do not rediscover children by hierarchy traversal at runtime

## Runtime Load

When baked `SubScene` data loads:
- DOTS bootstrap creates one `AuthoringContainerRuntime`
- DOTS bootstrap registers it

When baked `SubScene` data unloads:
- DOTS bootstrap unregisters it
- DOTS bootstrap disposes it

---

## Classic Scene Data Flow

On `VegetationRuntimeContainer.OnEnable()`:
- build runtime-safe tree records from current serialized authorings
- create one `AuthoringContainerRuntime`
- register it
- activate/refresh registration

On `VegetationRuntimeContainer.OnDisable()`:
- unregister it
- dispose/reset it

That keeps the current user workflow but removes runtime ownership from the provider.

---

## Hidden Failure Modes That Must Be Handled

## 1. Duplicate Registration

If open `SubScene` authoring and baked runtime data both try to register the same container, duplicates will appear.

Need:
- stable `ContainerId`
- registry-side uniqueness enforcement
- explicit precedence rule

Minimum rule:
- one active `ContainerId` == one active `AuthoringContainerRuntime`

## 2. Wrong Assembly Split

If `AuthoringContainerRuntime` moves into the DOTS assembly, the asmdef graph will either:
- cycle
- or force much more code to move than this task claims

That scope jump must be rejected.

## 3. Fake Runtime Tree Type

If `VegetationTreeAuthoringRuntime` is implemented as another `MonoBehaviour`, `SubScene` support is still broken.

## 4. Unload Leaks

If `SubScene` unload does not dispose runtime GPU objects deterministically, runtime memory will leak across scene streaming events.

## 5. Drift Between Providers

If classic mode and `SubScene` mode produce different `VegetationTreeAuthoringRuntime` ordering or field values, registration bugs will become mode-dependent.

The two providers must converge on the same runtime data contract.

---

## Test Requirements

Need explicit coverage for:

1. classic scene provider creates one `AuthoringContainerRuntime`
2. classic scene provider disposes it on disable
3. `SubScene` bake/load creates one `AuthoringContainerRuntime`
4. `SubScene` unload disposes it
5. same authored data produces equivalent `VegetationTreeAuthoringRuntime` payload in both modes
6. same authored data produces equivalent `VegetationRuntimeRegistry` in both modes
7. duplicate `ContainerId` registration is rejected or deduped deterministically

Current gap:
- existing vegetation tests are editor-only and do not cover `SubScene` registration lifecycle

---

## Implementation Order

1. Introduce `AuthoringContainerRuntime` in the main vegetation assembly and move container runtime logic into it.
2. Introduce `VegetationTreeAuthoringRuntime` in the main vegetation assembly and refactor the registry builder to consume it.
3. Convert `VegetationRuntimeContainer` into a classic-scene lifecycle provider.
4. Add active runtime-owner registry and switch `VegetationRendererFeature` to consume `AuthoringContainerRuntime`.
5. Add DOTS support assembly with `SubSceneAuthoring`, bakers, and runtime bootstrap systems.
6. Add duplicate-registration safeguards based on stable `ContainerId`.
7. Add classic-vs-`SubScene` equivalence tests.

---

## Done Criteria

This work is done only when all of the following are true:

- both modes use `AuthoringContainerRuntime` as the only runtime owner
- `VegetationRuntimeContainer` is only a provider
- `SubSceneAuthoring` is only a provider
- runtime registration no longer depends on live `VegetationTreeAuthoring`
- renderer feature consumes `AuthoringContainerRuntime`, not `VegetationRuntimeContainer`
- DOTS support lives in a separate DOTS-specific assembly
- main vegetation assembly remains DOTS-free
- `SubScene` load and unload deterministically create and dispose runtime state

If the final result still needs live authoring `GameObject`s for `SubScene` runtime registration, the design failed.
