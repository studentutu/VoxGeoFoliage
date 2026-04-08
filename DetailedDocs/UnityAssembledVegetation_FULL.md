# Unity Assembled Vegetation System - Full End-to-End Plan

## 0. Purpose

This document defines the production target inspired by Unreal Engine 5.7 foliage innovations (Assemblies, voxelized LOD, and hierarchical wind animation) for a high-performance vegetation system in Unity 6 URP.

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

Editor Authoring -> Editor Bake -> Runtime Registration/Flattening -> GPU-primary cell visibility -> GPU tree classification -> GPU branch and hierarchy survival evaluation -> GPU-primary survivor decode -> CPU upload of indirect args and visible instance data -> optional URP indirect depth pass -> URP indirect color pass

Key  target principle: minimum draw-calls with minimum geometry.

## 1.2 MVP Runtime Decode Model

Milestone 1 uses a temporary hybrid decode path with one strict default:
- GPU is the primary owner of coarse visibility, LOD rules, hierarchy survival decisions, and frontier decode.
- CPU fallback exists only as a temporary MVP backup path.
- CPU fallback may only consume compact decision buffers through completed non-blocking async readback results.
- CPU fallback decodes the persisted BFS hierarchy into exact visible mesh-part draws only when the GPU decode path is unavailable or explicitly disabled for validation/debugging.
- URP renders the final per-draw-slot instance lists via `RenderMeshIndirect`.

This is an MVP compromise. After MVP, the intended direction is to move from BFS plus simple hybrid decode to DFS preorder plus subtree spans and push the final decode fully onto the GPU.

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

## 1.4 Ownership Split

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
- build visible-instance lists, indirect args, and per-slot `worldBounds`
- submit indirect draws

Forbidden responsibilities:
- calling editor bake
- reading bake settings, triangle budgets, generated-folder overrides, validation UI state, or preview state
- mutating authoring assets or generated meshes
- any `AssetDatabase` usage
- any editor bake, preview, validation, or generated-mesh persistence work

---

## 1.5 ScriptableObject Usage Summary

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
- Branch reconstruction always starts at the hierarchy root and traverses downward.
- Parent bounds are tested before descendants.
- If a branch placement enters runtime `L0`, emit the source branch meshes and skip shell frontier emission for that branch in that frame.
- At `L0/L1/L2/L3` always try to simplify backside when obscured by higher level.
- If a branch placement enters runtime `L1/L2/L3`, choose exactly one hierarchy tier for that branch in that frame and emit the surviving frontier from that tier.

## 2.5 MVP BFS Hierarchy Contract

The persisted shell arrays are currently treated as BFS-flattened hierarchies for MVP decode.

Required invariants:
- root node is always index `0`
- immediate children of a node occupy one contiguous block starting at `firstChildIndex`
- `childMask` stores the octant occupancy bits for that immediate child block
- children are ordered in ascending octant-bit order inside that block
- child depth is always parent depth + 1
- child bounds always stay inside parent bounds
- node `localBounds` always contain the emitted mesh bounds for that node

This is enough for MVP survivor decode, including the GPU-primary path and the temporary CPU fallback.

It is not enough for efficient subtree skipping at scale. After MVP, switch to DFS preorder plus subtree spans.

## 2.6 Rendering Stack

- Unity 6 URP Forward+
- compute shaders for tree classification, branch decisions, and hierarchy survival decisions
- GPU-primary survivor decode for MVP with a temporary non-blocking CPU fallback path
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
- GPU writes compact survival decisions
- survivor decode reconstructs the visible frontier from those decisions on GPU by default and otherwise only through the temporary non-blocking CPU fallback path, then emits exact visible frontier meshes by draw slot

Current Phase D decision rule:
- nodes outside the frustum are `Reject`
- visible internal nodes are `ExpandChildren`
- only visible leaves are `EmitSelf`
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
- flattened BFS shell-node payloads per authored shell tier

