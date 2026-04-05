# Milestone 1 - MVP: Assembled Vegetation with Hybrid Decode Indirect Rendering

## Goal

Deliver a working end-to-end vegetation pipeline (similar to Unreal 5.7 new foliage assembly and rendering approach): author one tree species from reusable branch prototypes that:
- authors one tree species from reusable branch prototypes
- auto-generates shell hierarchies and far mesh output (editor only)
- preserves full source detail only in the very-near `L0` band
- simplifies the rest of the visible tree through runtime `L1`, `L2`, and `L3`
- evaluates visibility and hierarchy survival on GPU
- decodes surviving hierarchy parts on CPU for MVP
- submits final exact mesh-part draws through URP plus `Graphics.RenderMeshIndirect`

**Authority**: [UnityAssembledVegetation_FULL.md](UnityAssembledVegetation_FULL.md)

Package note (`2026-03-29`):
- Public package root: `Packages/com.voxgeofol.vegetation`
- Distributable demo content: `Packages/com.voxgeofol.vegetation/Samples~/Vegetation Demo`
- Repo-local demo mirror for current scenes: `Assets/Tree`

---

## Runtime Contract Update (`2026-04-05`)

The authoritative Milestone 1 runtime contract is now:
- runtime tiers are `L0`, `L1`, `L2`, `L3`, and `Impostor`
- runtime `L0` is the full source branch mesh and is only allowed very near the camera
- current authored shell arrays shift upward in runtime meaning:
  - authored `shellNodesL0` -> runtime `L1`
  - authored `shellNodesL1` -> runtime `L2`
  - authored `shellNodesL2` -> runtime `L3`
- GPU applies tree visibility, branch tier selection, and shell-node survival rules
- survivor decode is hybrid `CPU/GPU`, with GPU preferred when feasible and non-blocking CPU fallback otherwise
- rendering uses multiple indirect calls grouped by exact mesh/material slots, not one literal global draw call
- `LODProfileSO` should move to authored distance bands per species
- trunk stays full for runtime `L0` and `L1`, then switches to `trunkL3Mesh` for runtime `L2`, `L3`, and the `Impostor` path
- generated meshes persist even when they miss budgets; validation still marks the asset invalid

Any older BRG wording or tree-wide `R0/R1/R2/R3` runtime wording is obsolete.

---

## MVP Scope Summary

| In Scope | Out of Scope (deferred) |
|----------|-------------------------|
| Full authoring data model with real two-mesh branch prototypes (`woodMesh` + `foliageMesh`) | Auto-baking reduced geometry from arbitrary imported trees |
| Readable source meshes as a hard validation rule | HiZ depth pyramid occlusion |
| Runtime `L0` source-mesh band under authored near distance | Dithered transitions |
| Hierarchical shell generation for authored `shellNodesL0/L1/L2` | Fully GPU-driven final decode |
| Simplified branch wood generation for authored compact tiers | DFS preorder plus subtree spans |
| Simplified `trunkL3Mesh` generation | Runtime streaming / dynamic loading |
| Coarse far mesh generation from the original assembled tree | Hierarchical wind system |
| Editor preview for `L0/L1/L2/L3/Impostor` plus shell-only views | Feature-grade placement tools |
| GPU tree classification, branch tier selection, and node survival records | Random per-instance tint variation |
| Hybrid CPU/GPU BFS survivor decode into final draw-slot lists | Scale quantization optimization |
| Spatial partitioning via uniform grid | Advanced occlusion beyond coarse frustum / optional `CullingGroup` |
| `ScriptableRendererFeature` with indirect depth + color passes | Unity `LODGroup` |
| RSUV-only prototype canopy tint payload | - |
| Developer-side runtime verification tools and logs | Large new runtime test plan in this milestone |

---

## Runtime Representation Model

### Tree-Level Modes

| Mode | What Happens |
|------|--------------|
| Culled | Tree is rejected before draw emission |
| Expanded | Tree emits trunk plus branch-driven canopy parts |
| Impostor | Tree emits one coarse far mesh |

### Expanded-Tree Tiers

| Runtime Tier | Canopy Representation | Branch Wood Representation | Trunk Representation |
|--------------|------------------------|----------------------------|----------------------|
| `L0` | Source `foliageMesh` | Source `woodMesh` | Full `trunkMesh` |
| `L1` | Surviving frontier from authored `shellNodesL0` | Source `woodMesh` where needed | Full `trunkMesh` |
| `L2` | Surviving frontier from authored `shellNodesL1` | `shellL1WoodMesh` | `trunkL3Mesh` |
| `L3` | Surviving frontier from authored `shellNodesL2` | `shellL2WoodMesh` | `trunkL3Mesh` |

