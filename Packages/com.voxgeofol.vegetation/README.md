# VoxGeoFol Vegetation

## Summary

`com.voxgeofol.vegetation` is a Unity 6 URP package for branch-assembled, opaque-only vegetation rendering with a GPU-resident runtime path. Authoring data describes reusable branches, generated shell tiers, simplified trunk/far meshes, and runtime placement data; the runtime classifies vegetation on GPU, emits indirect instance payloads on GPU, and renders through URP via `Graphics.RenderMeshIndirect`.

Runtime ownership is container-based. A scene can use any number of `VegetationRuntimeContainer` instances, each with its own explicit serialized `VegetationTreeAuthoring` list. That is the intended contract for streamed chunks, additive scenes, and addressable prefab content.

## General Design

Authoring model:
- `VegetationTreeAuthoring`: one scene instance of one tree. It references exactly one `TreeBlueprintSO`.
- `TreeBlueprintSO`: one assembled-tree recipe. It owns the trunk/impostor data plus an array of `BranchPlacement`.
- `BranchPlacement`: one placement of one `BranchPrototypeSO` inside a blueprint.
- `BranchPrototypeSO`: one reusable branch module. The same prototype can be reused many times inside one blueprint or shared by many different blueprints.

Supported composition:
- Many `VegetationTreeAuthoring` objects can share the same `TreeBlueprintSO`.
- Different `VegetationTreeAuthoring` objects can use different `TreeBlueprintSO` assets in the same container.
- One `TreeBlueprintSO` can contain many different `BranchPrototypeSO` assets.
- One `TreeBlueprintSO` can also reuse the same `BranchPrototypeSO` many times with different positions, rotations, and scales.

Runtime model:
- `VegetationRuntimeContainer` freezes all registered authorings into one runtime registry.
- The runtime then classifies visible trees/branches on GPU and emits indirect instances into draw slots.
- Batching is not based on tree identity or blueprint identity. Batching is based on draw slots.

Draw-slot and draw-call model:
- One draw slot is keyed by exact `mesh + material + material kind`.
- If two blueprints or two branch prototypes resolve to the same render assets for a tier, they can share the same draw slot and batch into the same indirect draw.
- If meshes or materials differ, they become different draw slots and require separate indirect draws.
- Material kind is also part of the split. Trunk, foliage, shell, and far-mesh paths do not collapse into one slot just because a mesh/material asset matches.
- Draw calls are therefore driven by the number of active draw slots for the current camera and frame, not by the number of trees.
- The renderer submits both a vegetation depth pass and a vegetation color pass, so active slots are paid once in depth and once in color.

Strategy implications:
- Many copies of the same tree, or many different trees built from the same shared branch assets, are the best case for batching.
- Different blueprints can still batch well if they reuse the same meshes and materials for trunks, branches, foliage, and shell tiers.
- A blueprint that mixes many unique branch prototypes and unique materials is fully supported, but it increases the number of possible active draw slots and therefore increases draw calls.
- If the main goal is the lowest draw-call count, prefer shared branch prototypes and shared materials across species. If the main goal is unique visual identity, expect more draw slots.

## Limitation

In short:
- The runtime path is GPU-resident: compute classification selects visible content, emits indirect instance payloads on GPU, and the renderer submits those GPU-written buffers through indirect depth and color passes.

Important implementation notes:
- `VegetationRendererFeature` is wired into URP renderer-feature scheduling and recorded through the render-graph raster path. Built-in pipeline, HDRP, or custom SRP integrations are outside the current package contract.
- Runtime vegetation materials must use URP SRP-compatible shaders. Built-in pipeline shaders or ad-hoc non-SRP shaders are not supported.
- Runtime vegetation shaders must be indirect-instance compatible with the package instance payload contract. The package vegetation shaders are the reference implementation; custom shaders must replicate that instance-data path or rendering will break.

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

## Key Settings

`VegetationRuntimeContainer`

- `gridOrigin`: world-space origin of the frozen spatial grid built during registration. It affects which grid cell each authored tree lands in and therefore affects culling/classification layout.
- `cellSize`: world-space size of each spatial-grid cell. Smaller cells increase culling granularity but increase cell count; larger cells reduce grid overhead but make culling more conservative. Changing it requires `RefreshRuntimeRegistration()`.

`VegetationRendererFeature`

- `DepthPassEvent`: URP pass timing for the vegetation depth submission. This is also where the first vegetation pass of the frame prepares the GPU-resident instance and indirect-args buffers. It affects ordering against the rest of the opaque pipeline.
- `ColorPassEvent`: URP pass timing for the vegetation color submission. It affects when opaque vegetation shading lands relative to the rest of the renderer and should stay aligned with the intended opaque pipeline order.

## Rendering Pipeline

1. Enabling `VegetationRuntimeContainer` adds it to the active container list and calls `RefreshRuntimeRegistration()`.
2. Registration validates the serialized `VegetationTreeAuthoring` list, freezes it into `VegetationRuntimeRegistry`, builds the spatial grid and draw-slot registry, and allocates indirect renderer resources for that container.
3. For each eligible camera, `VegetationRendererFeature` gathers active containers and schedules one vegetation depth pass plus one vegetation color pass.
4. On the first vegetation pass for that camera and frame, each container calculates frustum planes, ensures the shared `VegetationClassify.compute` pipeline exists, and runs GPU classification plus GPU emission into shared instance and indirect-args buffers.
5. The vegetation depth pass submits `Graphics.RenderMeshIndirect` for the active draw slots with the depth-only vegetation materials.
6. The vegetation color pass submits the matching opaque vegetation materials from the same GPU-written buffers. End of frame leaves no CPU-side visible-instance list; the next frame is prepared again from current camera state.

Multi-container contract:

- Any number of `VegetationRuntimeContainer` instances can coexist in the same scene.
- Each container owns only the active authorings referenced by its serialized list.
- Nested child containers own their own descendants instead of leaking them to the parent container when the serialized lists are refilled through the editor tooling.
- This is the intended setup for streaming, additive-scene content, and addressable vegetation chunks with independent lifetimes.