Runtime per-frame may use authored asset references directly and/or these cached runtime structures, depending on which path is simpler and faster.

Runtime must never read the editor-only fields listed in `1.5`, and it must never invoke editor bake, preview, validation, or generated-mesh persistence logic.

## 5.1 Runtime Work Units

The runtime must not treat "one tree thread does everything" as the final model.

Required staged work:
1. GPU-primary tree-level coarse visibility
2. tree-level mode selection (`Culled`, `Expanded`, `Impostor`)
3. branch-level tier selection (`L0/L1/L2/L3`)
4. hierarchy survival decisions for shell tiers
5. GPU-primary survivor decode
6. CPU fallback bridge only when the GPU decode path is unavailable or explicitly disabled
7. per-draw-slot indirect submission

## 5.2 Exact Runtime Node and Survival Payloads

MVP decode requires two explicit decision contracts.

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

### `NodeDecisionGPU`

```text
- uint treeIndex
- uint branchPlacementIndex
- uint runtimeTier        // L1, L2, L3 only
- uint nodeIndex
- uint decision           // Reject, EmitSelf, ExpandChildren
```

GPU or CPU fallback uses `BranchDecisionGPU` and `NodeDecisionGPU` to reconstruct the final visible frontier.

## 5.3 Why BFS Is Acceptable For MVP

BFS is acceptable for MVP because the decode path is still intentionally simple, whether the frontier is reconstructed by a temporary GPU queue or by the non-blocking CPU fallback after GPU rules are applied.

MVP BFS strengths:
- easy immediate-child lookup from `firstChildIndex + childMask`
- simple queue-based decode on GPU or CPU
- no need for subtree spans yet

MVP BFS weaknesses:
- no cheap subtree skipping
- weaker cache locality for deep traversal
- not a good final shape for fully GPU-driven traversal

After MVP, migrate to DFS preorder with subtree spans.

---

# 6. GPU and CPU Decode Pipeline

## 6.1 Frame Stages

Per frame:
1. GPU updates coarse cell visibility
2. GPU clears counters and staging buffers
3. GPU classifies trees into `Culled`, `Expanded`, or `Impostor`
4. GPU emits one far-mesh instance record for `Impostor` trees
5. GPU emits one `BranchDecisionGPU` record for each surviving expanded branch placement
6. GPU emits `NodeDecisionGPU` records for shell-tier branches
7. GPU decodes the surviving frontier directly
8. CPU fallback consumes the latest completed non-blocking readback of compact decision buffers only when the GPU decode path is unavailable or explicitly disabled
9. CPU uploads per-draw-slot instance data and indirect args from GPU-decoded lists or from the CPU fallback decode result
10. URP renders indirect depth and color passes

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

## 6.4 Example Shell Hierarchy Survival Pseudocode

```hlsl
[numthreads(64, 1, 1)]
void EvaluateHierarchyNodes(uint id : SV_DispatchThreadID)
{
    BranchDecisionGPU branch = _BranchDecisions[id];
    if (branch.runtimeTier == TierL0)
        return;

    uint nodeIndex = 0; // root of selected BFS hierarchy
    Queue<uint> frontier;
    frontier.Push(nodeIndex);

    while (frontier.NotEmpty())
    {
        uint current = frontier.Pop();
        NodeDecision decision = EvaluateNodeDecision(branch, current);
        EmitNodeDecision(branch, current, decision);

        if (decision == ExpandChildren)
        {
            PushImmediateChildren(frontier, current);
        }
    }
}
```

Current Phase D implementation keeps this rule deterministic:
- `Reject` when the node AABB is outside the frustum
- `ExpandChildren` for visible nodes that still have children
- `EmitSelf` only for visible leaves

## 6.5 Hybrid Survivor Decode

Hybrid decode is the authoritative MVP reconstruction step.

