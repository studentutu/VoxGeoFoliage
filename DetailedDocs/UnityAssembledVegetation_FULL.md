# Unity Assembled Vegetation System - Full End-to-End Plan

## 0. Purpose

This document defines the production target inspired by Unreal Engine 5.7 foliage innovations (Assemblies, voxelized LOD, and hierarchical wind animation) for a high-performance vegetation system in Unity 6 URP.

Current implementation note (`2026-04-12`):
- The old MVP CPU fallback / async-readback runtime bridge is retired.
- Production runtime is GPU-resident only through `VegetationRuntimeContainer`, `VegetationGpuDecisionPipeline`, and `VegetationIndirectRenderer`.
- `VegetationRuntimeContainer` now consumes an explicit serialized `VegetationTreeAuthoring` list. Every referenced authoring must live inside that container's hierarchy, and editor fill tooling excludes descendants claimed by nested child containers. This is the intended streaming/addressables contract.
- Runtime registration is still a frozen snapshot. Transform edits and other registration-affecting authoring changes require explicit `RefreshRuntimeRegistration()`.
- Runtime registration no longer duplicates per-scene shell-node world bounds or per-scene node-decision slices for every branch instance. Persisted shell hierarchies stay prototype-local.
- Runtime uses tree spheres from registration for coarse tree classification, then generates branch bounds and shell-node bounds on GPU per frame from prototype-local bounds plus branch transforms.
- Visible instances are now counted and packed into one hard-bounded shared GPU instance buffer every frame. `VegetationRuntimeContainer.maxVisibleInstanceCapacity` is the active runtime budget; overflow clamps instead of reallocating scene-scale per-slot or per-node memory.
- The urgent redesign authority for dense forests is prioritization-first: mandatory tree presence plus nearest-first promotion must land before any later occlusion or submission-count optimization work.
- Current shipped tree runtime shape is still flat at the tree level: one tree points to a contiguous `SceneBranchStartIndex + SceneBranchCount` span, not a full-tree hierarchy.
- Current shipped shell-node metadata is branch-local BFS only, and the compute shader does not yet use `firstChildIndex + childMask` for real subtree skip. It linearly scans the selected shell tier and only emits visible leaf meshes.
- The old CPU/decode parity layer was also removed from `Runtime/Rendering`, so historical sections below that mention temporary CPU fallback should be treated as superseded design history rather than the current runtime contract.

The system is designed to:
- scale to millions of whole-tree instances
- preserve as much visible detail as possible near the camera
- simplify aggressively everywhere else
- reconstruct exact surviving branch and shell parts from a persisted hierarchy
- extend URP directly instead of relying on Unity `LODGroup`
- render through indirect draws grouped by exact mesh/material slots

Hard constraints:
- no transparency, opaque-only rendering, transparency will break tile-based rendering on mobiles/handheld devices.
- no alpha clip
- no billboards or cards
- no `BatchRendererGroup`
- no Unity `LODGroup`
- no `MaterialPropertyBlock`
- latest generated meshes always persist, even when validation marks them invalid
- `Impostor` is one baked tree-space mesh only. Runtime never draws `impostorMesh + trunkL3Mesh` together.
- `impostorMesh` combines the source trunk plus all placed source branch `woodMesh` and `foliageMesh`, and bake may optionally simplify it further down to a front-side-only surface
- Wind is hierarchical and representation-dependent

The runtime backend is `Graphics.RenderMeshIndirect` driven from a URP renderer feature. This does not mean one literal draw call for the whole vegetation system. It means one vegetation submission path built from multiple indirect calls, each bound to one exact mesh/material slot.

---

# 1. System Architecture

## 1.1 High-Level Pipeline

Editor Authoring -> Editor Bake -> Runtime Registration/Flattening -> GPU-primary cell visibility -> GPU tree classification -> GPU branch and hierarchy survival evaluation -> accepted-content prioritization -> GPU-resident count/pack/emit -> optional URP indirect depth pass -> URP indirect color pass

Key  target principle: minimum draw-calls with minimum geometry.

## 1.2 MVP Runtime Decode Model

Milestone 1 now uses a GPU-resident decode path with one strict default:
- GPU is the owner of coarse visibility, LOD rules, branch-tier decisions, and final indirect-emission buffers.
- Runtime container does not expose a CPU fallback path anymore.
- URP renders the final per-draw-slot instance lists via `RenderMeshIndirect`.

The next architectural steps after MVP are:
- accept one guaranteed `TreeL3` whole-tree representation per visible near / mid tree before any branch-expanded work exists
- make `TreeInstances[]` the authoritative owner of tree-first acceptance, `TreeL3` floor identity, and nearest-first promotion decisions in the runtime path
- remove legacy branch-level runtime owners from the redesigned urgent path instead of slimming them:
  - pre-populated `SceneBranches[]`
  - `ShellNodesL1/L2/L3[]`
