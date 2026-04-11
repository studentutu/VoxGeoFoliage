# VoxGeoFol Vegetation

## Summary

`com.voxgeofol.vegetation` is a Unity 6 URP package for branch-assembled, opaque-only vegetation rendering with a GPU-resident runtime path. Authoring data describes reusable branches, generated shell tiers, simplified trunk/far meshes, and runtime placement data; the runtime classifies vegetation on GPU, emits indirect instance payloads on GPU, and renders through URP via `Graphics.RenderMeshIndirect`.

Runtime ownership is container-based. A scene can use any number of `VegetationRuntimeContainer` instances, each with its own `VegetationTreeAuthoring` hierarchy. That is the intended contract for streamed chunks, additive scenes, and addressable prefab content.

## Limitation

- `VegetationRuntimeContainer` registration is a frozen snapshot. Transform edits after enable are not synced automatically, even in the editor. Call `RefreshRuntimeRegistration()` after transform changes.
- Other registration-affecting changes are also not live-synced. Adding or removing authorings, changing blueprint or placement data, or swapping generated meshes after enable requires `RefreshRuntimeRegistration()`.
- Each container only registers active `VegetationTreeAuthoring` components inside its own hierarchy. Nested child containers claim their own descendants. Scene-global discovery is intentionally not supported.
- The production runtime path is GPU-resident only. There is no CPU fallback, no CPU decode bridge, and no async-readback runtime rendering path.
- Exact CPU-side visible-instance lists are not part of the production contract. Runtime diagnostics are limited to profiler markers and conservative uploaded-batch snapshots.
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
- `VegetationClassify.compute` and the package runtime shaders imported successfully on the target platform.
- Opaque materials and generated vegetation assets prepared through the package authoring/bake workflow.

Render feature requirement:

- Add `VegetationRendererFeature` to the active URP renderer asset. The feature owns both the vegetation depth pass and the vegetation color pass; there are no separate runtime fallback features to enable.

## Setup and Use

1. Import the package into a Unity 6 URP project and make sure the active renderer asset includes `VegetationRendererFeature`.
2. Create one or more `VegetationRuntimeContainer` GameObjects in the scene, in streamed prefabs, or in addressable content roots.
3. Place each chunk's `VegetationTreeAuthoring` components under the hierarchy of the container that should own them.
4. Assign the package `VegetationClassify.compute` shader to each container.
5. Enter play mode or enable the container so it builds its runtime registry and GPU resources.
6. Call `RefreshRuntimeRegistration()` whenever transform or registration-affecting data changes after enable.

Multi-container contract:

- Any number of `VegetationRuntimeContainer` instances can coexist in the same scene.
- Each container owns only the active authorings in its own hierarchy.
- Nested child containers own their own descendants instead of leaking them to the parent container.
- This is the intended setup for streaming, additive-scene content, and addressable vegetation chunks with independent lifetimes.
