# VoxGeoFol Vegetation

## Summary

`com.voxgeofol.vegetation` is a Unity 6 URP package for branch-assembled, opaque-only vegetation rendering with a GPU-resident runtime path. Authoring data describes reusable branches, generated shell tiers, simplified trunk/far meshes, and runtime placement data; the runtime classifies vegetation on GPU, emits indirect instance payloads on GPU, and renders through URP via `Graphics.RenderMeshIndirect`.

Runtime ownership is container-based. A scene can use any number of `VegetationRuntimeContainer` instances, each with its own explicit serialized `VegetationTreeAuthoring` list. That is the intended contract for streamed chunks, additive scenes, and addressable prefab content.

## Limitation

In short:
- Survival classification is compute shader (Lod, bias, backside trimming, occlusion via field of view of camera) -> same as actual decoding and emiting of instance data -> which then is being rendered via indirect render command.

So:
- `VegetationRuntimeContainer` registration is a frozen snapshot. Transform edits after enable are not synced automatically, even in the editor. Call `RefreshRuntimeRegistration()` after transform changes.
- Other registration-affecting changes are also not live-synced. Adding or removing authorings, changing blueprint or placement data, or swapping generated meshes after enable requires `RefreshRuntimeRegistration()`.
- Each container only registers active `VegetationTreeAuthoring` references from its serialized list, and every referenced authoring must still live inside that container's hierarchy. Nested child containers claim their own descendants when you refill the list through the editor tooling. Scene-global discovery is intentionally not supported.
- The production runtime path is GPU-resident only. There is no CPU fallback, no CPU decode bridge, and no async-readback runtime rendering path.
- Exact CPU-side visible-instance lists are not part of the production contract. Runtime diagnostics are limited to profiler markers and conservative uploaded-batch snapshots.
- Runtime diagnostics are configured on `VegetationRendererFeature` through `VegetationFoliageFeatureSettings.EnableDiagnostics`, so the toggle is renderer-wide rather than per-container.
- Runtime rendering is opaque-only and URP-only. Transparent foliage, alpha-clipped foliage, `LODGroup`, and non-URP runtime pipelines are outside the current package contract.

## Supported Devices

Supported in general:

- Desktop and laptop GPUs that run Unity 6 URP with compute shaders and indirect draws.
- Console-class targets with the same feature support.
- Higher-end mobile and handheld devices when the target graphics API and hardware support URP, compute shaders, and indirect draw submission.

Not a target:

- WebGL.
- Platforms or graphics APIs that do not support compute shaders or indirect draw workflows.
- Very low-end mobile devices where opaque indirect vegetation workloads are outside practical GPU budget.

Final platform support still depends on the target Unity player backend, graphics API, driver quality, shader import success, and available GPU budget.

## Prerequisites

- Unity `6000.3` or newer.
- `com.unity.render-pipelines.universal` `17.3.0` or newer-compatible project setup.
- The package imported into a URP project.
- `VegetationRendererFeature` added to the active Universal Renderer Data and enabled in the renderer used by the current URP pipeline asset.
- `VegetationRendererFeature` `VegetationFoliageFeatureSettings.ClassifyShader` assigned to the package `VegetationClassify.compute` asset.
- `VegetationClassify.compute` and the package runtime shaders imported successfully on the target platform.
- Opaque materials and generated vegetation assets prepared through the package authoring/bake workflow.

Render feature requirement:

- Add `VegetationRendererFeature` to the active URP renderer asset. The feature owns both the vegetation depth pass and the vegetation color pass; there are no separate runtime fallback features to enable.

## Setup and Use

1. Import the package into a Unity 6 URP project and make sure the active renderer asset includes `VegetationRendererFeature`.
2. Create one or more `VegetationRuntimeContainer` GameObjects in the scene, in streamed prefabs, or in addressable content roots.
3. Place each chunk's `VegetationTreeAuthoring` components under the hierarchy of the container that should own them.
4. Assign the package `VegetationClassify.compute` shader to `VegetationRendererFeature`.
5. On each container, use the context action or inspector button `Fill Registered Authorings` so the serialized list matches that container's hierarchy ownership.
6. Enter play mode or enable the container so it builds its runtime registry and GPU resources.
7. Call `RefreshRuntimeRegistration()` whenever transform or registration-affecting data changes after enable, or refill the serialized list first if hierarchy ownership changed.

Multi-container contract:

- Any number of `VegetationRuntimeContainer` instances can coexist in the same scene.
- Each container owns only the active authorings referenced by its serialized list.
- Nested child containers own their own descendants instead of leaking them to the parent container when the serialized lists are refilled through the editor tooling.
- This is the intended setup for streaming, additive-scene content, and addressable vegetation chunks with independent lifetimes.