For each `BranchDecisionGPU`:
- if runtime tier is `L0`, emit source branch `woodMesh + foliageMesh`
- if runtime tier is `L1`, decode `shellNodesL0`
- if runtime tier is `L2`, decode `shellNodesL1`
- if runtime tier is `L3`, decode `shellNodesL2`

For each `NodeDecisionGPU`:
- `Reject` -> stop on that node
- `EmitSelf` -> add that node's `shellDrawSlot` to the per-draw-slot visible list
- `ExpandChildren` -> enqueue the node's contiguous immediate-child block using BFS contract

Preferred MVP path:
- decode the visible frontier on GPU and append final per-draw-slot visible instance data directly

CPU fallback path:
- read back compact decision buffers through completed non-blocking async results only when the GPU decode path is unavailable or explicitly disabled
- decode the visible frontier on CPU from the same BFS contract

After survivor decode:
- select one trunk draw for each expanded tree:
  - if any surviving branch decision is `L0` or `L1`, emit `trunkMesh`
  - otherwise emit `trunkL3Mesh`
- group all visible instances by exact mesh/material draw slot
- upload instance transforms and indirect args
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
- `NodeDecisions`
- decoded visible instance lists per draw slot (`GPU` preferred, `CPU` fallback)
- indirect argument buffers per draw slot

## 7.3 Flow

Tree -> Expanded or Impostor -> BranchDecision -> NodeDecision -> CPU/GPU BFS decode -> DrawSlot -> Indirect call

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
- compute dispatch ordering
- optional non-blocking `AsyncGPUReadback` scheduling for the CPU fallback path
- indirect args upload
- indirect depth pass
- indirect color pass

The renderer feature is the rendering integration point. It is not a BRG wrapper.

---

# 9. Developer Verification

Milestone 1 should not add a large new runtime test plan yet. It must add strong developer-side verification for the hybrid decode path.

Required developer verification:
- dump one flattened hierarchy per tier and confirm:
  - root at index `0`
  - contiguous child block starts at `firstChildIndex`
  - child block order matches ascending octant bits in `childMask`
  - child depth is parent depth + 1
  - child bounds stay inside parent bounds
  - mesh bounds stay inside node `localBounds`
- log one frame of `BranchDecisionGPU` output and verify selected runtime tiers match authored distances
- log one frame of `NodeDecisionGPU` output and verify the CPU-decoded frontier matches the emitted shell nodes for that camera position
- compare per-draw-slot decoded instance counts against uploaded indirect args
- verify the GPU decode path produces the same visible frontier as the CPU fallback on one captured frame
- if CPU fallback is active, verify its readback path stays non-blocking and only consumes completed async results
- verify `L0` only appears inside the authored near band
- verify `Impostor` only appears once tree distance reaches the impostor band
- verify invalid generated meshes still render, while validation marks the asset invalid in tooling
- if Unity imports `VegetationClassify.compute` without exposing the expected kernels, fail explicitly and skip GPU parity verification instead of silently faking GPU success

---

# 10. Deferred Optimization

Deferred items:
- DFS preorder plus subtree spans
- fully GPU-driven frontier decode
- HiZ depth pyramid occlusion
- dithered transitions
- hierarchical wind refinement
- scale quantization
- streaming and dynamic loading

---

# 11. Conclusion

This system targets large-scale vegetation in Unity by combining:
- reusable authored branches
- bounded baked shell hierarchies
- GPU visibility and hierarchy survival decisions
- GPU-primary BFS decode for MVP with a temporary CPU fallback
- exact per-draw-slot indirect submission

The runtime authority is now:
- runtime `L0/L1/L2/L3 + Impostor`
- authored shell arrays shifted into runtime `L1/L2/L3`
- BFS hierarchy flattening for MVP
- GPU-primary survivor decode before indirect submission, with a temporary non-blocking CPU fallback
- multiple indirect calls grouped by exact mesh/material slots, not one literal draw call

---

END
