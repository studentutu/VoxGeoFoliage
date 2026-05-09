# Vegetation Generational Redesign

Purpose: redesign proposal for scalable, fast, opaque-only vegetation that supports LOD, shadows, and dense scenes without turning every container into its own renderer.

Status: design proposal. No implementation has been made from this document yet.

Target:

- Opaque-only vegetation. No alpha clip, no transparency, no masked runtime materials.
- Authoring tree supports reusable branch/tree authoring, compiled page/container assets, and procedural placement outputs.
- Rendering container/runtime path supports 100k to 1M loaded instances with streaming.
- LOD and shadows stay deterministic and tier-consistent.
- Active budgeting, cheap shadows, and wind are production baseline scope.
- Runtime remains GPU-resident and avoids synchronous GPU readback in render callbacks.
- Dense scenes degrade by representation quality, not by undefined drops, device removal, or hidden per-container budget fights.
- Mobile and VR are first-class targets. Avoid per-pixel-perfect visibility as a required path; prefer conservative page/cell/tree-sphere decisions with stable hysteresis.
- HZB is not part of the production baseline. It is a post-wind improvement only, and only if target-device profiling proves it beats the simpler page/cell broad phase.

Production baseline:

```text
authoring branch/tree graph
-> compiled assembly/page assets
-> streaming container/page provider
-> global VegetationRenderWorld
-> CullingGroup page/cell broad phase
-> page/cell HLOD + tree expansion
-> active global budgets
-> CheapTree shadows
-> shader wind
-> grouped indirect submission
```

Explicit baseline non-goal:

```text
no HZB
no per-pixel-perfect occlusion dependency
no separate desktop renderer
no far-field per-tree floor
```

## critical flaws

1. The current renderer scales per container, not per scene.

   The current design lets each `VegetationRuntimeContainer` own its own registry, GPU decision pipeline, instance buffers, indirect args, shadow pipeline, and budgets. That is tolerable for one dense container. It becomes unstable once a large forest is split into multiple streaming chunks.

   Current failure mode:

   ```text
   visible containers increase
   -> duplicate static buffers and scratch buffers
   -> independent budgets
   -> no global near priority
   -> memory and work scale with active chunk count
   ```

   Chunking should improve streaming and culling. In the current design, chunking also multiplies renderer residency.
   Not only memory consumption increasing, but per container budget no longer make any meaning!
   Performance tanks with multiple containers!

2. The baseline representation still scales with visible tree count.

   `TreeL3` as a mandatory non-far floor is better than dropping trees, but it is not enough for dense forests. A valley full of visible trees still becomes one accepted tree representation per tree. There is no page/cell HLOD that collapses many far trees into a smaller opaque aggregate.

   This is the scalability ceiling. You cannot make an infinite forest fast if the minimum unit of visible distant vegetation remains one tree instance.

3. LOD is distance-band driven instead of screen-error and budget driven.

   Distance bands are easy to author but weak operationally. They do not account for:

   - tree scale
   - field of view
   - screen resolution
   - camera height
   - how much a tree contributes to final pixels
   - current frame budget pressure

   The runtime has approximate work-unit budgets, but the tier request is still distance-first. Production LOD should be screen-error-first, then budget-constrained.

4. Color, depth, and shadow are not one frame decision.

   Depth/color cache one prepared camera view. Shadow still prepares a separate explicit-frustum path. The shadow redesign in `fixShadows.md` addresses correctness, but the broader issue remains: all passes should consume one authoritative frame decision where possible.

   Correct direction:

   ```text
   one camera frame decision
   -> color/depth submission
   -> dependent cheap shadow submission
   ```

5. Submission still depends on a CPU-visible active-slot concept.

   The current active-slot list is derived from async GPU readback and can fall back to all registered slots. That is a useful transitional trick, not a scalable renderer architecture.

   Production submission should not need CPU knowledge of which slot emitted instances this frame. The GPU should write indirect command records and zero inactive commands. CPU submission should be stable and bounded by asset groups, not by per-frame readback.

   This also causes crashes or stalls on mobile tile-based rendering and multi-eye VR paths.

6. Current render-pass integration fights URP instead of using it cleanly.

   The shadow pass uses an unsafe render-graph pass. Unity documents that unsafe passes can prevent render graph optimizations and can be slower because URP cannot reason about the pass as precisely:

   - https://docs.unity.cn/Manual/urp/render-graph-unsafe-pass.html

   Unsafe shadow integration might be unavoidable short term, but the design must minimize custom unsafe work and keep it isolated.

7. The authoring model is good, but the runtime artifact is still too close to authoring data.

   `TreeBlueprintSO -> BranchPlacement[] -> BranchPrototypeSO` is a good authoring graph. Runtime should not keep thinking in authoring units after bake. Runtime needs compiled render pages and representation assets:

   ```text
   authoring graph -> compiled foliage pages -> runtime GPU scene
   ```

   The current registry is a runtime snapshot, but not a full compiled scene artifact. It still forces frame-time decisions to expand tree/branch structures too directly.

   Keep in mind memory usages, as each expansion also cost memory (pre-allocation).