- keep reusable `TreeBlueprints[]`, `BlueprintBranchPlacements[]`, and compact branch prototype tier meshes with separate canopy/wood per level as shared static inputs for promoted trees only
- promote nearest trees through branch-expanded `L2/L1/L0` tiers first, with `L0` defined as survived original branches
- compact final submissions down to non-zero emitted slots after accepted content is chosen
- revisit branch-local hierarchy traversal later only if the no-BFS branch tiers prove insufficient

## 1.3 Core Rules

- All runtime geometry is opaque.
- `L0` is the full source branch representation and is only allowed very near the camera.
- Runtime shell tiers are `L1`, `L2`, and `L3`.
- Current authored shell arrays map as:
  - runtime `L1` -> authored `shellNodesL0`
  - runtime `L2` -> authored `shellNodesL1`
  - runtime `L3` -> authored `shellNodesL2`
- Far trees render one coarse opaque far mesh (`impostorMesh`) only. No separate trunk draw is allowed in runtime `Impostor` mode.
- `trunkL3Mesh` is used only by expanded trees whose surviving branch placements are all `L2` or `L3`.
- Runtime uses the latest baked meshes even if validation marks them invalid. Invalid state is diagnostic, not a rollback trigger.

---

## 1.4 Runtime Terminology

- `Visible instance`
  One accepted runtime instance record packed into the shared container-scoped visible-instance buffer. This is what `maxVisibleInstanceCapacity` limits.

- `Draw slot`
  One exact runtime registry identity defined by `Mesh + Material + MaterialKind`. A draw slot owns one slot index, one indirect-args record, one packed-range start in `slotPackedStarts`, and one potential indirect submission in a render pass.

- `Registered draw slot`
  One slot that exists in `VegetationRuntimeRegistry.DrawSlots`. Current shipped `VegetationIndirectRenderer` binds every registered slot and treats it as active for submission once a frame is bound.

- `Non-zero emitted slot`
  One slot whose emitted instance count is greater than zero in `_SlotEmittedInstanceCounts` for the prepared frame. This is the measured submission-worthy subset, not the current shipped submission set.

- `Indirect submission`
  One final `DrawMeshInstancedIndirect` call issued for one active draw slot in one pass. It is downstream from accepted visible instances and is not the same as the visible-instance budget. Current shipped limitation: active slots are all registered slots after bind, not only the non-zero emitted subset.

- `Tree branch span`
  The current runtime tree contract is a flat contiguous branch range through `SceneBranchStartIndex + SceneBranchCount`. It is not a tree-wide octree/BVH/BFS hierarchy.

- `Scene branch record`
  `SceneBranches[]` is one bounded static registration record per placed branch in the runtime snapshot. It is not a final submission owner.

- `Branch-shell BFS metadata`
  The runtime stores `firstChildIndex + childMask` for branch shell tiers, but the current shipped shader does not yet use those links for true frontier traversal or subtree skip. This is branch-shell BFS, not tree BFS.

---

## 1.5 Ownership Split

This section is the ownership authority for editor authoring, editor bake, and runtime consume code.

### Editor Authoring

Editor authoring is the user-edited source of truth.

Allowed responsibilities:
- assign source meshes, materials, placements, authored bounds, authored distance bands, budgets, and bake settings
- run validation and preview
- persist generated bake outputs back onto the authoring assets

Forbidden responsibilities:
- runtime frame logic
- per-frame visibility or decode state
- indirect args or visible-instance list ownership

### Editor Bake

Editor bake is the only stage allowed to generate or replace derived geometry.

Allowed responsibilities:
- voxelize source foliage and branch geometry
- build canonical `shellNodesL0` and compact `shellNodesL1` / `shellNodesL2`
- build `shellL1WoodMesh`, `shellL2WoodMesh`, `trunkL3Mesh`, and `impostorMesh`
- persist generated meshes into writable project `Assets/` folders
- filled bounding volume of per-mesh node AABB per level of detail for the runtime phase.
- mark authoring assets invalid when generated outputs violate budgets or bounds (mesh still exists, additional warning/errors are available in the editor window panel)

Forbidden responsibilities:
- runtime scene registration
- per-frame culling, classification, decode, or draw submission

### Runtime Consume Path

Runtime is allowed to read the runtime-facing subset of authored ScriptableObject data directly. Optional caching or flattening is allowed for performance, but it is an implementation detail, not a separate source of truth.

Allowed responsibilities:
- read authored runtime data such as placements, meshes, materials, hierarchy topology, bounds, and LOD thresholds directly from `TreeBlueprintSO`, `BranchPrototypeSO`, `LODProfileSO`, `BranchPlacement`, and `BranchShellNode`
- derive runtime coarse bounds such as tree spheres and per-placement branch spheres in actual world space
- build optional runtime caches, draw-slot registries, and GPU buffers from that data
- update coarse cell visibility
- classify trees and branch tiers
- evaluate shell-node survival decisions
- decode visible frontiers
- build visible-instance lists, indirect args, and conservative per-slot `worldBounds`
- submit indirect draws

