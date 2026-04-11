# Milestone 1 - MVP: Assembled Vegetation with GPU-Resident Indirect Rendering

## Goal

Deliver a working end-to-end vegetation pipeline (similar to Unreal 5.7 new foliage assembly and rendering approach): author one tree species from reusable branch prototypes that:
- authors one tree species from reusable branch prototypes
- auto-generates shell hierarchies and far mesh output (editor only)
- preserves full source detail only in the very-near `L0` band
- simplifies the rest of the visible tree through runtime `L1`, `L2`, and `L3`
- evaluates visibility, hierarchy survival, and primary frontier decode on GPU
- submits final exact mesh-part draws through URP plus `Graphics.RenderMeshIndirect`

**Authority**: [UnityAssembledVegetation_FULL.md](UnityAssembledVegetation_FULL.md)

Ownership note:
- strict field/property/sub-object ownership for editor authoring, editor bake, runtime registration/flattening, and runtime per-frame use lives only in `UnityAssembledVegetation_FULL.md`
- this milestone tracks milestone scope, sequencing, and done criteria; it must not redefine per-field ownership separately

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
- GPU owns tree visibility, branch tier selection, shell-node survival rules, and the primary MVP frontier decode path
- rendering uses multiple indirect calls grouped by exact mesh/material slots, not one literal global draw call
- `LODProfileSO` now uses authored distance bands `l0Distance`, `l1Distance`, `l2Distance`, `impostorDistance`, and `absoluteCullDistance`;
- `Impostor` is one baked `impostorMesh` only; it combines the trunk plus all placed source branch `woodMesh` and `foliageMesh`, and bake may optionally reduce it to a front-side-only surface
- trunk selection for expanded trees is strict:
  - if any surviving branch placement is `L0` or `L1`, draw full `trunkMesh`
  - otherwise draw `trunkL3Mesh`
- generated meshes persist even when they miss budgets; validation still marks the asset invalid
- `trunkL3Mesh` is mandatory before runtime MVP work starts; runtime `L2/L3` and editor `L2/L3` are not optional side paths
- editor bake/preview authority continues to use per-placement AABB from authored `localBounds` plus per-node `localBounds`
- runtime branch tier classification uses one per-placement bounding sphere with exact sphere-surface distance:
  - `distance = max(0, length(cameraWorldPosition - sphereCenterWorld) - sphereRadiusWorld)`
- runtime shell-node visibility still uses authored node bounds
- current Phase D shell-node rule is conservative and explicit: visible internal nodes `ExpandChildren`, visible leaves `EmitSelf`, and nodes outside the frustum `Reject`; finer intra-tier collapse is deferred
- indirect submission uses scene-wide per exact draw slot and must rebuild `RenderMeshIndirect` `worldBounds` from visible data; fixed whole-scene bounds are forbidden in Milestone 1
- production runtime container is GPU-resident only; old CPU/readback decode is removed from the current rendering implementation and must not be reintroduced as a runtime fallback

Any older BRG wording or tree-wide `R0/R1/R2/R3` runtime wording is obsolete.

Specific ownership authority:
- use `UnityAssembledVegetation_FULL.md` sections `1.4`, `1.5`, `4.2`, and `5.0` as the single ownership source of truth
- runtime implementation must follow that authority when deciding which ScriptableObject fields are editor-only and which baked outputs are allowed into runtime registration

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
| GPU-resident BFS survivor decode into final draw-slot lists | GPU frame double-buffering / one-frame latency optimization |
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
- expanded-tree trunk draw is selected once per tree:
  - if any surviving branch placement is `L0` or `L1`, use `trunkMesh`
  - otherwise use `trunkL3Mesh`

### No Unity `LODGroup`

This system does not use Unity `LODGroup`. LOD selection is owned by:
- authored distance bands in `LODProfileSO`
- GPU tree classification
- GPU branch tier selection
- GPU shell-node survival decisions
- GPU-resident BFS survivor decode for MVP

---