Authoritative rule:
- `L0` is source branch geometry and is branch-placement granularity.
- `L1/L2/L3` are hierarchy-frontier granularity.
- `Impostor` is tree-level only.

### No Unity `LODGroup`

This system does not use Unity `LODGroup`. LOD selection is owned by:
- authored distance bands in `LODProfileSO`
- GPU tree classification
- GPU branch tier selection
- GPU shell-node survival decisions
- hybrid CPU/GPU BFS survivor decode for MVP, with GPU preferred

---

## Task 1: Authoring Data Model

### Implementation Status (`2026-04-05`)

- Base authoring assets, validation, and branch/tree authoring are implemented under `Packages/com.voxgeofol.vegetation/Runtime/Authoring/`.
- `LODProfileSO` now uses authored distance bands, `TreeBlueprintSO` now exposes `trunkL3Mesh`, and editor preview/summary naming is aligned to `L0/L1/L2/L3/Impostor`.
- Remaining authoring follow-up is generation/persistence of `trunkL3Mesh`; remaining runtime follow-up is the hybrid decode path with GPU-preferred visibility and decode.
- The data contract below is authoritative for follow-up work.

### 1.1 ScriptableObjects

**`BranchPrototypeSO`**

```text
- Mesh woodMesh
- Material woodMaterial
- Mesh foliageMesh
- Material foliageMaterial
- Color leafColorTint
- string generatedCanopyShellsRelativeFolder
- BranchShellNode[] shellNodesL0   // runtime L1
- BranchShellNode[] shellNodesL1   // runtime L2
- BranchShellNode[] shellNodesL2   // runtime L3
- Mesh shellL1WoodMesh             // runtime L2 wood
- Mesh shellL2WoodMesh             // runtime L3 wood
- Material shellMaterial
- ShellBakeSettings shellBakeSettings
- Bounds localBounds
- int triangleBudgetWood
- int triangleBudgetFoliage
- int triangleBudgetShellL0
- int triangleBudgetShellL1
- int triangleBudgetShellL2
```

**`TreeBlueprintSO`**

```text
- Mesh trunkMesh
- Mesh trunkL3Mesh
- Material trunkMaterial
- BranchPlacement[] branches
  - BranchPrototypeSO prototype
  - Vector3 localPosition
  - Quaternion localRotation
  - float scale
- string generatedImpostorMeshesRelativeFolder
- Mesh impostorMesh
- Material impostorMaterial
- LODProfileSO lodProfile
- ImpostorBakeSettings impostorBakeSettings
- Bounds treeBounds
```

**`LODProfileSO`**

```text
- float l0Distance
- float l1Distance
- float l2Distance
- float l3Distance
- float impostorDistance
- float absoluteCullDistance
```

### 1.2 Validation Rules

- `trunkMesh`, `woodMesh`, and `foliageMesh` must be non-null and readable.
- All materials must be opaque. No alpha clip. No transparency.
- Tier totals must strictly reduce across authored shell tiers: authored `L0 > L1 > L2`.
- `localBounds` must fully contain `woodMesh + foliageMesh`.
- Each persisted shell node `localBounds` must fully contain its stored shell mesh bounds.
- `treeBounds` must fully contain trunk + all placed branches.
- `shellL1WoodMesh`, `shellL2WoodMesh`, `trunkL3Mesh`, and `impostorMesh` must stay inside their authoritative source bounds.
- Branch scale must be exactly in steps of `0.25`.
- LOD distances must be monotonically increasing: `l0 < l1 < l2 < l3 < impostor < absoluteCull`.
- Over-budget generated meshes must persist, but validation must still fail and mark the authoring asset invalid.

---

## Task 2: Editor Preview

### 2.1 Required Preview States

- `L0`
- `L1`
- `L2`
- `L3`
- `Impostor`
- shell-only `L1`
- shell-only `L2`
- shell-only `L3`

### 2.2 Preview Rules

- `L0` shows source branch `woodMesh + foliageMesh` only.
- `L1` rebuilds the visible frontier from authored `shellNodesL0`.
- `L2` rebuilds the visible frontier from authored `shellNodesL1` and uses `trunkL3Mesh`.
- `L3` rebuilds the visible frontier from authored `shellNodesL2` and uses `trunkL3Mesh`.
- `Impostor` shows the far mesh only.
- Preview/editor code should use the runtime labels directly: `L0`, `L1`, `L2`, `L3`, `Impostor`, plus shell-only `L1/L2/L3`.