Forbidden responsibilities:
- calling editor bake
- reading bake settings, triangle budgets, generated-folder overrides, validation UI state, or preview state
- mutating authoring assets or generated meshes
- any `AssetDatabase` usage
- any editor bake, preview, validation, or generated-mesh persistence work

### Static Registry Owners vs Per-Frame Worklists

Static registry owners:
- `SpatialGrid`
  Tree-to-cell registration and visible-cell query boundaries only.
- `TreeInstances[]`
  Current shipped state: per-tree static scene registration plus the `SceneBranchStartIndex + SceneBranchCount` handle into the flat branch array.
  Urgent redesign target: authoritative per-tree owner of direct `L0/L1/L2/L3/Impostor` runtime handles, bounds, and slot identity.
- `SceneBranches[]`
  One bounded static scene-branch registration record per placed branch in the container snapshot. Count grows linearly with active tree authorings and blueprint branch placements during registration rebuild.
- `BranchPrototypes[] + ShellNodesL1/L2/L3[]`
  Reusable branch module decode data and reusable branch-shell BFS metadata.
- `DrawSlots[]`
  Final submission identities only.

Per-frame worklists:
- cell visibility masks
- tree mode decisions
- branch decisions
- packed visible instances
- indirect args
- future tree-visibility records, accepted-tree-tier counts, and active-slot compact worklists

Hard rule:
- static registry owners may grow only during explicit registration rebuild
- per-frame worklists must stay bounded by the active registry or by explicit runtime budgets
- `SceneBranches[]`, `BranchPrototypes[]`, and `ShellNodesL1/L2/L3[]` are current shipped owners only. The approved urgent redesign removes pre-populated `SceneBranches[]` and shell-node BFS ownership from the new runtime path instead of trying to preserve a slimmer branch-level work surface.
- `TreeInstances[]` becomes the authoritative urgent-path owner once tree-first `TreeL3` floor acceptance and nearest-first promotion land

---

## 1.6 ScriptableObject Usage Summary

This summary is intentionally short. The fields listed under "runtime-facing" are allowed runtime inputs as-is. Runtime may read them directly from the ScriptableObjects, or cache/flatten them for performance.

### Runtime-facing data

- `BranchPrototypeSO`: `woodMesh`, `woodMaterial`, `foliageMesh`, `foliageMaterial`, `leafColorTint`, `shellNodesL0`, `shellNodesL1`, `shellNodesL2`, `shellL1WoodMesh`, `shellL2WoodMesh`, `shellMaterial`, `localBounds`
- `TreeBlueprintSO`: `trunkMesh`, `trunkL3Mesh`, `trunkMaterial`, `branches`, `impostorMesh`, `impostorMaterial`, `lodProfile`, `treeBounds`
- `LODProfileSO`: `l0Distance`, `l1Distance`, `l2Distance`, `impostorDistance`, `absoluteCullDistance`
- `BranchPlacement`: `prototype`, `localPosition`, `localRotation`, `scale`
- `BranchShellNode`: `localBounds`, `firstChildIndex`, `childMask`, `shellL0Mesh`, `shellL1Mesh`, `shellL2Mesh`
- `BranchShellNode.depth`: optional validation/debug field; runtime may ignore it unless a debug path needs it

### Editor-only data

- `BranchPrototypeSO`: `generatedCanopyShellsRelativeFolder`, `triangleBudgetWood`, `triangleBudgetFoliage`, `triangleBudgetShellL0`, `triangleBudgetShellL1`, `triangleBudgetShellL2`, `shellBakeSettings`
- `TreeBlueprintSO`: `generatedImpostorMeshesRelativeFolder`, `ImpostorBakeSettings`
- editor preview state, validation state, and any `AssetDatabase` persistence helpers

---

# 2. Representation Model

## 2.1 Tree-Level Runtime Modes

| Mode | Meaning |
|------|---------|
| Culled | Tree rejected before draw emission |
| Expanded | Tree expands into trunk plus branch-driven canopy parts |
| Impostor | Tree emits one coarse opaque far mesh |

Only surviving near and mid trees enter `Expanded`. Far trees stay as one far mesh.

## 2.2 Expanded-Tree Part Tiers

| Runtime Tier | Canopy Representation | Branch Wood Representation | Trunk Representation |
|--------------|------------------------|----------------------------|----------------------|
| `L0` | Source branch `foliageMesh` at branch-placement granularity | Source branch `woodMesh` | Full `trunkMesh` |
| `L1` | Surviving frontier from authored `shellNodesL0` | Source branch `woodMesh` where needed beside the shell frontier | Full `trunkMesh` |
| `L2` | Surviving frontier from authored `shellNodesL1` | `shellL1WoodMesh` | Simplified `trunkL3Mesh` |
| `L3` | Surviving frontier from authored `shellNodesL2` | `shellL2WoodMesh` | Simplified `trunkL3Mesh` |