## Task 1: Authoring Data Model [DONE]

### 1.1 ScriptableObjects

Field inventory only:
- the lists below describe the current authored shape
- field-by-field ownership and where each field may be used is defined only in `UnityAssembledVegetation_FULL.md`

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
- Milestone 1 active LOD distances must be monotonically increasing: `l0 < l1 < l2 < impostor < absoluteCull`.
- `trunkL3Mesh` triangle count must be strictly lower than `trunkMesh`.
- Shell hierarchy BFS child blocks must follow ascending octant-bit order from `childMask`.
- Over-budget generated meshes must persist, but validation must still fail and mark the authoring asset invalid.

---

## Task 2: Editor Preview [DONE]

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

## Task 3: Shell, Trunk, and Far-Mesh Baking [DONE]

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

Current editor entry points:
- `Regenerate Shells`
- `Regenerate Trunk L3`
- `Regenerate Impostor`
- `Regenerate All Generated Meshes`

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

BFS is acceptable for Milestone 1 because the current GPU path only needs immediate-child traversal. It is not the desired long-term format for fully GPU-driven traversal. After MVP, move to DFS preorder plus subtree spans.

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
   - compute branchDistance = max(0, length(cameraWorldPosition - branchSphereCenterWorld) - branchSphereRadiusWorld)
   - if branchDistance < l0Distance -> `L0`
   - else if branchDistance < l1Distance -> `L1`
   - else if branchDistance < l2Distance -> `L2`
   - else -> `L3`
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
  - float3 localBoundsCenter
  - float boundingSphereRadius

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

Branch classification ownership:
- use the single-source ownership contract from `UnityAssembledVegetation_FULL.md`; this milestone does not redefine field usage separately here

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

GPU decode is the required MVP path. The production runtime container consumes GPU-written shared instance and indirect-args buffers directly and does not route visible-frontier data back through a CPU fallback path.

Submission ownership:
- use the single-source runtime submission contract from `UnityAssembledVegetation_FULL.md`; this milestone only tracks that the implementation must follow it

Milestone 1 does not claim one literal global draw call.

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

**Pass 3: GPU-Resident Decode + Emit**
- decode the visible frontier on GPU
- emit final shared instance data and indirect args on GPU

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
- inspect one frame of uploaded GPU batch bounds/count diagnostics and confirm they match the expected visible shell frontier
- compare per-draw-slot uploaded counts against indirect args when diagnostics are available
- verify `L0` only appears inside the very-near band
- verify `Impostor` only appears once the tree reaches the impostor band
- verify `Impostor` draws `impostorMesh` only and never submits a separate trunk draw
- verify invalid generated meshes still render while tooling marks the asset invalid

---

## Phase C.5: Runtime Readiness Gate

Code-side C.5 items landed on `2026-04-05`.

Required gate checklist done:
- `trunkL3Mesh` generation and persistence are implemented and exposed through the editor bake flow
- `Packages/com.voxgeofol.vegetation/Samples~/Vegetation Demo/VoxFoliage/TreeBlueprint_branch_leaves_fullgeo.asset` is rebaked with assigned `trunkL3Mesh`
- `Assets/Tree/VoxFoliage/TreeBlueprint_branch_leaves_fullgeo.asset` is rebaked with assigned `trunkL3Mesh`
- manual in-Editor visual verification is completed on both exact tree-blueprint assets for `L0`, `L1`, `L2`, `L3`, shell-only `L1`, shell-only `L2`, shell-only `L3`, and `Impostor`
- baked `L1/L2` compact hierarchies are compared against `Assets/Tree/Raw/branch_leaves_quadcards.obj` as the current silhouette reference input
- `ShellBakeSettings` and `ImpostorBakeSettings` on both exact tree-blueprint assets pass validation without depending on last-resort `MeshLodUtility` fallback
- validator/tests verify BFS child ordering against ascending octant-bit order, not only contiguous child blocks, depth, and bounds
- runtime flattening contract is frozen: branch placement runtime data must carry authored bounds center plus bounding sphere radius, runtime branch classification uses per-placement sphere-surface distance, shell-node visibility keeps authored node bounds, and `LODProfileSO` uses the 5-band `l0/l1/l2/impostor/absoluteCull` contract
- runtime submission contract is frozen: use scene-wide per exact draw slot with per-slot bounds rebuilt from visible data; fixed whole-scene bounds are forbidden
- compile validation is rerun and the Unity EditMode vegetation suite is rerun once the long Unity path is available

