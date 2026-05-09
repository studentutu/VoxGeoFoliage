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
-> editor-compiled representation packets
-> packet-level global active budgets
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
no per-frame branch work generation as the final architecture
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

   Final target removes mandatory per-tree `TreeL3`. `TreeL3` is a current-runtime artifact and must not survive the full packet migration as a far-field or non-far floor.

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
   max selected packet count
   max selected instance count
   max frame command records
   max approximate vertex/index work
   max near-detail resident bytes
   max near-detail upload bytes per frame
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

11. Editor-compiled render packets.

   The final runtime should not generate branch work every frame. The editor compiler should precompute draw-ready representation packets:

   ```text
   RepresentationPacket
     owner page/cell/tree
     representation kind
     asset group
     first static instance
     instance count
     bounds
     screen-error metric
     cost
     shadow packet mapping
     wind metadata range
   ```

   Runtime then selects packets, not branches. This moves the expensive tree/branch expansion math out of the frame loop and turns the renderer into a packet scheduler.

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

8. Iteration 6 [RECOMMENDED]: editor-compiled packet renderer.

   Changes:

   - compile page/cell/tree representations into immutable packet ranges
   - pre-expand branch draw instances into page blobs when memory budgets allow
   - keep high-detail `L0/L1` packet streams cold or near-resident instead of globally GPU-resident
   - select packets at runtime by page/cell visibility, screen-error band, and global budget
   - submit static instance ranges instead of packing a fresh visible-instance buffer every frame
   - derive shadow packets from accepted color packets

   Result:

   - runtime cost scales with visible pages/cells and selected packets, not raw tree branches
   - branch placement multiplication, bounds aggregation, draw-slot grouping, shadow caster mapping, and wind metadata packing happen in the editor
   - BRG becomes a realistic optional backend for compiled packet ranges, not a separate vegetation architecture

   This is the new target. The old runtime branch-work generator is replaced, not maintained as a parallel path.

## recommended combined design

### Final target: editor-compiled packet scene with HLOD, grouped submission, and CullingGroup broad phase

The right generational design is:

```text
Authoring graph
-> Editor compiler
-> FoliageAssemblyAsset + FoliagePageAsset
-> immutable RepresentationPacket ranges
-> VegetationRenderWorld
-> global packet scene
-> one frame decision per camera
-> dependent shadow decision
-> grouped RenderMeshIndirect submission
```

Containers remain authoring and streaming boundaries. They stop being renderers.

The final runtime should not rebuild tree/branch draw work. It should select already compiled packets.

```text
runtime hot path:
  page/cell culling
  packet LOD selection
  packet budget admission
  packet command emission
  draw submission

not runtime hot path:
  branch placement expansion
  bounds aggregation
  HLOD mesh generation
  draw-slot discovery
  shadow caster LOD authoring
  wind metadata generation
  per-frame visible-instance compaction
```

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
    - expands tree/branch representation packets
    - groups packet ranges by AssetGroup
    - builds cheap shadow packet mappings
    - packs wind phase/amplitude metadata
          |
          v
  FoliageAssemblyAsset
    - reusable species/blueprint render data
    - branch/tree representation costs
    - compatible mesh/material groups
    - reusable representation templates
          |
          v
  FoliagePageAsset[]
    - page bounds and cells
    - quantized tree transforms
    - species/assembly references
    - page/cell HLOD assets
    - static upload ranges
    - compiled color/shadow packet ranges
    - hot/cold detail stream metadata
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
    - representation packet table
    - static instance range table
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
    - GPU frustum validation for accepted pages/cells
    - no HZB dependency in the production baseline
    - screen-error packet choice
    - global budget arbitration
          |
          v
  Accepted packet stream
    - PageHLOD packets
    - CellHLOD packets
    - TreeL2/L1/L0 packets for expanded cells
    - no per-frame branch work generation
          |
          v
  Command emission
    - color/depth commands reference static instance ranges
    - shadow commands reference mapped shadow packets
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
RepresentationPackets provide draw-ready static ranges.
VegetationRenderWorld owns budgets, frame scratch, decisions, and submission.
```

### Production baseline contract

The production-ready target is the complete non-HZB path:

```text
BranchPrototypeSO + TreeBlueprintSO
-> FoliageAssemblyAsset
-> FoliagePageAsset[]
-> RepresentationPacket table
-> VegetationRuntimeContainer / SubScene / procedural provider
-> VegetationRenderWorld
-> CullingGroup page/cell broad phase
-> screen-error packet selection
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
runtime does not expand branch placements in the final architecture
runtime does not discover draw slots in the final architecture
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