Notes:
- `L0` is branch-placement granularity because the source asset contract stores one reusable source branch, not source geometry split by shell node. Front facing `L0 visibility` will use source mesh branch for runtime, but we still treat `L0` distance for backside simplification with `L1` shell.
- `L1/L2/L3` use hierarchy-frontier granularity, so only surviving node meshes are emitted.
- `Impostor` is tree-level only. It does not expand into branch parts.
- Expanded-tree trunk draw is selected once per tree after branch classification:
- if any surviving branch placement is `L0` or `L1`, draw full `trunkMesh`
- otherwise draw `trunkL3Mesh`
- persisted shell-node `localBounds` are authored octant ownership bounds; emitted shell meshes stay inside those bounds

## 2.3 `LODProfileSO` Authority

`LODProfileSO` owns the authored distance bands per species.

Expected authored thresholds:
- `l0Distance`
- `l1Distance`
- `l2Distance`
- `impostorDistance`
- `absoluteCullDistance`

Rules:
- the active Milestone 1 authored thresholds must satisfy `0 < l0Distance < l1Distance < l2Distance < impostorDistance < absoluteCullDistance`
- classification distance is sphere-surface distance:
  - `distance = max(0, length(cameraWorldPosition - sphereCenterWorld) - sphereRadiusWorld)`
- tree-mode classification uses the tree sphere derived from `treeBounds`:
  - `distance >= absoluteCullDistance` -> `Culled`
  - `impostorDistance <= distance < absoluteCullDistance` -> `Impostor`
  - `distance < impostorDistance` -> `Expanded`
- branch-tier classification uses one per-placement sphere derived from transformed prototype `localBounds` and applies only to `Expanded` trees:
  - `0 <= distance < l0Distance` -> `L0`
  - `l0Distance <= distance < l1Distance` -> `L1`
  - `l1Distance <= distance < l2Distance` -> `L2`
  - `l2Distance <= distance` -> `L3`
- editor preview and bake keep using authoritative branch and node bounds, not runtime spheres
- runtime shell-node survival uses persisted node bounds from the selected shell hierarchy

## 2.4 Tree Composition and Branch Reconstruction

Authoritative form:

```text
Tree
  = trunk
  + Sum(BranchPlacement -> BranchPrototype -> source branch OR shell frontier)
```

Branch reconstruction rules:
- `BranchPrototypeSO.shellNodesL0` is the canonical ownership and split hierarchy.
- `shellNodesL1` and `shellNodesL2` are separately persisted compact hierarchies derived from owned `L0` occupancy.
- Runtime `L1` uses `shellNodesL0`, runtime `L2` uses `shellNodesL1`, and runtime `L3` uses `shellNodesL2`.
- Compact baked tiers may intentionally collapse to one root node when the compact shell mesh is cheaper than keeping the child frontier. Low node counts in `shellNodesL1` or `shellNodesL2` are therefore not automatically missing bake steps.
- Branch reconstruction always starts at the hierarchy root and traverses downward.
- Parent bounds are tested before descendants.
- If a branch placement enters runtime `L0`, emit the source branch meshes and skip shell frontier emission for that branch in that frame.
- At `L0/L1/L2/L3` always try to simplify backside when obscured by higher level.
- If a branch placement enters runtime `L1/L2/L3`, choose exactly one hierarchy tier for that branch in that frame and emit the surviving frontier from that tier.

## 2.5 MVP Branch-Shell BFS Hierarchy Contract

The persisted shell arrays are currently treated as branch-shell BFS-flattened hierarchies for MVP decode.

Required invariants:
- root node is always index `0`
- immediate children of a node occupy one contiguous block starting at `firstChildIndex`
- `childMask` stores the octant occupancy bits for that immediate child block
- children are ordered in ascending octant-bit order inside that block
- child depth is always parent depth + 1
- child bounds always stay inside parent bounds
- node `localBounds` always contain the emitted mesh bounds for that node

This metadata is enough to preserve hierarchy ownership and leaf identity.

Current shipped limitation:
- the compute shader does not yet use `FirstChildIndex + ChildMask` for real frontier traversal
- it linearly scans the selected shell tier and emits nodes that are both visible and already leaves

It is not enough for efficient subtree skipping at scale until the shader actually traverses it. After that lands, switch to DFS preorder plus subtree spans.

## 2.6 Rendering Stack

- Unity 6 URP Forward+
- compute shaders for tree classification, branch decisions, and shell-tier leaf visibility tests in the current shipped path
- GPU-resident count/pack/emit of visible instances into one shared bounded buffer
- `Graphics.RenderMeshIndirect`
- per-draw-slot indirect args and visible instance buffers
- URP renderer feature for depth and color pass integration

---

# 3. Canopy Shell System

## 3.1 Definition

Branch-local opaque volumetric canopy meshes persisted as tier-specific hierarchies.

## 3.2 Adaptive Hierarchical Shell Architecture

The authored shell model is:

```text
BranchPrototypeSO
  -> BranchShellNode[] shellNodesL0
  -> BranchShellNode[] shellNodesL1
  -> BranchShellNode[] shellNodesL2
```