---

## Phase D Completion Update (`2026-04-09`)

Phase D landed with the following concrete implementation:
- frozen runtime contracts for draw slots, flattened blueprints/prototypes/scene branches, BFS shell-node caches, branch decisions, node decisions, indirect-arg seeds, and per-slot visible-instance outputs
- `VegetationSpatialGrid` deterministic tree-to-cell registration with conservative resident cell bounds
- `VegetationRuntimeRegistryBuilder` / `VegetationRuntimeRegistry` flattening from scene authoring into runtime caches without reading editor-only fields
- `VegetationRuntimeContainer` orchestration and runtime tree-index assignment
- `VegetationGpuDecisionPipeline` plus `VegetationClassify.compute` as the GPU-resident runtime classification/emission path against the frozen contracts
- EditMode coverage for registration, bounds math, and GPU-resident renderer binding contracts

Validation status:
- `Fully Compile by Unity` succeeded on `2026-04-09`
- Unity EditMode tests passed on `2026-04-09`
- current batch-mode Unity environment imports `VegetationClassify.compute` without exposing the expected kernels, so the GPU parity test is intentionally ignored there and the runtime GPU hook throws explicit `NotSupportedException` instead of pretending GPU success

## Phase E Implementation Update (`2026-04-10`)

Phase E now has a working runtime render path with the following concrete implementation:
- render-side ownership is frozen between `VegetationRuntimeContainer`, `VegetationIndirectRenderer`, and `VegetationRendererFeature`
- `VegetationIndirectRenderer` binds shared GPU-written instance payloads and indirect args by exact draw slot
- runtime shader suite is implemented: `VegetationCanopyLit.shader`, `VegetationTrunkLit.shader`, `VegetationFarMeshLit.shader`, `VegetationDepthOnly.shader`, and shared `VegetationIndirectCommon.hlsl`
- `VegetationRuntimeContainer` now prepares one GPU-resident per-camera frame for Phase E consumption
- `VegetationRendererFeature` schedules indirect depth and color submission
- EditMode coverage now includes production renderer binding and conservative uploaded-batch snapshot validation

Perf stabilization update (`2026-04-11`):
- the constant per-frame GC from `VegetationRuntimeMathUtility.TransformBounds` corner-array allocation was removed from the Phase E visible-bounds path
- `VegetationRendererFeature` no longer allocates `managers.ToArray()` per render-graph pass
- profiler markers now cover runtime frame prep, decode, indirect upload, slot-capacity growth, and render-pass submission so Playground captures can isolate the next hot stage
- the completed-GPU-readback consume path no longer does a full branch/node reset before overwriting those buffers from readback payloads
- visible-cell ordering from GPU readback now rebuilds in place from the mask instead of allocating a temporary `int[]`
- Phase E decode now writes slim debug-visible payload plus final `VegetationIndirectInstanceData` in one step, and `VegetationIndirectRenderer` uploads that direct data instead of repacking every visible instance again on the CPU
- profiler markers now also split GPU readback consume, cell/tree/decision copies, and per-slot instance-buffer vs args-buffer upload so the remaining spike can be attributed to a specific substage
- `VegetationDecisionDecoder.DecodeTrees` now rejects whole trees before branch traversal, rejects whole shell tiers before scanning node decisions, and consumes precomputed per-tree/per-branch/per-node world bounds from runtime registration instead of rebuilding transformed draw-slot bounds in the decode hot loop
- runtime registration now also caches per-tree/per-branch upload payloads (`VegetationIndirectInstanceData`), and the old runtime frame-output path disables per-instance debug capture unless diagnostics are enabled so the decode path does not duplicate debug-visible payload on normal rendering frames
- `VegetationGpuDecisionPipeline` now also owns the main runtime decode/emission path: after GPU classification it resets per-slot indirect args, emits exact visible instance payloads into one shared GPU instance buffer using fixed per-slot slices from runtime registration, finalizes one indirect-args record per draw slot, and `VegetationIndirectRenderer` consumes those GPU-written buffers directly
- `VegetationRuntimeContainer` now runs GPU-resident only and bypasses `VegetationDecisionDecoder.DecodeTrees` on the main runtime path; legacy CPU/decode parity remains editor-only test tooling and no longer exists as a runtime container fallback
- final production cleanup removed the remaining editor-only decode/upload helpers from `Runtime/Rendering` and deleted the old runtime Scene-view debug MonoBehaviour

