# VoxGeoFol Vegetation

## Quick Navigation

- Basics: [Summary](#summary), [Highlights](#highlights), [Best-Fit Use Cases](#best-fit-use-cases), [Requirements](#requirements), [Setup](#setup)
- Runtime: [Draw Calls And Draw Slots](#draw-calls-and-draw-slots), [Runtime Terminology](#runtime-terminology), [Grass-Like Vegetation Strategies](#grass-like-vegetation-strategies), [Key Settings](#key-settings), [Capacity And Containers](#capacity-and-containers), [Current Lifecycle](#current-lifecycle), [Runtime Pipeline](#runtime-pipeline), [Important Limitations](#important-limitations), [Supported Devices](#supported-devices)
- [Included In This Repo](#included-in-this-repo)
- [License](#license)
- [Kudos](#kudos)

## Summary

`com.voxgeofol.vegetation` is a Unity 6 URP package for opaque-only, branch-assembled vegetation with a GPU-resident tree-first runtime path. Authorings are frozen into a container-owned runtime registry, every visible non-far tree is accepted to at least `TreeL3` before optional branch expansion, and URP submits the compact accepted-content set through indirect depth and color passes.

## Highlights

1. `VegetationTreeAuthoring -> TreeBlueprintSO -> BranchPlacement[] -> BranchPrototypeSO`
2. One blueprint can reuse the same branch prototype many times or mix many different prototypes.
3. Many scene authorings can share one blueprint, and one container can mix many blueprints.
4. Runtime ownership is explicit and streaming-friendly through `VegetationRuntimeContainer` with one shared `AuthoringContainerRuntime` behind both classic-scene and SubScene flows.
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
6. Classic-scene flow:
   leave `VegetationRuntimeContainer` enabled and it will create its runtime owner on `OnEnable()`.
7. Dots unity `SubScene` flow:
   add `SubSceneAuthoring` on the same GameObject as `VegetationRuntimeContainer` before baking/loading the `SubScene`.
   Enable/Disabling `VegetationRuntimeContainer` will not change `SubSceneAuthoring`.
   `SubSceneAuthoring` and  unity `SubScene` will not be shown in scene view, so in order to view both - make subscene editable and use enabled `VegetationRuntimeContainer`. 
8. Call `RefreshRuntimeRegistration()` after transform, hierarchy, blueprint, placement, or generated-mesh changes.

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
   Renderer-wide diagnostics toggle for every active container rendered by that feature. This is also the switch for current runtime-review telemetry in the Unity Console.
6. `VegetationRuntimeContainer.maxVisibleInstanceCapacity`
   Per-container hard cap for visible packed instances. This does not define a global full-scene budget unless the whole scene is rendered through one container.

## Diagnostics

1. Open the URP renderer data asset that contains `VegetationRendererFeature`.
2. Expand `VegetationRendererFeature` settings.
3. Enable `VegetationFoliageFeatureSettings.EnableDiagnostics`.
4. Enter Play Mode or render through SceneView/Game cameras with vegetation enabled.
5. Read the Unity Console output from `AuthoringContainerRuntime`, `VegetationRenderPass`, and `VegetationIndirectRenderer`.

Current shipped diagnostics include:

1. Registration counts for `trees`, `TreeBlueprints[]`, `BlueprintBranchPlacements[]`, `BranchPrototypes[]`, `drawSlots`, and `cells`.
2. Exact allocated GPU bytes for urgent-path branch-owned buffers:
   `blueprintPlacementBufferBytes`, `prototypeBufferBytes`, `treeVisibilityBufferBytes`, `expandedBranchWorkItemBufferBytes`, `totalBranchTelemetryBufferBytes`, `visibleInstanceStrideBytes`, and `visibleInstanceCapacityBytes`.
3. One-shot prepared-frame acceptance telemetry:
   `visibleTrees`, `acceptedTreeL3`, `promotedL2`, `promotedL1`, `promotedL0`, `rejectedPromotions`, `expandedTrees`, `expandedBranchWorkItems`, `acceptedTierCostUsage`, `nonZeroEmittedSlots`, `emittedVisibleInstances`, and `emittedVisibleInstanceBytes`.
4. Final render-prep diagnostics use the compact non-zero slot set from `VegetationIndirectRenderer`, so uploaded/submitted slot counts now match the emitted slot surface instead of every registered draw slot.

Scope note:

1. This is current runtime-review telemetry only.
2. `nonZeroEmittedSlots` and emitted visible-instance totals come from one synchronous CPU readback of `_SlotEmittedInstanceCounts`, so leave diagnostics off outside review.
3. Dense-forest validation still needs scene-level visual review; the runtime logs the counts, but it does not replace inspecting the one-container forest scenario from `DetailedDocs/urgentRedesign.md`.

## Capacity And Containers

1. `VegetationRuntimeContainer.maxVisibleInstanceCapacity` applies to one container and only to the trees owned by that container.
2. One container owns one runtime registry, one GPU decision pipeline, and one packed visible-instance buffer.
3. If the whole forest lives under one container, that container budget behaves like a full-scene budget.
4. If the forest is split across multiple containers, each container gets its own budget and its own packed visible-instance buffer.
5. Splitting a `6000`-tree forest across multiple containers is valid and is the intended way to chunk large scenes for streaming/addressable ownership.
6. Splitting is not free:
   each container adds its own runtime registry, GPU pipeline state, indirect args, and packed instance buffer memory.
7. There is currently no global cross-container budget coordinator and no global near-detail prioritization. If the camera sees many heavy containers at once, total memory and total visible-instance capacity are the sum of all container budgets.
8. If one container runs out of its own budget, the urgent runtime degrades detail inside that container by falling back to `TreeL3` and then dropping farther optional content; raising the budget helps only that container.
9. Current default `maxVisibleInstanceCapacity` is `262144`. Shared packed instance payload is approximately `144 bytes` per visible instance, so larger values increase GPU memory quickly.
10. If near `L0/L1` detail disappears, first check whether one container owns too much visible content for its own budget before assuming the whole scene budget is too small.
11. Visible packed instances are final draw-ready per-slot instances after tree-first acceptance and promoted-tree-only branch survival.

## Runtime Terminology

| Term | Status | Where Used | Purpose |
| --- | --- | --- | --- |
| `registeredAuthorings` | Shipped | `VegetationRuntimeContainer.registeredAuthorings` | Explicit container ownership boundary for `VegetationTreeAuthoring` inputs. |
| `VegetationTreeAuthoringRuntime` | Shipped | `VegetationRuntimeContainer.BuildRuntimeTreeAuthorings()` -> `AuthoringContainerRuntime` | Runtime-safe tree snapshot used so registration does not depend on live `MonoBehaviour` traversal. |
| `VegetationRuntimeRegistry` | Shipped | Built by `VegetationRuntimeRegistryBuilder`, then consumed by GPU pipeline and indirect renderer | Frozen flattened runtime snapshot for one container. |
| `TreeL3 floor` | Shipped urgent path | `TreeBlueprintSO.treeL3Mesh`, `VegetationGpuDecisionPipeline.AcceptTreeTiers` | Mandatory whole-tree non-far fallback accepted before any expanded branch work exists. |
| `Blueprint branch placement` | Shipped urgent path | `VegetationRuntimeRegistry.BlueprintBranchPlacements[]` | Reusable branch placement data stored once per blueprint and expanded on demand only for promoted trees. |
| `Draw slot` | Shipped | `VegetationRuntimeRegistry.DrawSlots`, slot counters, `VegetationIndirectRenderer` | Exact `Mesh + Material + MaterialKind` batch identity. One slot owns one slot index, one indirect-args record, and one potential indirect submission per pass. |
| `Registered draw slot` | Shipped | `VegetationRuntimeRegistry.DrawSlots` | One slot that exists in the runtime registry, whether or not the current frame emitted any instances into it. |
| `Non-zero emitted slot` | Shipped telemetry | `_SlotEmittedInstanceCounts`, `AuthoringContainerRuntime preparedFrameTelemetry` | One draw slot whose emitted instance count is greater than zero for the prepared frame. This is the measured submission-worthy subset. |
| `Visible instance` | Shipped | Packed into `residentInstanceBuffer`; bounded by `VegetationRuntimeContainer.maxVisibleInstanceCapacity` | Final draw-ready instance payload written by the compute path. Many visible instances can map to one draw slot. |
| `Indirect submission` | Shipped urgent path | `VegetationIndirectRenderer.Render()` depth/color calls | One final `DrawMeshInstancedIndirect` call for one non-zero active draw slot in one pass. |
| `Branch split tier` | Shipped urgent path | `BranchPrototypeSO.branchL1/2/3CanopyMesh`, `BranchPrototypeSO.branchL1WoodMesh`, `shellL1WoodMesh`, `shellL2WoodMesh` | Separate baked canopy and wood meshes used for promoted branch-expanded `L1/L2/L3`; no BFS traversal is involved. |
| `Accepted tree tier` | Shipped urgent path | `VegetationGpuDecisionPipeline`, `TreeVisibilityGpu.acceptedTier` | The one final representation chosen for one visible tree in the current frame: `Impostor`, `TreeL3`, `L2`, `L1`, or `L0`. |
| `Compact expanded branch work item` | Shipped urgent path | `_ExpandedBranchWorkItems`, promoted-tree branch count/emit kernels | Per-frame branch placement work generated only for trees already promoted above `TreeL3`. |
| `Active-slot filtering` | Shipped urgent path | `VegetationIndirectRenderer.BindGpuResidentFrame()` | Final submission compaction that keeps only non-zero emitted slots after accepted tree and branch content are emitted. |

## Current Lifecycle

### Registration / Flattening

```text
VegetationRuntimeContainer.registeredAuthorings
(List<VegetationTreeAuthoring>)
-> BuildRuntimeTreeAuthorings()
-> VegetationTreeAuthoringRuntime[]
-> AuthoringContainerRuntime.Activate()
-> AuthoringContainerRuntime.RefreshRuntimeRegistration()
-> VegetationRuntimeRegistryBuilder.Build()
-> VegetationRuntimeRegistry
   -> DrawSlots[]                 exact mesh + material + material-kind buckets
   -> TreeInstances[]             one per active tree and the urgent-path acceptance owner
   -> TreeBlueprints[]            reusable tree-level slots, LOD profile links, and static promotion costs
   -> BlueprintBranchPlacements[] reusable blueprint-local branch placements stored once per blueprint
   -> BranchPrototypes[]          reusable branch split-tier meshes and packed leaf tint
   -> SpatialGrid                 tree-cell ownership for visibility classification
```

### Per-Camera / Per-Frame Submission

```text
Camera
-> VegetationRendererFeature depth/color pass setup
-> VegetationActiveAuthoringContainerRuntimes.GetActive()
-> AuthoringContainerRuntime.PrepareFrameForCamera()
-> VegetationGpuDecisionPipeline.PrepareResidentFrame()
   -> ClassifyCells
   -> ClassifyTrees
   -> AcceptTreeTiers       mandatory TreeL3 floor first, then nearest-first promotion
   -> GenerateExpandedBranchWorkItems
   -> ResetSlotCounts
   -> CountTrees
   -> CountExpandedBranches promoted-tree-only branch work
   -> BuildSlotStarts
   -> EmitTrees
   -> EmitExpandedBranches  promoted-tree-only branch work
   -> FinalizeIndirectArgs
-> residentInstanceBuffer            packed visible instances
-> residentArgsBuffer                indirect args per draw slot
-> slotPackedStartsBuffer            packed range start per draw slot
-> slotEmittedInstanceCountsBuffer   non-zero active-slot source
-> VegetationIndirectRenderer.BindGpuResidentFrame()
-> for each non-zero emitted draw slot:
   bind shared buffers
-> URP Depth Pass: DrawMeshInstancedIndirect per active draw slot after bind
-> URP Color Pass: DrawMeshInstancedIndirect per active draw slot after bind
-> final URP indirect submissions
```

### Static Registry Owners vs Per-Frame Worklists

Static registry owners:

1. `SpatialGrid`
   Owns tree-to-cell registration and visible-cell query boundaries. It does not own branch decode or final submissions.
2. `TreeInstances[]`
   Owns per-tree static scene registration, tree bounds, blueprint handles, and the urgent-path acceptance identity.
3. `TreeBlueprints[] + BlueprintBranchPlacements[]`
   Own reusable tree-level slots, promotion costs, and blueprint-local branch placement data without duplicating branches per scene tree.
4. `BranchPrototypes[]`
   Own reusable branch module draw-slot handles for `L0` source meshes plus split canopy/wood tiers for `L1/L2/L3`.
5. `DrawSlots[]`
   Own final submission identities only: exact `Mesh + Material + MaterialKind` buckets and per-slot indirect-args layout.

Per-frame worklists:

1. `cellVisibilityBuffer`, `treeVisibilityBuffer`, and `_ExpandedBranchWorkItems`
   Frame-local urgent-path acceptance and promoted-tree branch work derived from the static registry.
2. `residentInstanceBuffer` and `residentArgsBuffer`
   Frame-local accepted-content outputs used for indirect submission.
3. `slotEmittedInstanceCountsBuffer` and `ActiveSlotIndices`
   Final submission compaction state that filters the renderer down to the non-zero emitted slot set.

### What This Means Operationally

1. `VegetationRuntimeContainer.maxVisibleInstanceCapacity` limits packed visible instances, not draw slots.
2. `DrawSlots.Count` controls how many potential indirect submissions exist for a container, but only the non-zero emitted subset is submitted in the current urgent path.
3. Runtime memory no longer scales with pre-populated scene-wide branch ownership. Branch work exists only for trees that already promoted above `TreeL3`.
4. Dense-forest survival is decided before slot packing: every visible non-far tree is accepted to at least `TreeL3`, and only then can nearer trees spend budget on branch-expanded detail.

## Runtime Pipeline

1. Registration is `serialized authorings -> VegetationTreeAuthoringRuntime[] -> VegetationRuntimeRegistry`.
2. Per-camera work is `camera -> GPU classification/emission -> shared instance buffer + indirect args`.
3. Final rendering is `shared instance buffer + indirect args -> depth pass + color pass -> one indirect submission per active draw slot per pass`.
4. `changed transforms / hierarchy / blueprint data -> RefreshRuntimeRegistration() -> rebuilt runtime state`

## Important Limitations

1. Opaque-only runtime: no transparency, no alpha clip, no masked foliage.
2. URP only (via new render graph API). Built-in pipeline, HDRP, and custom SRP integrations are outside the contract.
3. No runtime CPU fallback, no CPU decode bridge, and no async-readback rendering path.
4. Exact CPU-side visible-instance lists are not exposed in production flow.
5. `LODGroup` is not used; LOD comes from authored distance bands and GPU classification.
6. Registration is frozen after enable. Changes require `RefreshRuntimeRegistration()`. Transform changes (position/rotation/scale) do not auto-sync (not even in editor). Either enable/disable the container (script) or force update via `RefreshRuntimeRegistration()`.
7. Each container owns only active authorings from its serialized list, and nested child containers own their own descendants.
8. Runtime diagnostics are renderer-wide through `VegetationFoliageFeatureSettings.EnableDiagnostics`.
9. `maxVisibleInstanceCapacity` is per-container, not global. Multiple containers do not share one packed visible-instance buffer.
10. Splitting one large forest across multiple containers can avoid one-container overflow, but it does not create a global coordinator. Memory, buffers, and capacity all scale with the number of visible containers.
11. The urgent runtime now reprioritizes inside one container with `TreeL3` floor plus nearest-first promotion, but there is still no global cross-container arbiter.
12. Multi-container prioritization stays unresolved follow-up work; the dense-forest one-container design authority remains [../../DetailedDocs/urgentRedesign.md](../../DetailedDocs/urgentRedesign.md).
13. Closed `SubScene` runtime loading requires `SubSceneAuthoring` on the same GameObject as `VegetationRuntimeContainer`; the plain container alone is only the classic-scene lifecycle provider.

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
4. Urgent runtime scaling redesign: [../../DetailedDocs/urgentRedesign.md](../../DetailedDocs/urgentRedesign.md)

## License

Package license: [LICENSE.md](LICENSE.md)

## Kudos

Big thanks to the [unity-voxel](https://github.com/mattatz/unity-voxel) and it's author @mattatz !