Each `BranchShellNode` stores:
- local bounds
- hierarchy topology (`firstChildIndex`, `childMask`)
- three tier-specific mesh slots in the serialized class, with one strict authored-array rule:
  - in `shellNodesL0`, only `shellL0Mesh` may be non-null
  - in `shellNodesL1`, only `shellL1Mesh` may be non-null
  - in `shellNodesL2`, only `shellL2Mesh` may be non-null

Authored mapping to runtime:
- authored `shellNodesL0` -> runtime `L1`
- authored `shellNodesL1` -> runtime `L2`
- authored `shellNodesL2` -> runtime `L3`

## 3.3 Per-BranchPrototype Bake Pipeline

Per branch prototype:
1. Read the source foliage mesh.
2. Build one canonical `L0` voxel occupancy field.
3. Split into canonical authored `L0` ownership nodes using owned surface occupancy.
4. Emit bounded authored `L0` meshes for canonical nodes.
5. Re-voxelize owned authored `L0` occupancy into coarse node-local volumes for authored `L1` and authored `L2`.
6. Persist `shellNodesL0`, `shellNodesL1`, and `shellNodesL2`.
7. Bake `shellL1WoodMesh` and `shellL2WoodMesh`.

## 3.4 Per-TreeBlueprint Bake Pipeline

Per tree blueprint:
1. Read the source `trunkMesh` plus all placed source branch `woodMesh` and `foliageMesh` in tree space.
2. Bake bounded `trunkL3Mesh` from the source `trunkMesh`, clipping every voxel candidate, lower-resolution retry, and fallback mesh back to the original `trunkMesh.bounds`.
3. Bake one bounded `impostorMesh` from the combined tree-space source geometry.
4. Optionally simplify `impostorMesh` further down to a front-side-only surface when that still preserves the intended far silhouette.
5. Persist `trunkL3Mesh` and `impostorMesh`.

## 3.5 Runtime Use of the Shell Hierarchy

At runtime:
- tree classification decides whether the tree stays `Impostor` or expands
- expanded trees enqueue branch work items
- each branch work item chooses runtime `L0`, `L1`, `L2`, or `L3`
- shell-mode branches evaluate the chosen hierarchy
- GPU counts required instances per draw slot
- GPU packs all visible tree, branch, and shell outputs into one bounded shared visible-instance buffer
- overflow is clamped against the shared visible-instance capacity instead of growing scene-scale buffers

Current Phase D decision rule:
- nodes outside the frustum are `Reject`
- non-leaf nodes are currently skipped
- only visible leaves are emitted
- finer intra-tier screen-size collapse is deferred until after the current MVP foundation

This is the core reason the hierarchy exists: exact visible parts can be emitted independently, instead of forcing one mesh choice for the whole branch.

## 3.6 Invalid Output Policy

Generated meshes are not discarded when they miss budgets.

Required behavior:
- persist the latest generated shell, branch-wood, `trunkL3Mesh`, and far-mesh outputs
- mark the owning authoring asset invalid through validation
- keep the latest generated meshes wired for preview and runtime
- do not silently restore the previous valid bake

Invalid state means "current bake needs follow-up", not "runtime must revert".

---

# 4. Authoring Workflow

## 4.1 Tree Assembly / Branch Creation

Trees are authored as:

```text
Tree = trunk + reusable branch placements + generated shell hierarchies + generated far mesh
```

Authoring constraints:
- opaque-only source materials
- readable source meshes
- reusable branch modules
- bounded generated output

## 4.2 Baking Outputs

Required baked outputs:
- `shellNodesL0`
- `shellNodesL1`
- `shellNodesL2`
- `shellL1WoodMesh`
- `shellL2WoodMesh`
- `trunkL3Mesh`
- far mesh (`impostorMesh`)

These outputs are editor-baked products.

Rules:
- runtime never regenerates them
- runtime may consume them directly from the authored assets
- runtime may cache or flatten them for performance, but that does not change the ownership contract

## 4.3 Simplified Trunk for `L3`

Runtime `L3` requires a separate simplified trunk mesh.

Rules:
- generate `trunkL3Mesh` from `trunkMesh`
- use the same bounded reduction policy used elsewhere
- clip every candidate back to the original `trunkMesh.bounds`, including lower-resolution retries and fallback meshes
- keep it inside authoritative trunk bounds
- triangle count must be strictly lower than the source `trunkMesh`

## 4.4 Far Mesh Baking

The far mesh is a coarse opaque tree-space mesh built from:
- trunk mesh
- all placed branch wood meshes
- all placed branch foliage meshes

Rules:
- no cards
- no billboards
- no alpha-based impostor
- runtime `Impostor` mode draws `impostorMesh` only
- source inputs are the source `trunkMesh` plus all placed source branch `woodMesh` and `foliageMesh`
- bake may additionally reduce the final mesh to a front-side-only surface

---

# 5. Runtime System

## 5.0 Runtime Entry Contract