Current implementation caveats:
- GPU-resident decode is now the production runtime path, but it still needs manual Playground validation and hardening
- GPU-resident debug exposure is intentionally thinner than the removed CPU runtime path: uploaded batch bounds are conservative scene-level slot bounds and exact per-slot visible counts are not CPU-visible unless additional diagnostics are added
- the batch-mode kernel-import issue on `VegetationClassify.compute` is still open
- end-to-end scene verification for expanded trees, impostors, per-slot counts, and rebuilt `worldBounds` is still pending

Validation status:
- `Fully Compile by Unity` succeeded on `2026-04-10` and again on `2026-04-11` after removing the legacy runtime path
- Unity EditMode tests were not rerun after Phase E because the long Unity test path is still intentionally user-triggered in this repo

---

## Implementation Order

### Phase A: Foundation [DONE]
1. Align docs and runtime naming
2. Move `LODProfileSO` from projected-area thresholds to authored distance bands
3. Add `trunkL3Mesh` to tree authoring
4. Update validation to match the new contract

### Phase B: Shell / Trunk / Far-Mesh Baking [DONE]
1. Keep current authored shell hierarchy path
2. Add `trunkL3Mesh` generation
3. Keep bounded far-mesh generation
4. Preserve-invalid generated outputs

### Phase C: Editor Preview [DONE]
1. Align preview states with `L0/L1/L2/L3/Impostor`
2. Keep shell-only inspection views
3. Manual visual verification

### Phase C.5: Runtime Readiness Gate [DONE]
1. Close every item in the mandatory runtime readiness checklist above
2. Reconcile real sample assets with the current validator and preview contract
3. Freeze the runtime bounds and submission contracts before runtime implementation starts

### Phase D: Spatial Grid + Runtime Data Foundation [DONE]

1. `D1` Freeze the runtime-side data contracts that Phase D owns:
   - flattened tree/tree-blueprint/branch-placement/prototype payloads
   - BFS shell-node runtime payloads
   - branch-decision and node-decision payloads
   - per-slot visible-instance output, indirect-arg seed input, and visible-data bounds payloads for Phase E
2. `D2` Implement `VegetationSpatialGrid`:
   - deterministic tree-to-cell registration
   - authoritative cell bounds and visible-cell query output
   - no renderer coupling and no occlusion scope creep
3. `D3` Implement runtime registration/flattening from authored assets:
   - tree spheres from `treeBounds`
   - per-placement branch spheres from prototype `localBounds`
   - exact draw-slot registries
   - BFS shell-node runtime caches per authored tier
4. `D4` Implement a deterministic CPU reference mirror for the runtime rules:
   - tree mode classification (`Culled` / `Expanded` / `Impostor`)
   - branch tier selection (`L0/L1/L2/L3`)
   - shell-node decisions (`Reject` / `EmitSelf` / `ExpandChildren`)
   - trunk selection and BFS frontier decode into per-slot visible-instance outputs
