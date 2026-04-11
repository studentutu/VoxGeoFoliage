# VoxGeoFol Vegetation

## Overview

`com.voxgeofol.vegetation` is a Unity 6 URP package for branch-assembled, opaque-only vegetation with a GPU-resident runtime path. It is inspired by foliage-assembly workflows built from reusable branch modules, explicit shell tiers, and opaque far meshes, but implemented for Unity through GPU classification and `Graphics.RenderMeshIndirect`.

Runtime ownership is container-based. A scene can use any number of `VegetationRuntimeContainer` instances for chunks, additive scenes, or addressable prefabs.

## Target use-case Model

- `VegetationTreeAuthoring`: one instance of vegetation, one `TreeBlueprintSO`.
  - meaning that grass can be a single instance of vegetation authority with multiple different branches (e.g. actual instances of the foliage grass blades or flowers)
  - single instance of a tree is also a single `TreeBlueprintSO`
- `TreeBlueprintSO`: one assembled-tree recipe with trunk, trunk-L3, far mesh (geometry-imposter), LOD profile, and `BranchPlacement[]`.
- `BranchPlacement`: one placement of one `BranchPrototypeSO`.
- `BranchPrototypeSO`: one reusable branch module.
- Supported composition: many authorings can share one blueprint, one container can mix many blueprints, and one blueprint can mix many branch prototypes or reuse the same prototype many times.
- Registration is container-scoped (to support streaming) and snapshot-based until `RefreshRuntimeRegistration()` runs.

## Draw Calls And Batching

- Runtime flattens authorings into a registry, classifies visible content on GPU, and emits indirect instances into draw slots.
- One draw slot is one indirect render bucket with one shared instance range and one indirect-args record.
- A draw slot is keyed by exact `mesh + material + material kind`.
- Shared render assets across blueprints or branch prototypes batch into the same indirect draw.
- Different meshes, materials, or material kinds split into different draw slots.
- Draw-call count scales with active draw slots for the current camera, not with raw tree count.
- Vegetation is submitted in both depth and color, so active slots are paid once per pass.
- Lowest draw-call count comes from shared branch prototypes and shared materials across species. Unique assets are supported, but they increase draw slots.

Concrete effect:
- Reusing the same branch prototype many times does not automatically create more draw calls. If those placements resolve to the same mesh/material tier, they still write into the same draw slot.
- Creating many different branch prototypes is also not automatically expensive. It becomes expensive when those prototypes resolve to different meshes or materials, because that creates more draw slots.
- A scene with many trees can still render with a small number of indirect draws if most visible content resolves to the same small set of slots.

Example:
- `1000` trees that all share the same trunk meshes, trunk material, foliage mesh/material, and shell assets can still collapse into a relatively small slot set.
- `100` trees that all use unique branch meshes or unique materials can create more active draw slots and therefore more draws, even though the raw tree count is lower.

## Grass-Like Vegetation Strategies

Both strategies are supported by the package.

`1.` [Preferred] One vegetation instance as one clump blueprint with many branch placements:
- Example: one `VegetationTreeAuthoring` represents one grass tuft or flower patch, and its `TreeBlueprintSO` contains many blade or flower `BranchPlacement` entries.
- Pros: fewer scene authorings, fewer runtime tree instances, fewer tree bounds to classify, simpler placement when the content is naturally clumped.
- Pros: if the blades/flowers reuse the same few branch prototypes and materials, draw calls can stay very low because most visible content lands in the same draw slots.
- Cons: culling and LOD are coarser, because the whole clump shares one tree-level bounds and one tree-level classification path before branch-tier decisions.
- Cons: if the clump becomes large, it can keep more content resident than necessary when only part of it is relevant.

`2.` Many small vegetation instances with one branch placement each:
- Example: many `VegetationTreeAuthoring` objects represent individual grass plants or tiny tufts, and each blueprint contains only one branch placement.
- Pros: finer spatial granularity for culling, streaming, and placement variation.
- Pros: better when the content should behave like many separate plants instead of one shared patch.
- Cons: more runtime tree instances, more registry records, and more tree-level classification work.
- Cons: draw calls do not necessarily go down compared with the clump approach; if the assets are the same, the renderer can still end up using the same draw slots while paying more instance-management overhead.

Rule of thumb:
- Use the clump approach when the content is visually read as one patch and the main goal is lower runtime management overhead.
- Use the many-small-instances approach when culling granularity, streaming granularity, or procedural placement flexibility matters more.
- The main draw-call lever is shared render assets, not whether the content is authored as one big blueprint or many small authorings.