Runtime may consume the runtime-facing data listed in `1.5` directly from the authored ScriptableObjects.

Runtime may also build optional cached data from those fields during scene registration, for example:
- draw-slot registries
- tree coarse bounds
- per-placement branch bounds center plus bounding sphere radius
- flattened branch-shell BFS shell-node payloads per authored shell tier

Runtime per-frame may use authored asset references directly and/or these cached runtime structures, depending on which path is simpler and faster.

Runtime must never read the editor-only fields listed in `1.5`, and it must never invoke editor bake, preview, validation, or generated-mesh persistence logic.

## 5.1 Runtime Work Units

The runtime must not treat "one tree thread does everything" as the final model.

Required staged work:
1. GPU-primary tree-level coarse visibility
2. tree-level mode selection (`Culled`, `Expanded`, `Impostor`)
3. tree-level accepted tier selection (`TreeL3/L2/L1/L0` for near and mid trees, `Impostor` for far trees) after tree prioritization
4. per-slot instance counting
5. per-slot packed-start build for the shared visible-instance buffer
6. direct GPU emission into the bounded shared visible-instance buffer
7. final submission compaction to non-zero emitted slots
8. per-draw-slot indirect submission

## 5.2 Exact Runtime Branch and Emission Payloads

MVP emission requires one explicit branch-decision contract plus one explicit shared-buffer packing contract.

### `BranchShellNodeRuntimeBfs`

```text
- float3 localCenter
- float3 localExtents
- uint firstChildIndex
- uint childMask
- uint shellDrawSlot
```

Meaning:
- `firstChildIndex` points to the start of the contiguous immediate-child block
- `childMask` defines which octants exist in that block
- `shellDrawSlot` is the exact mesh/material slot for that node mesh in the active authored hierarchy

### `BranchDecisionGPU`

```text
- uint treeIndex
- uint branchPlacementIndex
- uint runtimeTier        // L0, L1, L2, L3
```

### Shared per-frame packing state

```text
- uint slotRequestedInstanceCounts[drawSlotCount]
- uint slotEmittedInstanceCounts[drawSlotCount]
- uint slotPackedStarts[drawSlotCount]
- VegetationIndirectInstanceData visibleInstances[maxVisibleInstanceCapacity]
- IndirectArgs indirectArgs[drawSlotCount]
```

Meaning:
- shell-node arrays stay prototype-local and are never duplicated into scene-wide per-node decision buffers
- each draw slot is one exact `Mesh + Material + MaterialKind` registry identity, not one tree or one species
- tree, wood, foliage, and shell outputs all land in one shared visible-instance buffer
- `slotPackedStarts` defines each draw slot's packed range inside that shared buffer
- `VegetationRuntimeContainer.maxVisibleInstanceCapacity` is the hard scene budget for visible packed instances
- current renderer can issue one indirect submission per draw slot in a pass

## 5.3 What Branch-Shell BFS Metadata Provides Today

Current shipped reality:
- branch-shell BFS metadata is persisted and uploaded prototype-local
- the shader uses node bounds, leaf/non-leaf state, and draw-slot identity
- the shader does not yet walk child links to build a visible frontier

Current shipped strengths:
- preserves authored branch-shell ownership and per-node leaf meshes
- keeps the door open for real hierarchy traversal later without reauthoring data

Current shipped weaknesses:
- no subtree skipping
- no queue-based frontier decode
- flat scans across the selected shell tier for promoted branches
- flat tree branch spans still dominate expanded-tree work

After the urgent prioritization/compaction work lands, the next hierarchy step is real traversal, then DFS preorder with subtree spans.

---

# 6. GPU Direct Emission Pipeline

## 6.1 Frame Stages

Per frame:
1. GPU updates coarse cell visibility
2. GPU classifies trees into `Culled`, `Expanded`, or `Impostor`
3. GPU resolves accepted trees by priority before any slot packing
4. GPU chooses exactly one accepted direct tree tier per visible tree
5. GPU resets per-slot requested and emitted counters
6. GPU counts accepted tree-level emissions only
7. GPU builds `slotPackedStarts` for the bounded shared visible-instance buffer
8. GPU emits accepted tree representations into the packed visible-instance buffer
9. GPU finalizes indirect args with instance counts clamped to remaining shared capacity
10. renderer compacts final submissions to non-zero emitted slots
11. URP renders indirect depth and color passes

## 6.2 Example Tree Classification Pseudocode

```hlsl
[numthreads(64, 1, 1)]
void ClassifyTrees(uint id : SV_DispatchThreadID)
{
    TreeInstanceGPU tree = _TreeInstances[id];
    LODProfileGPU lod = _LODProfiles[tree.lodProfileIndex];

    if (!CellVisible(tree.cellIndex))
        return;

    float treeDistance = DistanceToTreeSphere(tree, _CameraPosition);
    if (treeDistance >= lod.absoluteCullDistance)
        return;

    if (treeDistance >= lod.impostorDistance)
    {
        EmitImpostorTree(tree);
        return;
    }

    EmitExpandedTree(tree);
}
```