5. `D5` Implement the GPU-primary visibility/classification/decision path against the same contracts:
   - visible-cell upload
   - tree classification
   - branch tier selection
   - shell-node decision production
   - GPU-primary frontier decode or equivalent final visible-list emission
6. `D6` Stabilize the Phase E handoff surface:
   - expose per-slot visible-instance data
   - expose indirect-args seed inputs
   - expose per-slot visible-data bounds rebuilt from visible outputs
   - keep `RenderMeshIndirect` submission out of Phase D
7. `D7` Add developer verification for Phase D:
   - spatial-grid EditMode coverage
   - classification/decode parity checks between CPU mirror and GPU path
   - frame-capture logging for branch decisions, node decisions, and per-slot counts
8. `D8` Compile check + targeted manual verification

Current caveat:
- the compute shader parity hook exists and is wired, but Unity batch-mode currently imports the shader without exposing its kernels; Phase E must investigate that environment issue while replacing the parity hook with the real renderer-facing GPU bridge (low priority, as developer will verify actual end product with a full solution in editor)

### Phase E: GPU-Resident Rendering Pipeline [DONE]

Status clarification (`2026-04-10`):
- Code-side render infrastructure for `E1` through `E5` is implemented.
- `E6` end-to-end demo-scene verification is still pending.
- `E7` compile check is done, but the manual verification part is still pending.
- Current runtime container is GPU-resident only; remaining work is validation and hardening, not migration away from CPU fallback.

1. `E1` Freeze render-side ownership and pass contracts before implementation:
   - `VegetationIndirectRenderer` owns per-slot visible buffers, indirect args, and visible-data `worldBounds`
   - `VegetationRendererFeature` owns URP pass scheduling, render-pass integration, and the shared `VegetationClassify.compute` asset reference, not classification authority
   - `VegetationRuntimeContainer` owns scene/runtime wiring, explicit serialized authoring ownership, and the handoff from stable Phase D outputs into rendering
   - fixed whole-scene bounds and BRG-style shortcuts remain forbidden
2. `E2` Implement the shader suite with one consistent instance-input contract:
   - `VegetationCanopyLit.shader`
   - `VegetationTrunkLit.shader`
   - `VegetationFarMeshLit.shader`
   - `VegetationDepthOnly.shader`
   - opaque-only, RSUV-compatible, and no hidden per-shader divergence in instance payload layout
3. `E3` Implement `VegetationIndirectRenderer` consumption of the Phase D outputs:
   - exact draw-slot registry consume path
   - per-slot visible-instance buffer upload
   - per-slot indirect-args build/final upload
   - per-slot `worldBounds` rebuild from visible data only
4. `E4` Implement `VegetationRuntimeContainer` runtime/renderer wiring:
   - scene registration of authored vegetation instances through an explicit serialized authorings list
   - lifetime/reset of runtime caches and GPU resources
   - handoff from Phase D visibility/decode outputs into Phase E renderer consumption
   - no editor-only field access and no bake/preview leakage into runtime
5. `E5` Implement `VegetationRendererFeature` + `VegetationRenderPass` integration:
   - frame ordering for Phase D output consumption
   - indirect depth pass submission
   - indirect color pass submission
   - no CPU fallback handoff in the production runtime path
6. `E6` End-to-end verification in a demo scene:
   - verify the full solution in editor through scene render validation, uploaded-batch diagnostics, and profiler captures
   - expanded trees render trunk, branch wood, source foliage, and shell tiers on the correct runtime bands
   - impostor trees render `impostorMesh` only
   - per-slot visible counts match uploaded indirect args and rebuilt `worldBounds`
   - one captured debug frame confirms GPU batch output matches the expected visible frontier
7. `E7` Compile check + manual verification

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
- GPU-primary survivor decode reconstructs final visible draw-slot lists correctly without any runtime CPU fallback path
- Indirect rendering draws trunk, branch wood, branch foliage, shell tiers, and far mesh correctly, and `Impostor` mode draws `impostorMesh` only
- Generated outputs remain persisted even when validation marks them invalid

---

END