8. The profiler sample is not the dense failure case, but it exposes the shape.

   In the current profiler CSV, vegetation markers are small in the sampled frame, but there are already five `PrepareResidentFrame` calls and four frustum prepares around shadow work. That is acceptable in a tiny scene and wrong in a dense one. The problem is multiplicative structure, not just current milliseconds.

   This is biggest current design flaw.

9. Pages can become the next broken abstraction if they are treated as "just bigger containers."

   Pages are not mini renderers and not visual authoring units. They are compiled streaming/culling/HLOD units. If every page owns unique materials, independent budgets, independent command buffers, and arbitrary sizes, the redesign recreates the same container failure at a different layer.

   Required page invariant:

   ```text
   page = provider/upload/streaming unit
   render world = budget/culling/submission owner
   cell = LOD/culling subdivision inside page
   asset group = draw submission owner
   ```

## missing pieces

1. A global vegetation render world.

   Containers should become page providers, not independent renderers.

   Required ownership:

   ```text
   VegetationRenderWorld
     owns global static GPU scene buffers
     owns global frame scratch pools
     owns global budgets
     owns active page table
     owns camera frame decisions
     owns color/depth/shadow command buffers

   VegetationRuntimeContainer
     owns authoring references
     builds or references compiled pages
     streams pages into VegetationRenderWorld
   ```

2. Compiled foliage pages.

   A page is the runtime unit for streaming, culling, memory, and HLOD.

   ```text
   FoliagePage
     page id
     world bounds
     cells[]
     tree instances[]
     blueprint ids[]
     page HLOD meshes
     cell HLOD meshes
     static GPU ranges
     asset group references
   ```

   Containers can still exist for authoring and SubScene boundaries, but the renderer sees pages.

3. Cell/page HLOD.

   Required representation ladder:

   ```text
   PageHLOD      many trees -> one/few opaque aggregate meshes
   CellHLOD      cell cluster -> one/few opaque aggregate meshes
   TreeL2        trunkL3 + branch L2 canopy/wood
   TreeL1        trunk full + branch L1 canopy/wood
   TreeL0        trunk full + source branch wood/foliage
   ```

   Final target removes mandatory per-tree `TreeL3`. `TreeL3` is a migration-only compatibility fallback until page/cell HLOD exists.

4. Screen-error LOD.

   Runtime LOD should use projected bounds and authored error metrics:

   ```text
   projected radius / screen height
   projected error per representation
   cost per representation
   hysteresis band
   global quality budget
   ```

   Distance can remain as an authoring clamp, not the primary LOD selector.

5. Global budget arbitration.

   Per-container budget is a real-world failure. A forest visible across ten chunks needs one global admission decision.

   Required budgets:

   ```text
   max frame visible instances
   max frame branch work items
   max frame command records
   max frame approximate vertex/index work
   max near-ring tree count
   max cascade-0 same-as-color shadow casters
   max offscreen shadow ring casters
   ```

   Budget ownership belongs to `VegetationRenderWorld`.