## 6.3 Example Branch Tier Selection Pseudocode

```hlsl
[numthreads(64, 1, 1)]
void ClassifyBranches(uint id : SV_DispatchThreadID)
{
    ExpandedTreeGPU expanded = _ExpandedTrees[id];
    TreeBlueprintGPU blueprint = _TreeBlueprints[expanded.blueprintIndex];
    LODProfileGPU lod = _LODProfiles[blueprint.lodProfileIndex];

    for each branch placement in blueprint
    {
        float branchDistance = DistanceToBranchSphere(expanded, branchPlacement, _CameraPosition);

        if (branchDistance < lod.l0Distance)
        {
            EmitBranchDecision(expanded, branchPlacement, TierL0);
            continue;
        }

        if (branchDistance < lod.l1Distance)
        {
            EmitBranchDecision(expanded, branchPlacement, TierL1);
            continue;
        }

        if (branchDistance < lod.l2Distance)
        {
            EmitBranchDecision(expanded, branchPlacement, TierL2);
            continue;
        }

        EmitBranchDecision(expanded, branchPlacement, TierL3);
    }
}
```

## 6.4 Current Shipped Shell-Tier Count/Emit Pseudocode

```hlsl
[numthreads(64, 1, 1)]
void EmitBranchTierInstances(uint id : SV_DispatchThreadID)
{
    BranchDecisionGPU branch = _BranchDecisions[id];
    if (branch.runtimeTier == TierL0)
    {
        EmitSourceBranch(branch);
        return;
    }

    for each node in selected shell tier
    {
        if (node has children)
        {
            continue;
        }

        if (!NodeVisible(node))
        {
            continue;
        }

        EmitShellLeaf(branch, node);
    }
}
```

Current Phase D implementation keeps this rule deterministic but limited:
- non-leaf nodes are skipped, not traversed
- only visible leaves are emitted
- `FirstChildIndex + ChildMask` metadata is not yet used to build a visible frontier

## 6.5 Current Direct GPU Leaf Emission

Current direct GPU leaf emission is the authoritative shipped reconstruction step.

For each `BranchDecisionGPU`:
- if runtime tier is `L0`, emit source branch `woodMesh + foliageMesh`
- if runtime tier is `L1`, decode `shellNodesL0`
- if runtime tier is `L2`, decode `shellNodesL1`
- if runtime tier is `L3`, decode `shellNodesL2`

Per-frame packing flow:
- count how many instances each draw slot wants to emit
- build one packed start offset per draw slot inside the shared visible-instance buffer
- emit all tree, branch, and shell instances directly into that shared buffer
- clamp final indirect instance counts when a draw slot would extend past the remaining shared capacity

After direct GPU emission:
- select one trunk draw for each expanded tree:
  - if any surviving branch decision is `L0` or `L1`, emit `trunkMesh`
  - otherwise emit `trunkL3Mesh`
- bind the shared visible-instance buffer plus `slotPackedStarts`
- submit one indirect call per exact mesh/material draw slot
- render one indirect call per draw slot

---

# 7. GPU Buffer Layout

## 7.1 Static Buffers

These buffers are optional runtime-side caches built from the runtime-facing authored data. They are not rebuilt by editor bake code during play.

- `TreeInstances`
- `TreeBlueprints`
- `BranchPlacements`
- `BranchPrototypes`
- `BranchShellNodesL0`
- `BranchShellNodesL1`
- `BranchShellNodesL2`

## 7.2 Per-Frame Staging Buffers

- `ExpandedTrees`
- `BranchDecisions`
- `SlotRequestedInstanceCounts`
- `SlotEmittedInstanceCounts`
- `SlotPackedStarts`
- bounded shared visible instance buffer
- indirect argument buffers per draw slot

## 7.3 Flow

Tree -> Expanded or Impostor -> flat branch span -> BranchDecision -> shell-tier leaf scan from prototype-local node data -> Packed shared buffer -> draw-slot indirect call

---

# 8. Rendering

## 8.1 Draw Groups

| Group | Mesh Source | Shader |
|------|-------------|--------|
| TrunkFull | `TreeBlueprintSO.trunkMesh` | `VegetationTrunkLit` |
| TrunkL3 | `TreeBlueprintSO.trunkL3Mesh` | `VegetationTrunkLit` |
| BranchWoodL0 | `BranchPrototypeSO.woodMesh` | `VegetationTrunkLit` |
| BranchFoliageL0 | `BranchPrototypeSO.foliageMesh` | `VegetationCanopyLit` |
| BranchWoodL1 | `BranchPrototypeSO.woodMesh` | `VegetationTrunkLit` |
| BranchWoodL2 | `BranchPrototypeSO.shellL1WoodMesh` | `VegetationTrunkLit` |
| BranchWoodL3 | `BranchPrototypeSO.shellL2WoodMesh` | `VegetationTrunkLit` |
| ShellL1 | `BranchPrototypeSO.shellNodesL0[*]` | `VegetationCanopyLit` |
| ShellL2 | `BranchPrototypeSO.shellNodesL1[*]` | `VegetationCanopyLit` |
| ShellL3 | `BranchPrototypeSO.shellNodesL2[*]` | `VegetationCanopyLit` |
| Impostor | `TreeBlueprintSO.impostorMesh` | `VegetationFarMeshLit` |

