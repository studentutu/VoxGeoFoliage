# VoxGeoFol Vegetation

## Quick Navigation

- Basics: [Summary](#summary), [Highlights](#highlights), [Best-Fit Use Cases](#best-fit-use-cases), [Requirements](#requirements), [Setup](#setup)
- Runtime: [Draw Calls And Draw Slots](#draw-calls-and-draw-slots), [Grass-Like Vegetation Strategies](#grass-like-vegetation-strategies), [Key Settings](#key-settings), [Runtime Pipeline](#runtime-pipeline), [Important Limitations](#important-limitations), [Supported Devices](#supported-devices)
- [Included In This Repo](#included-in-this-repo)
- [License](#license)

## Summary

`com.voxgeofol.vegetation` is a Unity 6 URP package for opaque-only, branch-assembled vegetation with a GPU-resident runtime path. Authorings are frozen into a container-owned runtime registry, visible content is classified and emitted on GPU, and URP submits vegetation through indirect depth and color passes.

## Highlights

1. `VegetationTreeAuthoring -> TreeBlueprintSO -> BranchPlacement[] -> BranchPrototypeSO`
2. One blueprint can reuse the same branch prototype many times or mix many different prototypes.
3. Many scene authorings can share one blueprint, and one container can mix many blueprints.
4. Runtime ownership is explicit and streaming-friendly through `VegetationRuntimeContainer`.
5. Draw calls scale with active draw slots, not with raw tree count.
6. Runtime is opaque-only, URP-only, compute-required, and snapshot-based until refresh.

## Missing features

1. [ ] Wind.
2. [ ] Support non-package only materials and shaders.
3. [ ] Generation of canopy-shell (primary voxelization) based on the [GPUVoxelizer](Packages/com.voxgeofol.vegetation/Runtime/VoxelizerV2/Scripts/GPUVoxelizer.cs) from quad, alpha-masked branch material.


## Best-Fit Use Cases

1. Tree species/bushes/grass-flower clumps built from reusable branch modules.
2. Multiple species that share meshes and materials to keep batching strong.
3. Grass, flowers, or shrubs authored either as clumps or as many small instances, depending on culling and placement needs.

## Draw Calls And Draw Slots

1. A draw slot is one indirect render bucket with one shared instance range and one indirect-args record.
2. Slot key = exact `mesh + material + material kind`.
3. If different trees or branch prototypes resolve to the same slot key, they batch into the same indirect draw.
4. If mesh, material, or material kind changes, a new draw slot is created.
5. Instance count grows instance-buffer usage; draw-slot count is what grows draw calls.
6. The renderer pays active slots once in depth and once in color.

Examples:

1. `1000` trees sharing the same trunk, foliage, and shell assets can still collapse into a small slot set.
2. `100` trees with unique branch meshes or unique materials can produce more active slots and more draws, even with fewer total trees.
3. Reusing the same branch prototype many times inside one blueprint does not automatically create more draws if the placements resolve to the same slot key.

## Grass-Like Vegetation Strategies

1. Clump strategy [Preferred]:
   `1 VegetationTreeAuthoring -> 1 TreeBlueprintSO -> many BranchPlacement`
   Lower scene-authoring count and lower tree-level runtime overhead, but coarser culling and coarser streaming granularity.
2. Many-small-instances strategy:
   `many VegetationTreeAuthoring -> 1 small TreeBlueprintSO each -> 1 BranchPlacement each`
   Finer culling, finer streaming, and better procedural-placement flexibility, but more runtime tree instances and more tree-level classification work.
3. Main rule:
   Shared render assets are the primary draw-call lever in both strategies. If both strategies use the same meshes and materials, draw-call count can stay similar even though runtime-management cost differs.

## Requirements

1. Unity `6000.3` or newer.
2. URP `17.3.0` or newer-compatible project setup.
3. Compute-shader and indirect-draw support on the target hardware and graphics API.
4. `VegetationRendererFeature` added to the active URP renderer. Preview via scene view or Render graph viewer.
5. `VegetationFoliageFeatureSettings.ClassifyShader` assigned to `VegetationClassify.compute`.
6. Opaque URP SRP-compatible shaders only.
7. Runtime vegetation shaders compatible with the package indirect-instance contract.

## Setup

1. Import the package into a URP project and add `VegetationRendererFeature` to the active renderer.
2. Create one or more `VegetationRuntimeContainer` roots.
3. Place `VegetationTreeAuthoring` components under the container that should own them.
4. Assign `VegetationClassify.compute` on the renderer feature.
5. Use `Fill Registered Authorings` on each container.
6. Enable the container or enter play mode to build runtime registration and GPU resources.
7. Call `RefreshRuntimeRegistration()` after transform, hierarchy, blueprint, placement, or generated-mesh changes.

## Key Settings

1. `VegetationRuntimeContainer.gridOrigin`
   World-space origin of the frozen spatial grid. It changes cell assignment and culling layout.
2. `VegetationRuntimeContainer.cellSize`
   World-space size of the frozen spatial grid. Smaller cells improve culling granularity but increase cell count; larger cells are cheaper but more conservative.
3. `VegetationRendererFeature.DepthPassEvent`
   URP event for vegetation depth submission. The first vegetation pass of the frame also prepares the GPU-resident buffers.
4. `VegetationRendererFeature.ColorPassEvent`
   URP event for vegetation color submission. It controls ordering against the rest of the opaque pipeline.
5. `VegetationFoliageFeatureSettings.EnableDiagnostics`
   Renderer-wide diagnostics toggle for every active container rendered by that feature.

## Runtime Pipeline

1. `serialized authorings -> RefreshRuntimeRegistration() -> VegetationRuntimeRegistry + spatial grid + draw slots`
2. `camera -> frustum planes -> GPU classification/emission -> shared instance buffer + indirect args`
3. `shared instance buffer + indirect args -> vegetation depth pass -> vegetation color pass`
4. `changed transforms / hierarchy / blueprint data -> RefreshRuntimeRegistration() -> rebuilt runtime state`

## Important Limitations

1. Opaque-only runtime: no transparency, no alpha clip, no masked foliage.
2. URP only. Built-in pipeline, HDRP, and custom SRP integrations are outside the contract.
3. No runtime CPU fallback, no CPU decode bridge, and no async-readback rendering path.
4. Exact CPU-side visible-instance lists are not exposed in production flow.
5. `LODGroup` is not used; LOD comes from authored distance bands and GPU classification.
6. Registration is frozen after enable. Changes require `RefreshRuntimeRegistration()`. Transform changes (position/rotation/scale) do not auto-sync (not even in editor). Either enable/disable the container (script) or force update via `RefreshRuntimeRegistration()`.
7. Each container owns only active authorings from its serialized list, and nested child containers own their own descendants.
8. Runtime diagnostics are renderer-wide through `VegetationFoliageFeatureSettings.EnableDiagnostics`.

## Supported Devices

1. Desktop and laptop GPUs that run Unity 6 URP with compute shaders and indirect draws.
2. Console-class targets with the same feature support.
3. Higher-end mobile and handheld targets when the graphics API and hardware support URP, compute shaders, and indirect draws.
4. Not targeted: WebGL, graphics APIs without compute or indirect draws, and very low-end mobile hardware.

## Included In This Repo

1. Playground scene: [../../Assets/Scenes/Playground.unity](../../Assets/Scenes/Playground.unity)
2. Package sample content: [Samples~/VegetationDemo](Samples~/VegetationDemo)
- sample mesh very hight poly pine tree, see [ChristmasTree](Samples~/VegetationDemo/Raw/ChristmasTree.fbx) with separate trunk and branches mesh from the leaves mesh (pines)
- sample high poly single Fern leaf, see [fern_foliage_dense](Samples~/VegetationDemo/Raw/fern_foliage_dense_fullgeo.obj)
- sample branch for standard tree, see [branch_leaves](Samples~/VegetationDemo/Raw/branch_leaves_fullgeo.obj)
3. Architecture authority: [UnityAssembledVegetation_FULL.md](https://github.com/studentutu/VoxGeoFoliage/blob/master/DetailedDocs/UnityAssembledVegetation_FULL.md)

## License

Package license: [LICENSE.md](LICENSE.md)