---

## Task 3: Shell, Trunk, and Far-Mesh Baking

### 3.1 Required Outputs

Branch outputs:
- `shellNodesL0`
- `shellNodesL1`
- `shellNodesL2`
- `shellL1WoodMesh`
- `shellL2WoodMesh`

Tree outputs:
- `trunkL3Mesh`
- `impostorMesh`

### 3.2 Runtime Mapping

- authored `shellNodesL0` is consumed at runtime as `L1`
- authored `shellNodesL1` is consumed at runtime as `L2`
- authored `shellNodesL2` is consumed at runtime as `L3`

### 3.3 Invalid Result Policy

Required behavior:
- persist the latest generated shell, wood, `trunkL3Mesh`, and far mesh
- validation marks the owner invalid when budgets or rules are violated
- preview and runtime keep using the latest generated outputs
- no automatic rollback to the previous valid bake

---

## Task 4: Exact Runtime Node Contract

### 4.1 BFS Node Shape for MVP

The MVP decode contract is BFS, not DFS.

```text
BranchShellNodeRuntimeBfs
  - float3 localCenter
  - float3 localExtents
  - uint firstChildIndex
  - uint childMask
  - uint shellDrawSlot
```

### 4.2 Required BFS Invariants

- root node is index `0`
- immediate children form one contiguous block starting at `firstChildIndex`
- `childMask` defines which octants are present in that block
- contiguous child order is ascending octant-bit order
- child depth is parent depth + 1
- child bounds stay inside parent bounds
- node mesh bounds stay inside node bounds

### 4.3 Why BFS Is Only Temporary

BFS is acceptable for Milestone 1 because CPU does the final decode. It is not the desired long-term format for fully GPU-driven traversal. After MVP, move to DFS preorder plus subtree spans.

---

## Task 5: GPU Compute Classification and Survival Records

### 5.1 `VegetationClassify.compute`

Milestone 1 should use a multi-stage compute flow. The old "one tree thread does everything" model is not the authority anymore.

Input buffers:
- `_TreeInstances`
- `_TreeBlueprints`
- `_BranchPlacements`
- `_BranchPrototypes`
- `_BranchShellNodesL0`
- `_BranchShellNodesL1`
- `_BranchShellNodesL2`
- `_LODProfiles`
- `_CellVisibility`

Output buffers:
- `_ExpandedTrees`
- `_ImpostorTrees`
- `_BranchDecisions`
- `_NodeDecisions`

### 5.2 Classification Steps

```text
1. LOAD TreeInstanceGPU + TreeBlueprintGPU + LODProfileGPU
2. CELL CHECK
3. TREE DISTANCE CHECK
4. ABSOLUTE CULL
5. IF treeDistance >= impostorDistance:
   - emit one Impostor tree record
   - stop
6. ELSE:
   - emit one Expanded tree record
7. FOR each Expanded tree:
   - classify each branch placement into runtime L0 / L1 / L2 / L3
8. FOR each shell-tier branch:
   - evaluate the selected authored BFS hierarchy
   - emit `NodeDecisionGPU` records with `Reject`, `EmitSelf`, or `ExpandChildren`
```

### 5.3 Runtime Data Structs

```text
TreeInstanceGPU
  - float3 position
  - float uniformScale
  - uint blueprintIndex
  - uint cellIndex
  - float boundingSphereRadius

TreeBlueprintGPU
  - uint lodProfileIndex
  - uint branchPlacementStartIndex
  - uint branchPlacementCount
  - uint trunkFullDrawSlot
  - uint trunkL3DrawSlot
  - uint impostorDrawSlot

BranchPlacementGPU
  - float3 localPosition
  - float uniformScale
  - float4 localRotation
  - uint prototypeIndex

BranchPrototypeGPU
  - uint woodDrawSlotL0
  - uint woodDrawSlotL1
  - uint woodDrawSlotL2
  - uint woodDrawSlotL3
  - uint foliageDrawSlotL0
  - uint shellNodeStartIndexL1
  - uint shellNodeCountL1
  - uint shellNodeStartIndexL2
  - uint shellNodeCountL2
  - uint shellNodeStartIndexL3
  - uint shellNodeCountL3
  - uint packedLeafTint

BranchDecisionGPU
  - uint treeIndex
  - uint branchPlacementIndex
  - uint runtimeTier

NodeDecisionGPU
  - uint treeIndex
  - uint branchPlacementIndex
  - uint runtimeTier
  - uint nodeIndex
  - uint decision
```

---