### Editor-prepared packet model

The compiler should move every stable calculation out of the frame loop.

Compile-time responsibilities:

```text
partition authorings into pages and cells
split pages/cells that exceed count, bounds, or memory caps
build page/cell HLOD meshes
precompute page/cell/tree bounds and culling spheres
precompute screen-error thresholds and hysteresis bands
precompute representation costs and value buckets
precompute AssetGroup ids from mesh/material/pass contract
precompute per-representation draw packets
precompute cheap shadow packet mapping
precompute wind metadata ranges
pre-sort packet ranges by AssetGroup
quantize static transforms relative to page origin
estimate GPU/CPU bytes per page and per detail stream
```

Runtime responsibilities:

```text
load page headers
upload required static packet streams
consume CullingGroup page/cell visibility
select packets by broad-phase band, screen error, and budget
write command records or BRG draw commands
submit grouped draws
sample telemetry
```

Runtime should not do:

```text
branch placement traversal
tree-to-branch work-list generation
per-frame bounds aggregation
per-frame draw-slot lookup
per-frame material compatibility checks
per-frame HLOD selection by raw branch count
per-frame CPU-visible active-slot readback
```

Packet record:

```text
FoliageRepresentationPacket
  packet id
  owner page id
  owner cell id
  representation kind
  residency class
  asset group id
  first instance
  instance count
  bounds
  culling sphere
  max screen error
  cost units
  value bucket
  color pass flags
  depth pass flags
  shadow packet id
  wind metadata offset
```

Residency classes:

```text
AlwaysResident:
  page headers
  page/cell bounds
  PageHLOD packets
  CellHLOD packets
  cheap shadow HLOD packets

NearResident:
  TreeL2 packets
  TreeL1 packets
  TreeL0 packets

ColdBlob:
  high-detail per-tree/branch packet streams not currently uploaded
```

Do not upload every `L0/L1` branch-expanded instance stream for 1M loaded trees. That only moves the performance failure into memory. High-detail packet streams are compiled in the editor but uploaded at cell/page granularity only when a near-ring policy can pay for them.

Simplified runtime invariant:

```text
loaded page count can be high
uploaded high-detail cell count must be bounded
selected packet count must be bounded
submitted command count must be bounded
```

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
  Used for distant pages and emergency overload representation.

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

Final target removes mandatory per-tree `TreeL3`. Once the packet renderer lands, far coarse vegetation is represented by `CellHLOD` or `PageHLOD`, not by one whole-tree proxy per tree.

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
representation templates
asset group references
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
representation packets
packet-to-shadow mappings
near-detail stream offsets
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
packet bounds contain all instances referenced by the packet
packet instance ranges are contiguous per AssetGroup
packet cost is monotonic across LOD detail
shadow packet bounds are not larger than the color packet it can replace, except documented tolerance
near-detail stream bytes stay below configured per-cell cap
```

Compiler output should include a build report:

```text
page count
cell count
tree count
always-resident bytes
near-detail cold bytes
max page bytes
max cell bytes
max packet count per page
max packet count per cell
AssetGroup count
estimated color command upper bound
estimated shadow command upper bound
HLOD triangle budgets
validation failures
```

If the compiler cannot fit a page/cell into caps, it must split or fail at authoring time. Runtime must not discover that a page is too large during rendering.

### Frame pipeline

Recommended pipeline:

```text
BeginFrame
-> Register active pages from streaming providers
-> Prepare camera constants
-> Consume CullingGroup page/cell visibility and distance bands
-> Select page/cell packets by screen error
-> Apply global packet budgets
-> Request near-detail stream uploads for future frames if needed
-> Emit color/depth commands from accepted packet ranges
-> Emit dependent shadow commands from accepted packet mappings
-> Submit grouped indirect commands
-> Capture async telemetry
```

### LOD selection

Use screen-space error and global budgets:

```text
for each page:
  if projected page error <= pageHlodError:
    accept PageHLOD packet
    skip cells and trees