## Requirements

- Unity `6000.3` or newer.
- URP `17.3.0` or newer-compatible project setup.
- Compute shaders and indirect draws on the target hardware and graphics API.
- `VegetationRendererFeature` added to the active URP renderer.
- `VegetationFoliageFeatureSettings.ClassifyShader` assigned to `VegetationClassify.compute`.
- Opaque URP SRP-compatible shaders only.
- Runtime vegetation shaders must be indirect-instance compatible with the package instance-data contract.

## Setup

1. Import the package into a URP project and add `VegetationRendererFeature` to the active renderer.
2. Create one or more `VegetationRuntimeContainer` roots.
3. Place `VegetationTreeAuthoring` components under the container that should own them.
4. Assign `VegetationClassify.compute` on the renderer feature.
5. Use `Fill Registered Authorings` on each container.
6. Enable the container or enter play mode to build runtime registration and GPU resources.
7. Call `RefreshRuntimeRegistration()` after transform, hierarchy, blueprint, placement, or generated-mesh changes.

## Key Settings

- `VegetationRuntimeContainer.gridOrigin`: world-space origin of the frozen spatial grid. It changes cell assignment and culling layout.
- `VegetationRuntimeContainer.cellSize`: world-space size of the frozen spatial grid. Smaller cells improve culling granularity but increase cell count; larger cells are cheaper but more conservative.
- `VegetationRendererFeature.DepthPassEvent`: URP event for vegetation depth submission. The first vegetation pass of the frame also prepares the GPU-resident buffers.
- `VegetationRendererFeature.ColorPassEvent`: URP event for vegetation color submission. It controls final ordering against the rest of the opaque pipeline.
- `VegetationFoliageFeatureSettings.EnableDiagnostics`: renderer-wide diagnostics toggle for every active container rendered by that feature.

## Runtime Pipeline

1. Enabling `VegetationRuntimeContainer` adds it to the active container list and calls `RefreshRuntimeRegistration()`.
2. Registration validates and freezes the serialized `VegetationTreeAuthoring` list into `VegetationRuntimeRegistry`, spatial grid, branch/prototype data, and draw slots.
3. `VegetationRendererFeature` gathers active containers for each eligible camera and schedules one vegetation depth pass and one vegetation color pass.
4. On the first vegetation pass for that camera and frame, each container calculates frustum planes, ensures `VegetationClassify.compute` is ready, and runs GPU classification plus GPU emission into shared instance and indirect-args buffers.
5. The depth pass submits `Graphics.RenderMeshIndirect` for active draw slots with depth-only materials.
6. The color pass submits matching opaque vegetation materials from the same GPU-written buffers.

## Constraints

- Opaque-only runtime: no transparency, no alpha clip, no masked foliage.
- URP only. Built-in pipeline, HDRP, and custom SRP integrations are outside the contract.
- No runtime CPU fallback, no CPU decode bridge, and no async-readback rendering path.
- Exact CPU-side visible-instance lists are not exposed in production flow.
- `LODGroup` is not used; LOD comes from authored distance bands and GPU classification.
- Each container owns only active authorings from its serialized list, and nested child containers own their own descendants.
- Registration is frozen after enable. Changes require `RefreshRuntimeRegistration()`. Moving/rotating/scaling transforms will not auto-sync! You need to manually call `RefreshRuntimeRegistration()` on the authoritative container.
- Runtime diagnostics are renderer-wide through `VegetationFoliageFeatureSettings.EnableDiagnostics`.

## Supported Devices

- Desktop and laptop GPUs that run Unity 6 URP with compute shaders and indirect draws.
- Console-class targets with the same feature support.
- Higher-end mobile and handheld targets when the graphics API and hardware support URP, compute shaders, and indirect draws.
- Not targeted: WebGL, graphics APIs without compute or indirect draws, and very low-end mobile hardware.

## Included

- Package sample content: [Samples~/VegetationDemo](Samples~/VegetationDemo)
- sample mesh very hight poly pine tree, see [ChristmasTree](Samples~/VegetationDemo/Raw/ChristmasTree.fbx) with separate trunk and branches mesh from the leaves mesh (pines)
- sample high poly single Fern leaf, see [fern_foliage_dense](Samples~/VegetationDemo/Raw/fern_foliage_dense_fullgeo.obj)
- sample branch for standard tree, see [branch_leaves](Samples~/VegetationDemo/Raw/branch_leaves_fullgeo.obj)


## License

- Package license: [LICENSE.md](LICENSE.md)