## 8.2 Shaders

`VegetationCanopyLit.shader`
- opaque only
- no normal map
- no emission
- RSUV-driven canopy tint

`VegetationTrunkLit.shader`
- opaque only
- albedo texture allowed
- no normal map
- no emission

`VegetationFarMeshLit.shader`
- opaque only
- no billboard rotation
- stable simple shading

## 8.3 URP Integration

`VegetationRendererFeature` owns:
- the shared `VegetationClassify.compute` asset reference through feature settings
- indirect depth pass
- indirect color pass
- `VegetationFoliageFeatureSettings.EnableDiagnostics` as the current shipped runtime-review telemetry gate for registration, preparation, indirect submission, exact branch/shell/visible-instance byte logs, and one-shot emitted-slot readback
- optional later optimization hook points for depth-aware culling or other submission reductions after prioritization is stable

`VegetationRuntimeContainer` still owns runtime registration and GPU-frame preparation; urgent dense-forest work should keep that path focused on accepted-content prioritization first. It is not a BRG wrapper.

---

# 9. Developer Verification

## 9.0 How To Enable Current Runtime-Review Telemetry

1. Open the URP renderer data asset that contains `VegetationRendererFeature`.
2. Enable `VegetationFoliageFeatureSettings.EnableDiagnostics`.
3. Render vegetation through a Game or SceneView camera.
4. Read Unity Console entries from `AuthoringContainerRuntime`.

Current shipped telemetry includes:
- `TreeBlueprints[]` count
- `SceneBranches[]` count
- `BranchPrototypes[]` and `ShellNodesL1/L2/L3[]` counts
- exact allocated GPU bytes for `branchBuffer`, `branchDecisionBuffer`, prototype buffer, shell-node buffers, and the visible-instance capacity buffer
- `totalBranchTelemetryBufferBytes`
- one-shot prepared-frame readback for `nonZeroEmittedSlots`, `emittedVisibleInstances`, and `emittedVisibleInstanceBytes`

Scope note:
- this is current runtime-review telemetry only
- `nonZeroEmittedSlots` and emitted visible-instance totals use one synchronous CPU readback of `_SlotEmittedInstanceCounts`, so leave diagnostics disabled outside review
- it does not replace the later redesign telemetry for promoted trees, acceptance buckets, or overflow outcomes

Milestone 1 should not add a large new runtime test plan yet. It must add strong developer-side verification for the direct GPU emission path.

Required developer verification:
- dump one flattened hierarchy per tier and confirm:
  - root at index `0`
  - contiguous child block starts at `firstChildIndex`
  - child block order matches ascending octant bits in `childMask`
  - child depth is parent depth + 1
  - child bounds stay inside parent bounds
  - mesh bounds stay inside node `localBounds`
- log one frame of `BranchDecisionGPU` output and verify selected runtime tiers match authored distances
- capture one frame of packed per-slot counts and verify shell-tier leaves are emitted only from visible leaf nodes
- compare packed per-draw-slot emitted instance counts against uploaded indirect args
- verify visible packed instance count clamps cleanly when the shared capacity is exceeded
- verify prioritization preserves whole-tree presence before optional detail under heavy overflow
- verify `L0` only appears inside the authored near band
- verify `Impostor` only appears once tree distance reaches the impostor band
- verify invalid generated meshes still render, while validation marks the asset invalid in tooling
- if Unity imports `VegetationClassify.compute` without exposing the expected kernels, fail explicitly and skip GPU parity verification instead of silently faking GPU success

---

# 10. Deferred Optimization

Deferred items:
- DFS preorder plus subtree spans
- fully GPU-driven frontier decode
- full HiZ depth pyramid occlusion for tree and branch work as a later optimization only
- dithered transitions
- hierarchical wind refinement
- scale quantization
- streaming and dynamic loading

---

# 11. Conclusion

This system targets large-scale vegetation in Unity by combining:
- reusable authored branches
- bounded baked shell hierarchies
- GPU visibility and branch-tier decisions
- GPU-resident direct leaf emission into one bounded shared visible-instance buffer
- exact per-draw-slot indirect submission

The runtime authority is now:
- runtime `L0/L1/L2/L3 + Impostor`
- authored shell arrays shifted into runtime `L1/L2/L3`
- branch-shell BFS hierarchy flattening for MVP
- prototype-local shell caches plus per-frame GPU count/pack/emit
- explicit `maxVisibleInstanceCapacity` as the hard runtime visible-instance budget
- multiple indirect calls grouped by exact mesh/material slots, not one literal draw call

---

END