for each cell in accepted page:
  if projected cell error <= cellHlodError:
    accept CellHLOD packet
    skip trees

for each tree in accepted cell:
  choose candidate packet by projected tree error
  admit candidate packet by value/cost bucket
```

Priority should be bucketed, not globally sorted with expensive dynamic sort:

```text
bucket = screen coverage band + distance ring + packet benefit
```

This keeps the GPU path simple and avoids O(N log N) or O(N^2) behavior.

If a high-detail packet is not resident yet:

```text
use current resident lower-detail packet this frame
queue near-detail stream upload
upgrade only after upload completes
apply hysteresis to avoid flicker
```

### Budget behavior

Budgets are global and hierarchical:

```text
Near safety budget:
  guarantees close visible trees get at least CellHLOD coverage or TreeL2 when the cell expands to individual trees.

HLOD budget:
  page/cell aggregates cover dense far-field.

Tree budget:
  individual tree packets admitted by screen value.

Near detail budget:
  near-detail packet residency and high-detail packet admission only.

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

The final design should not have a per-frame branch work budget because the frame loop should not generate branch work. It should have:

```text
near-detail upload budget
near-detail resident byte budget
selected packet budget
selected instance budget
selected command budget
shadow packet budget
```

### Broad-phase and occlusion strategy

Unified default path:

1. Use `CullingGroup` for page/cell sphere visibility and distance bands.
2. Use GPU frustum checks as validation for cells accepted by the CPU broad phase.
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
runtime writes or enables one command buffer per AssetGroup
CPU submits one RenderMeshIndirect per AssetGroup per pass
inactive commands have instanceCount = 0
```

This removes production dependency on CPU active-slot readback.

Packet submission model:

```text
static instance buffer per AssetGroup
  contains compiled page/cell/tree packet instance ranges

command buffer per AssetGroup/pass
  contains one command per accepted packet range or merged adjacent ranges

shader instance lookup
  uses start instance + instance id to read the static packet range
```

The runtime should not copy accepted instances into a fresh visible-instance buffer every frame unless profiling proves static packet ranges are worse on a target API.

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

With editor-compiled packets, BRG becomes simpler:

```text
BRG culling callback:
  consume selected packet ranges
  emit visible instance ranges
  emit compact BatchDrawCommand records
```

That is an optional backend experiment over the same packet data. It must not create a second authoring model or second LOD/shadow policy.

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
accepted color packet
-> shadow packet mapping
-> cascade 0 same-as-color for near L0/L1
-> cascades 1+ cheap tree or HLOD shadow
-> offscreen camera-radius ring defaults to 5 m
```

Add page/cell HLOD shadows:

```text
PageHLOD color packet -> PageHLOD shadow packet if within shadow distance
CellHLOD color packet -> CellHLOD shadow packet
Tree color packet -> fixShadows packet mapping
```

Shadow packet mapping is compiled in the editor. Runtime can reject or enable shadow packets, but it must not choose a different geometry family from scratch.

### Wind

Wind must not become a compute animation system by default.

Recommended:

```text
L0/L1:
  shader procedural branch/trunk bend from packed per-instance phase and compiled branch metadata

L2:
  cheap trunk sway / canopy sway

CellHLOD/PageHLOD:
  low-frequency vertex displacement or no wind

Shadow:
  same wind function for same-as-color cascade 0
  cheap casters can use reduced or no wind with documented mismatch tolerance
```

Do not add bone animation or per-vertex compute deformation until the static renderer is stable and profiled.

Wind authoring data should be compiled into packet streams:

```text
per species:
  wind profile id
  stiffness bands
  bend amplitude limits

per packet instance:
  phase
  amplitude scalar
  packed variation
  optional branch anchor metadata
```

Runtime only updates global wind constants. It does not rebuild instance data when wind changes.

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

Compiled page blobs should be SoA where runtime scans many records:

```text
page records
cell records
packet records
packet costs
packet bounds
packet residency states
```

Keep AoS only for final shader instance payload because shaders consume it directly.

Quantize static transforms in page assets where acceptable:

```text
position relative to page origin
rotation compressed
scale quantized to existing 0.25 step rule
```

This reduces memory bandwidth, which matters more than micro-optimizing C# wrappers.

Final static instance data:

```text
AssetGroupBuffer
  static compiled instance payloads sorted by page/cell/packet

PacketRange
  assetGroup
  firstInstance
  instanceCount

FrameCommand
  packetRange reference
  pass flags
  instanceCount or zero
```

Do not duplicate the same transform data in both tree records and draw-ready instance records unless the compiler report proves the runtime savings justify the memory. Prefer:

```text
HLOD streams:
  always resident and draw-ready

near tree streams:
  draw-ready but cold until near cells request upload

tree metadata:
  compact culling/LOD data only
```

### Diagnostics

Required frame telemetry:

```text
active pages
visible pages
uploaded near-detail streams
selected packet count
selected instance count
page HLOD packets accepted
cell HLOD packets accepted
individual tree packets accepted
L0/L1/L2 counts
CellHLOD/PageHLOD counts
TreeL3 migration count
Impostor count
legacy branch work count
color commands
shadow commands by cascade
instance buffer usage
command buffer usage
near-detail resident bytes
near-detail upload bytes
occlusion rejected pages/cells
budget pressure reason
degradation level used
GPU buffer bytes
```

During replacement validation, track `TreeL3` and legacy branch work only to prove they are deleted from the final path. Final builds must report `TreeL3 migration count = 0` and `legacy branch work count = 0`.

Diagnostics must be async and sampled. No synchronous readback in render callbacks.

### Full migration plan

This is a replacement migration, not a staged maintenance plan. The old tree-first runtime is source material and a reference for behavior only. It must not become a supported bridge, fallback, or alternate renderer.

Hard migration rules:

```text
no long-lived dual runtime
no old/new renderer toggle in production
no draw-slot backend kept after cutover
no runtime compatibility shim for old tree-first assets
no production `TreeL3` floor
no production branch-work generator
no HZB work before the packet renderer is production-ready
no BRG fork that changes authoring, LOD, shadow, or wind policy
```

Allowed implementation mechanics:

```text
short-lived scaffolding inside the same change series
one editor asset-upgrade command for existing scenes/assets
test-only comparison helpers that are deleted before production acceptance
```

Rollback policy:

```text
rollback is source-control rollback
not a runtime mode
not a compatibility layer
not a second renderer
```

#### Cutover 1: new compiled asset contract

Build the final asset contract first. Do not retrofit the old registry.

1. Add `FoliageAssemblyAsset`.
2. Add `FoliagePageAsset`.
3. Add `FoliageRepresentationPacket`.
4. Add `AssetGroup` as the final mesh/material/pass identity.
5. Add compiler build reports for pages, cells, packets, memory, and command upper bounds.
6. Add one editor upgrade command that converts current container authorings into page assets.

Delete at this cutover:

```text
runtime draw-slot discovery from live authorings
runtime material compatibility discovery
runtime assumptions that authoring objects are render input
```

Acceptance:

```text
compiled output is deterministic
page/cell caps are enforced before Play Mode
packet ranges are contiguous per AssetGroup
oversized pages/cells split or fail in the compiler
existing demo content can be upgraded by the editor command
```

#### Cutover 2: HLOD, shadow, and wind compiled into packets

Compile all stable vegetation representation data in the editor.

1. Bake species-specific `CellHLOD` and `PageHLOD`.
2. Compile always-resident HLOD packet streams.
3. Compile bounded near-resident `TreeL2/L1/L0` packet streams.
4. Compile cheap shadow packet mappings.
5. Compile wind metadata into packet payloads.
6. Compile validation for packet bounds, shadow bounds, packet cost monotonicity, and near-detail stream bytes.

Delete at this cutover:

```text
mandatory far/non-far `TreeL3` contract
independent `ShadowProxyL0/L1` production path
runtime shadow LOD promotion
runtime branch placement expansion for final rendering
```