## Task 6: Hybrid Survivor Decode and Indirect Submission

### 6.1 Decode Rules

For each `BranchDecisionGPU`:
- if tier is `L0`, emit source branch `woodMesh + foliageMesh`
- if tier is `L1`, decode authored `shellNodesL0`
- if tier is `L2`, decode authored `shellNodesL1`
- if tier is `L3`, decode authored `shellNodesL2`

For each `NodeDecisionGPU`:
- `Reject` -> stop on that node
- `EmitSelf` -> add that node's `shellDrawSlot`
- `ExpandChildren` -> enqueue its contiguous immediate-child block using BFS contract

### 6.2 Final Submission Rule

GPU decode is preferred when feasible. If that is not ready for MVP, CPU groups decoded visible instances by exact draw slot from completed non-blocking async results and uploads:
- per-slot visible instance data
- per-slot indirect args

URP then renders multiple indirect calls, one per exact mesh/material slot. Milestone 1 does not claim one literal global draw call.

---

## Task 7: `ScriptableRendererFeature` + Indirect Passes

### 7.1 `VegetationRendererFeature`

Required stages:

**Pass 1: Clear + Upload**
- clear staging buffers and indirect args
- upload per-frame cell visibility

**Pass 2: GPU Classification**
- dispatch tree classification
- dispatch branch tier selection
- dispatch shell-node survival decision passes

**Pass 3: Hybrid Decode Bridge**
- decode the visible frontier on GPU when feasible
- otherwise read back compact GPU decision buffers through completed non-blocking async results
- keep the CPU fallback decode path available until GPU decode is stable
- upload final per-slot instance data and indirect args

**Pass 4: Vegetation Depth Pass**
- render indirect depth

**Pass 5: Vegetation Color Pass**
- render indirect color

---

## Task 8: Developer Verification

Milestone 1 should prefer strong developer-side verification over a large new runtime test plan.

Required verification:
- dump one flattened hierarchy per authored tier and confirm contiguous BFS child blocks and child-mask ordering
- log one frame of branch tier decisions and check they match authored distance bands
- log one frame of node decisions and confirm CPU frontier decode matches visible shell nodes in the preview
- compare per-draw-slot decoded counts against uploaded indirect args
- compare one captured GPU-decoded frontier against the CPU fallback decode on the same inputs
- verify `L0` only appears inside the very-near band
- verify `Impostor` only appears once the tree reaches the impostor band
- verify invalid generated meshes still render while tooling marks the asset invalid

---

## Implementation Order

### Phase A: Foundation
1. Align docs and runtime naming
2. Move `LODProfileSO` from projected-area thresholds to authored distance bands
3. Add `trunkL3Mesh` to tree authoring
4. Update validation to match the new contract

### Phase B: Shell / Trunk / Far-Mesh Baking
1. Keep current authored shell hierarchy path
2. Add `trunkL3Mesh` generation
3. Keep bounded far-mesh generation
4. Preserve-invalid generated outputs

### Phase C: Editor Preview
1. Align preview states with `L0/L1/L2/L3/Impostor`
2. Keep shell-only inspection views
3. Manual visual verification

### Phase D: Spatial Grid + MVP Visibility/Decode Mirror
1. Implement `VegetationSpatialGrid`
2. Implement the MVP visibility and decode mirror with GPU-preferred design and CPU fallback
3. Compile check + manual verification

### Phase E: Hybrid Decode Runtime Pipeline
1. Implement compute tree classification
2. Implement compute branch tier selection
3. Implement compute node decision emission
4. Implement GPU survivor decode when feasible, with a non-blocking `AsyncGPUReadback` CPU fallback bridge
5. Implement indirect renderer and renderer feature integration
6. End-to-end manual verification in a demo scene

---

## Done Criteria

- One tree species is authored as a `TreeBlueprintSO` using reusable branch prototypes
- Source branch meshes validate as readable
- Authored shell `L0/L1/L2` are auto-generated from branch foliage geometry
- `trunkL3Mesh` is auto-generated and bounded
- `impostorMesh` is auto-generated from the original assembled tree
- Editor preview shows runtime `L0/L1/L2/L3/Impostor` correctly
- Runtime tree classification selects `Expanded` or `Impostor` correctly
- GPU emits branch tier and node survival records
- Survivor decode reconstructs final visible draw-slot lists correctly, either on GPU or through the non-blocking CPU fallback
- Indirect rendering draws trunk, branch wood, branch foliage, shell tiers, and far mesh correctly
- Generated outputs remain persisted even when validation marks them invalid

---

END