6. Cross-platform broad-phase culling path.

   Unity's GPU occlusion culling works with GPU Resident Drawer and uses camera/light depth history to cull objects:

   - https://docs.unity.cn/6000.0/Documentation/Manual/urp/gpu-culling.html

   Custom indirect vegetation cannot directly rely on that path. The package needs a conservative broad-phase for pages/cells before tree work. That broad-phase must be mobile/VR-safe first.

   HZB is excluded from the production baseline. It can only be reconsidered after authoring, page/cell HLOD, active budgets, shadows, and wind are production-ready and verified on target hardware.

   We can still use CPU broad-phase culling through the `CullingGroup` API (see https://docs.unity3d.com/6000.0/Documentation/Manual/CullingGroupAPI.html)

   Correction: `CullingGroup` is not a general dynamic occlusion solution. Unity documents it as a way to integrate custom systems into Unity culling/LOD using bounding spheres. It supports one camera per group, distance bands, callbacks/query results, and async results that update only during camera culling. It uses frustum and static occlusion, not dynamic occluders.

   Use it as the primary cross-platform CPU broad phase for pages/cells. Do not use it as per-tree culling for 1M trees.

7. Mobile/VR quality constraints.

   The design must not require:

   - per-pixel-perfect HZB decisions
   - per-eye divergent LOD
   - extra full-depth passes by default
   - CPU readback for command compaction
   - high-frequency branch shadows outside cascade 0

   For VR, culling and LOD must use a conservative union of both eyes or a shared stereo decision. Per-eye LOD selection is a visual bug, not an optimization.

8. Stable GPU-built commands.

   Unity documents `Graphics.RenderMeshIndirect` as accepting command buffers that can be set up on CPU or GPU and can contain multiple commands:

   - https://docs.unity.cn/6000.2/Documentation/ScriptReference/Graphics.RenderMeshIndirect.html

   The redesign should move away from per-slot `DrawMeshInstancedIndirect` submission and toward grouped `RenderMeshIndirect` command buffers.

9. Shadow dependency from color decision.

   Shadow redesign lives in `fixShadows.md`. The generational design depends on it:

   ```text
   color accepted representation
   -> cheap shadow mapping
   -> no independent shadow LOD promotion
   ```

10. A compiled asset contract.

   The package needs a build artifact, not just ScriptableObject runtime snapshots:

   ```text
   FoliageAssemblyAsset
   FoliagePageAsset
   FoliageRenderAssetRegistry
   ```

   These artifacts are generated in editor and loaded by runtime. ScriptableObjects remain authoring inputs.

## best alternatives

1. Iteration 1: Patch the current renderer.

   Changes:

   - implement `ShadowMode.CheapTree`
   - fix active-slot compaction not being diagnostics-gated
   - split shadow budgets lower than color
   - remove slot-order clamping bias
   - pool prepared-view scratch buffers

   Result:

   - practical short-term improvement
   - lower shadow cost
   - fewer correctness bugs

   Rejected as final design because it still keeps independent container renderers and tree-count baseline scaling.

2. Iteration 2: Global renderer, same tree representations.

   Changes:

   - introduce `VegetationRenderWorld`
   - containers feed pages into one global renderer
   - one global camera decision
   - one global budget
   - shadow depends on color decision
   - grouped indirect submission

   Result:

   - fixes per-container budget fights
   - reduces duplicate buffers
   - makes dense scene behavior predictable

   Still not enough for very dense forests because every visible tree still bottoms out at a per-tree coarse representation.

3. Iteration 3 [USER-APPROVED]: Global renderer plus page/cell HLOD.

   Changes:

   - bake page and cell aggregate opaque meshes
   - add screen-error LOD
   - render distant dense vegetation as cell/page aggregates
   - only expand to individual trees when screen error and budget justify it

   Result:

   - tree count stops being the far-field cost driver
   - global budget can degrade gracefully
   - dense forest scalability becomes plausible

   This is the recommended practical target.

4. Iteration 4 [REJECTED AS BASELINE]: HZB-first culling.

   Changes:

   - build depth pyramid every frame
   - use HZB to reject page/cell/tree work
   - rely on GPU occlusion to make dense scenes fit

   Result:

   - useful later in occluder-heavy desktop scenes
   - wrong as the foundation for mobile/VR

   Rejected as baseline because it creates a per-pixel precision dependency before the core renderer is proven. It also risks hiding bad page sizing, missing HLOD, and weak budget policy behind an occlusion feature that may be disabled or slower on target hardware.

5. Iteration 5 [REJECTED]: Full virtualized foliage geometry.

   Changes:

   - build a Nanite-like cluster hierarchy
   - stream and select meshlets/clusters by screen error
   - GPU-driven cluster raster path

   Result:

   - strongest theoretical path

   Rejected for now. Unity URP does not give enough low-level control to justify cloning a virtualized geometry renderer inside this package. The project would become a renderer research project instead of a production vegetation package.

   Rejection:
   Unity already tried to implement that in the [com.unity.virtualmesh](https://github.com/Unity-Technologies/com.unity.virtualmesh). Unreal with Nanite does not specifically target mobile, and even in the best case scenario - doesn't improve perfomance (a slight degradation and more battery drainage).
   We also need to support Mobile and VR, so it is essential to stay away from per-pixel precision!

6. BRG / GPU Resident Drawer as main backend [USER-APPROVED].

   Unity says BRG is intended for high-performance custom SRP rendering and large numbers of environment objects:

   - https://docs.unity.cn/Manual/batch-renderer-group.html
   - https://docs.unity.cn/2022.1/Documentation/Manual/batch-renderer-group-how.html

   It is worth testing for page/cell HLOD or far static proxies. It should not replace the custom GPU-driven assembly path now because BRG culling is CPU-callback centered, uses a different shader/metadata contract, and would force a second renderer architecture before the current design is stable.

7. unityHISM BRG pattern [PARTIALLY USEFUL].

   Source reviewed: https://github.com/WestallZhu/unityHISM

   The useful pieces:

   - chunk-first runtime ownership
   - self-relative blob chunk format
   - async chunk streaming through `AsyncReadManager`
   - per-archetype BRG batch/sub-batch allocation
   - 64 KB constant-buffer windows for mobile-friendly per-instance data
   - Burst CPU BVH/frustum/LOD callback that emits BRG `visibleInstances` and `BatchDrawCommand` ranges
   - command run compaction by mesh/material/batch state

   The unsuitable pieces:

   - no vegetation authoring tree
   - no page/cell HLOD compiler
   - no global active quality budget
   - distance-threshold LOD, not screen-error plus budget
   - shallow BVH only works if chunks are already tightly bounded and capped
   - no wind contract
   - no shadow policy comparable to `CheapTree`
   - no million-instance proof or production telemetry
   - limited to BRG callback ownership, which does not match the current GPU-driven branch assembly path

   Verdict:

   ```text
   use HISM as a reference for BRG batch memory layout, streaming blobs, and CPU callback command emission
   do not import HISM as the vegetation renderer architecture
   do not replace the non-HZB production baseline with HISM
   ```

## recommended combined design

### Final target: page-based GPU scene with HLOD, grouped indirect submission, and CullingGroup broad phase

The right generational design is:

```text
Authoring graph
-> Editor compiler
-> FoliageAssemblyAsset + FoliagePageAsset
-> VegetationRenderWorld
-> global GPU scene
-> one frame decision per camera
-> dependent shadow decision
-> grouped RenderMeshIndirect submission
```

Containers remain authoring and streaming boundaries. They stop being renderers.

### Whole pipeline

High-level data transformation:

```text
AUTHORING TIME

  BranchPrototypeSO
    - source wood mesh
    - source foliage mesh
    - branch tier bake settings
    - materials
          |
          v
  TreeBlueprintSO
    - trunk mesh
    - branch placements
    - LOD/screen-error authoring limits
    - shadow/wind/material compatibility metadata
          |
          v
  VegetationRuntimeContainer / procedural placement source
    - scene placement ownership
    - streaming chunk boundary
    - authoring-only hierarchy
          |
          v
  Editor compiler
    - validates opacity/material contract
    - bakes branch tiers
    - bakes cell HLOD meshes
    - bakes page HLOD meshes
    - splits oversized pages/cells
    - computes bounds, costs, screen-error metadata
          |
          v
  FoliageAssemblyAsset
    - reusable species/blueprint render data
    - branch/tree representation costs
    - compatible mesh/material groups
          |
          v
  FoliagePageAsset[]
    - page bounds and cells
    - quantized tree transforms
    - species/assembly references
    - page/cell HLOD assets
    - static upload ranges
```

Runtime ownership flow:

```text
RUNTIME LOAD / STREAMING

  VegetationRuntimeContainer or SubScene provider
          |
          | loads compiled FoliagePageAsset[]
          | does not allocate render budgets
          | does not submit draw calls
          v
  VegetationRenderWorld.RegisterPages()
          |
          v
  Global static GPU scene
    - page table
    - cell table
    - tree table
    - assembly/representation table
    - asset group table
    - command layout
```

Per-frame render flow:

```text
FRAME N

  Camera / stereo camera pair
          |
          v
  Build conservative view input
    - mono: camera frustum + position
    - VR: union of both eyes
    - shared LOD decision for both eyes
          |
          v
  CullingGroup broad phase
    - page/cell bounding spheres
    - visibility
    - distance bands
    - async one-cull-late results
          |
          v
  VegetationRenderWorld frame decision
    - consume page/cell broad-phase masks
    - GPU frustum fallback for accepted pages/cells
    - no HZB dependency in the production baseline
    - screen-error representation choice
    - global budget arbitration
          |
          v
  Accepted representation stream
    - PageHLOD entries
    - CellHLOD entries
    - TreeL2/L1/L0 entries for expanded cells
    - branch work only for admitted high-value near trees
          |
          v
  Command emission
    - color/depth instance payloads
    - shadow payloads derived from accepted color state
    - grouped indirect command buffers per AssetGroup
          |
          v
  Submission
    - RenderMeshIndirect per AssetGroup/pass
    - no CPU active-slot readback required
    - async sampled telemetry only
```

Representation decision flow:

```text
PAGE
  |
  | if page projected error is low
  v
PageHLOD
  |
  | else inspect cells
  v
CELL
  |
  | if cell projected error is low or budget is tight
  v
CellHLOD
  |
  | else expand to individual trees
  v
TREE
  |
  | screen error + global budget
  +--> TreeL2
  +--> TreeL1
  +--> TreeL0
```

Shadow dependency flow:

```text
Accepted color representation
          |
          v
ShadowMode.CheapTree
    - cascade 0: same-as-color only for near L0/L1
    - cascades 1+: cheap HLOD/tree caster
    - offscreen 5 m ring: cheap caster only
    - no independent shadow LOD promotion
          |
          v
Grouped shadow indirect commands
```

The important ownership rule:

```text
Pages provide compiled data.
Cells provide culling and LOD subdivision.
AssetGroups provide draw grouping.
VegetationRenderWorld owns budgets, frame scratch, decisions, and submission.
```

### Production baseline contract

The production-ready target is the complete non-HZB path:

```text
BranchPrototypeSO + TreeBlueprintSO
-> FoliageAssemblyAsset
-> FoliagePageAsset[]
-> VegetationRuntimeContainer / SubScene / procedural provider
-> VegetationRenderWorld
-> CullingGroup page/cell broad phase
-> screen-error HLOD/tree selection
-> global active budget arbitration
-> grouped color/depth/shadow commands
-> shader wind
```

Hard contract:

```text
authoring data is editable input only
compiled assets are immutable runtime input
containers are streaming/page providers only
render world is the only owner of runtime budgets
render world is the only owner of GPU scratch and command emission
shadow casters are derived from accepted color representations
wind data is packed into representation/instance metadata
```

Scale contract:

```text
100k loaded instances:
  must run as the normal dense-scene case

1M loaded instances:
  must run with streaming pages and HLOD-dominant far field

near field:
  may expand to individual trees by budget and screen error

far field:
  must stay PageHLOD/CellHLOD unless budget and screen error justify expansion
```

This is the point of the redesign. If 1M loaded instances still require processing one tree representation per visible far tree, the redesign failed.

### Cross-platform rules

There should not be separate desktop and mobile/VR vegetation renderers. The project should use one conservative architecture that survives mobile/VR. Optional accelerators can be considered only after the production baseline is complete, and only when they do not fork ownership, data layout, or correctness rules.

```text
required:
  coarse page/cell culling before tree work
  bounded command counts
  one shared stereo LOD decision
  aggressive HLOD
  cascade-0-only detailed shadowing
  CPU CullingGroup page/cell broad phase

not required:
  HZB
  per-pixel occlusion precision
  perfect far-field geometric continuity
  per-tree far-field representation
```

Production readiness requires the conservative path to stand on its own. HZB cannot be used to compensate for weak page sizing, missing HLOD, missing budgets, or excessive shadow work.

VR rule:

```text
left eye visible OR right eye visible -> visible
left eye projected error OR right eye projected error -> use worst/larger error
one accepted tier for both eyes
```

Mobile tile-based GPU rule:

```text
avoid extra depth prepass by default
avoid readback-driven submission
avoid excessive compute passes before raster
prefer fewer, coarser HLOD draws in far field
```

### Core runtime ownership

```text
VegetationRenderWorld
  Static scene:
    page table
    cell table
    tree instance table
    blueprint table
    representation table
    asset group table
    draw command layout

  Frame scratch:
    visible pages
    visible cells
    visible trees
    accepted representations
    branch/cluster work
    color instance output
    shadow instance output
    command buffers
    frame counters

  Policies:
    global budgets
    LOD quality
    shadow mode
    occlusion mode
    telemetry mode
```

`AuthoringContainerRuntime` either disappears from the render hot path or becomes a provider adapter:

```text
VegetationRuntimeContainer
-> BuildRuntimeTreeAuthorings()
-> Compile or load FoliagePageAsset
-> Register pages with VegetationRenderWorld
```

### Representation ladder

The current tree tiers become part of a wider ladder:

```text
PageHLOD
  Many cells / many trees collapsed into an opaque aggregate.
  Used for distant pages and emergency overload fallback.

CellHLOD
  Many trees inside one spatial cell collapsed into an opaque aggregate.
  Used for mid/far dense cells.

TreeL2
  trunkL3 + branch L2 canopy/wood.

TreeL1
  trunk full + branch L1 canopy/wood.

TreeL0
  trunk full + source branch wood/foliage.
```

Rule:

```text
Do not expand to a finer representation unless its projected error and budget value justify the cost.
```

Final target removes mandatory per-tree `TreeL3`. `TreeL3` may remain only as a migration alias while the HLOD compiler is not ready. Once page/cell HLOD exists, far coarse vegetation should be represented by `CellHLOD` or `PageHLOD`, not by one whole-tree proxy per tree.

Sparse-cell edge case:

```text
if a cell contains one or very few trees:
  CellHLOD can be generated from those trees
  but it is still owned and budgeted as a cell representation
  not as the return of mandatory TreeL3
```

This keeps the runtime model simple: individual trees start at `TreeL2` when a cell expands to tree-level detail.

### Page rules

Pages must be deliberately boring.

Hard rules:

```text
page owns compiled data ranges
page does not own render budgets
page does not own scratch buffers
page does not own material variants
page does not submit draw calls
page has stable id and stable local coordinate origin
page bounds are conservative and validated
page has cells for culling/LOD subdivision
page load/unload is explicit and asynchronous
```

Page sizing rules:

```text
too large:
  bad culling
  HLOD pops over wide areas
  large upload/unload spikes
  bad VR stereo overdraw

too small:
  metadata explosion
  too many streaming handles
  too many HLOD meshes
  poor batching
  page table churn
```

Start target:

```text
page size:
  32 m to 64 m square for dense ground vegetation/forest chunks

cell size inside page:
  8 m to 16 m depending on tree size

page tree count:
  bounded by build-time validation
  overflow splits page before runtime

page HLOD:
  species-specific first
  no mixed-material aggregate until draw count proves it is needed
```

Do not use page boundaries as visible LOD boundaries without hysteresis. Use overlap or stable transition bands so the camera does not see a whole page switch at once.

### Editor compiler

Add a compiler layer:

```text
TreeBlueprintSO + BranchPrototypeSO + placements
-> FoliageAssemblyAsset

VegetationRuntimeContainer authorings or procedural placement output
-> FoliagePageAsset[]
```

`FoliageAssemblyAsset` stores reusable per-species data:

```text
branch prototype render tiers
tree tiers
representation costs
bounds
screen-error metadata
material compatibility metadata
wind metadata
shadow caster metadata
```

`FoliagePageAsset` stores world/chunk data:

```text
page bounds
cell bounds
tree transforms, quantized where acceptable
tree blueprint indices
cell HLOD meshes
page HLOD meshes
static GPU upload ranges
```

This turns runtime from "rebuild from live authoring graph" into "load compiled render data."

Compiler must validate:

```text
page bounds contain all cells
cell bounds contain all assigned tree bounds
HLOD bounds stay inside source page/cell bounds + tolerance
HLOD triangle budgets are monotonic
page tree count below configured cap
cell tree count below configured cap or recursively split
asset groups reuse shared meshes/materials
no per-page unique runtime material instances
```

### Frame pipeline

Recommended pipeline:

```text
BeginFrame
-> Register active pages from streaming providers
-> Prepare camera constants
-> Consume CullingGroup page/cell visibility and distance bands
-> Cull pages
-> Cull cells
-> Select page/cell/tree representations by screen error
-> Apply global budgets
-> Emit color/depth commands
-> Emit dependent shadow commands from accepted color state
-> Submit grouped indirect commands
-> Capture async telemetry
```

### LOD selection

Use screen-space error and global budgets:

```text
for each page:
  if projected page error <= pageHlodError:
    accept PageHLOD
    skip cells and trees

for each cell in accepted page:
  if projected cell error <= cellHlodError:
    accept CellHLOD
    skip trees

for each tree in accepted cell:
  choose candidate tier by projected tree error
  admit candidate by value/cost bucket
```

Priority should be bucketed, not globally sorted with expensive dynamic sort:

```text
bucket = screen coverage band + distance ring + representation benefit
```

This keeps the GPU path simple and avoids O(N log N) or O(N^2) behavior.

### Budget behavior

Budgets are global and hierarchical:

```text
Near safety budget:
  guarantees close visible trees get at least CellHLOD coverage or TreeL2 when the cell expands to individual trees.

HLOD budget:
  page/cell aggregates cover dense far-field.

Tree budget:
  individual tree representations admitted by screen value.

Branch budget:
  branch expansion only for high-value near trees.

Shadow budget:
  subordinate to color; cascade 0 same-as-color only, cheap tree elsewhere.
```

When overloaded:

```text
drop L0 -> L1
drop L1 -> L2
drop L2 -> CellHLOD
drop CellHLOD groups -> PageHLOD
never silently drop near required visibility
```

This is the difference between scalable degradation and brittle caps.

### Broad-phase and occlusion strategy

Unified default path:

1. Use `CullingGroup` for page/cell sphere visibility and distance bands.
2. Use GPU frustum checks as the authoritative fallback and for cells accepted by the CPU broad phase.
3. Never feed one sphere per tree into `CullingGroup` for dense scenes.
4. Treat results as async and one-cull-late.
5. Use hysteresis so page/cell transitions do not pop in VR.

Initial occlusion targets:

```text
PageHLOD bounds
CellHLOD bounds
expanded tree bounds
```

Current `TreeL3` bounds can be used only as migration data. In the final HLOD renderer, occlusion targets are page bounds, cell bounds, and individual expanded tree bounds.

Do not occlude individual branch work first. That is complexity in the wrong place.

Post-wind HZB improvement, not production baseline:

```text
not implemented before wind is complete
desktop/console first
disabled by default on mobile/VR
consumes previous-frame depth only after opaque occluders exist
never culls near safety ring by itself
requires two-frame confirmation before rejecting a page/cell
can be removed without changing page/cell/LOD ownership
```

If the non-HZB path cannot meet production targets, the page/cell/HLOD/budget design is still wrong. HZB is an accelerator, not a foundation.

`CullingGroup` integration:

```text
one CullingGroup per active camera or eye
shared BoundingSphere[] per page/cell table
preallocated query result arrays
distance bands match page/cell LOD bands
callbacks update persistent visibility state
query API is used only from non-render hot paths
results are treated as async and one-cull-late
```

For VR:

```text
left eye group OR right eye group -> visible
closest distance band across eyes -> chosen distance band
```

Do not feed `CullingGroup` one sphere per tree for 100k to 1M loaded trees. Feed page/cell spheres, then let GPU/tree-level logic run only inside accepted cells.

### Draw submission

Move toward:

```text
AssetGroup = mesh + material + pass contract
GPU writes one command buffer per AssetGroup
CPU submits one RenderMeshIndirect per AssetGroup per pass
inactive commands have instanceCount = 0
```

This removes production dependency on CPU active-slot readback.

BRG compatibility track, informed by unityHISM:

```text
AssetGroup/Archetype
  owns mesh + material + shader metadata contract

Page/Cell chunk
  owns immutable instance ranges and representation metadata

Batch window
  owns packed SoA instance data under Unity's per-batch buffer limits

Culling callback
  emits visible instance ranges and compact draw commands
```

This is useful for far static HLOD or fully compiled page/cell representations. It is not a drop-in replacement for near `L0/L1` branch-expanded vegetation unless the branch-expanded output is first turned into stable compiled representations or a separate BRG-compatible instance stream.

Adopt from HISM:

```text
stable chunk handles
sub-batch range allocator
asset/archetype grouped batch windows
command run compaction
zero-fixup binary chunk format idea
```

Reject from HISM:

```text
distance-only LOD thresholds
shallow BVH as a substitute for page/cell count caps
per-primitive far-field floor
renderer-owned streaming policy
no explicit active quality budget
no vegetation shadow/wind/material contract
```

Short-term bridge:

```text
existing draw slots remain
but active-slot compaction must run outside diagnostics
and zero-instance fallback must be bounded and measured
```

Final target:

```text
RenderMeshIndirect(commandBuffer, commandCount = registered commands for asset group)
```

The CPU command count can be stable per asset group. The GPU controls instance counts.

### Render ordering

Current depth-before-opaque behavior should be reconsidered.

Recommended default:

```text
opaque scene depth
-> vegetation color with ZWrite On
-> transparent / post
```

Optional mode:

```text
near vegetation depth prepass
-> vegetation color
```

Use the optional mode only if profiling proves vegetation self-overdraw costs more than duplicate vertex processing.

### Shadows

Use `fixShadows.md`:

```text
ShadowMode.Off
ShadowMode.CheapTree
```

Generational integration:

```text
accepted color representation
-> shadow representation mapping
-> cascade 0 same-as-color for near L0/L1
-> cascades 1+ cheap tree or HLOD shadow
-> offscreen camera-radius ring defaults to 5 m
```

Add page/cell HLOD shadows:

```text
PageHLOD accepted in color -> PageHLOD shadow if within shadow distance
CellHLOD accepted in color -> CellHLOD shadow
Tree accepted in color -> fixShadows mapping
```

### Wind

Wind must not become a compute animation system by default.

Recommended:

```text
L0/L1:
  shader procedural branch/trunk bend from packed per-instance phase and branch metadata

L2:
  cheap trunk sway / canopy sway

CellHLOD/PageHLOD:
  low-frequency vertex displacement or no wind

Shadow:
  same wind function for same-as-color cascade 0
  cheap casters can use reduced or no wind with documented mismatch tolerance
```

Do not add bone animation or per-vertex compute deformation until the static renderer is stable and profiled.

### Material contract

The package needs one explicit compatible-material contract:

```text
ForwardLit pass
DepthOnly pass
ShadowCaster pass
Vegetation indirect include
RSUV packed variation
optional wind function hook
no alpha clip
no transparency
```

Reject incompatible materials at validation. Do not clone materials at runtime.

### Data layout

Runtime buffers should be SoA where kernels scan many records:

```text
tree positions
tree radii
tree blueprint ids
tree page/cell ids
tree packed rotation/scale
```

Keep AoS only for final instance payload because shaders consume it directly.

Quantize static transforms in page assets where acceptable:

```text
position relative to page origin
rotation compressed
scale quantized to existing 0.25 step rule
```

This reduces memory bandwidth, which matters more than micro-optimizing C# wrappers.

### Diagnostics

Required frame telemetry:

```text
active pages
visible pages
page HLOD accepted
cell HLOD accepted
individual trees accepted
L0/L1/L2 counts
CellHLOD/PageHLOD counts
TreeL3 migration count
Impostor count
branch work count
color commands
shadow commands by cascade
instance buffer usage
command buffer usage
occlusion rejected pages/cells
budget pressure reason
fallback level used
GPU buffer bytes
```

During migration, track `TreeL3` only to prove it is disappearing from the final path. Final builds should report `TreeL3 migration count = 0`.

Diagnostics must be async and sampled. No synchronous readback in render callbacks.

### Migration plan

#### Phase 1: stop the bleeding

1. Implement `fixShadows.md`.
2. Make active-slot filtering production behavior, not diagnostics behavior.
3. Lower default shadow budgets.
4. Remove slot-order clamping bias.
5. Add telemetry for actual per-pass command count and fallback submission count.

Verification:

```text
current sample
dense single container
dense multi-container
shadows off
cheap shadows
scene view + game view
```

#### Phase 2: global render world

1. Add `VegetationRenderWorld`.
2. Keep existing container registration, but have containers register pages into the world.
3. Move GPU scratch pools and budgets to the world.
4. Prepare one global camera decision.
5. Shadow consumes color decision.

Verification:

```text
same visual result as current renderer
less duplicated GPU residency
one global budget visible in diagnostics
multi-container scene no longer multiplies full pipelines
```

#### Phase 2.5: CPU broad-phase safety net

1. Add page/cell `CullingGroup` provider as the primary broad-phase.
2. Use shared page/cell `BoundingSphere[]`.
3. Support one group per camera and two groups for stereo when needed.
4. Convert results into page/cell visible masks before GPU tree work.
5. Keep GPU frustum path as the authoritative fallback.
6. Keep a debug toggle to bypass `CullingGroup`, but do not build a second runtime architecture.

Verification:

```text
no allocations during query/callback handling
mobile scene can run without HZB
VR uses one shared accepted LOD per stereo pair
CullingGroup disabled path still works
```

#### Phase 3: grouped indirect backend

1. Introduce `AssetGroup`.
2. Build grouped command buffers.
3. Use `RenderMeshIndirect` where compatible.
4. Keep old draw-slot backend behind a temporary debug flag until parity is proven.
5. Remove old backend after parity.

Verification:

```text
same rendered instance counts
no active-slot readback needed for production submission
CPU draw submission bounded by asset groups
```

#### Phase 4: page/cell HLOD compiler

1. Add `FoliageAssemblyAsset`.
2. Add `FoliagePageAsset`.
3. Bake cell HLOD and page HLOD meshes.
4. Add validation for HLOD bounds and triangle budgets.
5. Add runtime page/cell HLOD selection before tree selection.
6. Make `TreeL3` a migration-only fallback.
7. Remove mandatory `TreeL3` after HLOD parity is proven.

Verification:

```text
far dense forest cost depends on pages/cells, not raw tree count
near ring still upgrades to individual trees
no transparent or billboard fallback
TreeL3 no longer required for far-field scalability
```

#### Phase 5: wind and material contract

1. Add shader wind hooks.
2. Add packed wind phase/amplitude.
3. Validate compatible materials.
4. Keep HLOD wind cheap.

Verification:

```text
same tier has same wind in color/depth/shadow where required
no new material clones
no alpha path
```

#### Phase 6: production verification gate without HZB

This phase blocks claiming the redesign is production-ready. It verifies the whole intended scope without HZB.

Required scenarios:

```text
100k loaded instances in one container/page set
1M loaded instances with streaming pages
dense multi-container scene
mobile target profile
VR stereo profile
shadows off
CheapTree shadows on
wind off
wind on
scene view + game view
```

Required pass conditions:

```text
no synchronous GPU readback in render callbacks
no per-container budget fights
no separate mobile/desktop renderer path
bounded command count by AssetGroup
bounded scratch buffer usage
stable shared stereo LOD
cascade 0 same-as-color self-shadowing only
cascades 1+ use cheap HLOD/tree casters
offscreen shadow ring defaults to 5 m
far field represented by PageHLOD/CellHLOD, not mandatory TreeL3
wind does not allocate or add runtime material clones
telemetry reports active pages, accepted HLOD/tree counts, budget pressure, shadow commands, and buffer usage
```

Production failure criteria:

```text
renderer needs HZB to hit baseline scenes
TreeL3 remains required for far-field scalability
shadows select finer/larger LOD than color
VR uses divergent per-eye LOD
mobile path requires extra per-pixel occlusion work
active-slot CPU readback is needed for normal submission
```

#### Phase 7: post-wind optional HZB accelerator

This is a future improvement only. Do not implement it before Phase 6 passes. It must plug into the same page/cell broad-phase masks and must not create a separate desktop renderer. If it is a performance degradation, or for mobile/VR targets, keep the async CPU `CullingGroup` page/cell broad phase as the only enabled path.

Allowed work only after production baseline:

```text
build or consume a depth pyramid after opaque occluders
occlude pages and cells first
add two-frame hysteresis
keep near safety ring unoccluded by HZB
keep HZB off by default on mobile/VR until target-device profiling proves otherwise
```

Verification:

```text
occluded forest behind terrain/buildings reduces accepted page/cell count
non-occluded scenes are not slower
no one-frame popping during camera turns
occlusion can be disabled for debugging
removing HZB leaves the production renderer functional
```

## open questions (numbered)

1. What is the real target density: 10k, 100k, or 1M placed tree instances per loaded scene? The recommended design can support each, but HLOD urgency changes.
Answer: 100k to 1M per loaded scene (with support of streaming, so we need some form of containers or similar alternatives).
2. Should page assets be authored from scene containers only, or also from procedural placement outputs? Recommendation: both, but the runtime should only see compiled pages.
Answer: we need to support streaming, so it is a must.
3. Is terrain/building occlusion important in target scenes? If yes, HZB can be a post-wind accelerator after the non-HZB renderer is production-ready. It must not be used to justify weaker page/cell/HLOD design.
Answer: HZB can wait until after wind and production verification. The working practical solution must stand without it.
4. Should far page/cell HLOD be species-specific or mixed-species aggregate? Recommendation: start species-specific for material/slot simplicity, then allow mixed aggregates only after metrics prove draw count is the bottleneck.
Answer: start species-specific for material/slot simplicity. We can always make a different containers for different species.
5. Should BRG be tested as a far-HLOD backend? Recommendation: yes as an experiment, no as the primary rewrite path.
Answer: if it helps eliminate fighting with URP and truly helps, then yes.
6. Should `TreeL3` remain non-far mandatory after HLOD lands? Recommendation: mandatory only inside the near safety ring and for cells that have expanded to individual trees. Far dense cells should be allowed to stay at `CellHLOD` or `PageHLOD`.
Answer: let's simplify to migrate to  `CellHLOD` or `PageHLOD` and remove `TreeL3`.