Acceptance:

```text
far dense forest cost depends on PageHLOD/CellHLOD packets
shadow packet bounds are not larger than allowed color packet bounds
wind changes update shader constants only
near-detail packet streams can remain unloaded until near cells request them
```

#### Cutover 3: global packet render world

Replace per-container renderer ownership with one render world.

1. Add `VegetationRenderWorld`.
2. Convert containers, SubScenes, and procedural outputs into page providers.
3. Load page headers and page packet streams through the render world.
4. Add near-detail stream residency limits.
5. Move all budgets, packet selection, telemetry, and command emission to the render world.
6. Add page/cell `CullingGroup` broad phase for the render world.
7. Use one conservative stereo packet decision for VR.

Delete at this cutover:

```text
per-container GPU decision pipeline ownership
per-container render budgets
camera/frustum duplicate full pipeline residency
container-owned draw submission
```

Acceptance:

```text
multiple containers feed one global budget
loaded pages and uploaded detail streams are reported separately
high-detail uploaded cell count is bounded
VR uses one shared packet LOD decision
runtime chooses packets, not branch work items
```

#### Cutover 4: grouped packet submission

Replace the old active-slot submission model.

1. Use static instance buffers per `AssetGroup`.
2. Emit command records from accepted packet ranges.
3. Submit grouped `RenderMeshIndirect` commands per `AssetGroup` and pass.
4. Derive depth and shadow commands from the accepted color packet decision.
5. Add `ShadowMode.Off` and `ShadowMode.CheapTree` as the only public shadow modes.

Delete at this cutover:

```text
old draw-slot backend
active-slot CPU readback as normal submission input
registered-slot warm-up fallback
legacy `RenderMainLightShadows`
legacy `AllowExpandedTreePromotionInShadows`
per-frame visible-instance packing for the final path
```

Acceptance:

```text
submission count is bounded by AssetGroup and accepted packet count
no synchronous GPU readback in render callbacks
no active-slot readback needed for production submission
cascade 0 uses same-as-color shadow packets where required
cascades 1+ use cheap HLOD/tree shadow packets
```

#### Cutover 5: delete obsolete runtime architecture

This cutover is mandatory. The redesign is not complete while old runtime architecture remains as a maintained path.

Delete or fully repurpose:

```text
VegetationGpuDecisionPipeline as tree-first frame authority
tree-first accepted-tier runtime as production renderer
expanded branch work-item production path
mandatory `TreeL3` baseline-fit logic
per-container runtime budget ownership
old shadow proxy LOD family
old active-slot submission path
obsolete docs that describe the old path as production target
```

Keep only if rewritten around packets:

```text
authoring ScriptableObjects
editor mesh bake utilities
opaque material validation
SubScene/classic-scene provider concepts
URP renderer feature shell
diagnostics surface
```

Acceptance:

```text
there is one production vegetation renderer
there is one public shadow policy surface
there is one packet asset contract
there is one global budget owner
there is no old/new runtime selection in user settings
```

#### Cutover 6: production verification without HZB

This cutover blocks release.

Required scenarios:

```text
100k loaded instances in one page set
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
no per-container budget fights
no separate mobile/desktop renderer path
bounded command count by AssetGroup and packet count
bounded near-detail resident bytes
stable shared stereo LOD
far field represented by PageHLOD/CellHLOD packets
TreeL3 migration count = 0
legacy branch work count = 0
wind does not allocate or add runtime material clones
telemetry reports active pages, uploaded detail streams, accepted packets, budget pressure, shadow commands, and buffer usage
```

Production failure criteria:

```text
renderer needs HZB to hit baseline scenes
old renderer remains selectable
TreeL3 remains required for far-field scalability
runtime branch work generation remains required
all L0/L1 detail for 1M loaded trees must be GPU-resident
shadows select finer/larger LOD than color
VR uses divergent per-eye LOD
active-slot CPU readback is needed for normal submission
```

#### Post-release candidates only

These are not part of the migration and must not block deletion of the old runtime:

```text
BRG backend over the same packet data
HZB page/cell accelerator after wind and packet verification
mixed-species HLOD aggregates if draw count proves it is needed
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
