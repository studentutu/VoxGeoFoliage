# Urgent Redesign - Tree-First Prioritization Without BFS

## Purpose

This document defines the urgent runtime redesign for dense vegetation under one-container budget pressure.

Target failure case:
- camera stands inside a dense forest
- one container may own around `6000` tightly packed trees
- near visible trees must not disappear because branch-heavy content or slot order consumed the shared budget first

Urgent priorities:
- near-camera correctness first
- runtime memory first
- stable degradation by quality, never by random disappearance
- remove branch-shell BFS entirely from the production path
- remove pre-populated per-scene branch runtime ownership from the default path

Explicit scope:
- focus on one active container only
- leave multi-container clashes as a note, not as urgent implementation scope
- keep occlusion out of this urgent redesign
- keep current renderer backend model: GPU classification plus indirect submission

Reason for the scope cut:
- the production bug is not missing occlusion
- the production bug is wrong acceptance order plus too much static branch-owned runtime memory
- fixing near-tree survival and deleting branch-BFS runtime ownership must happen before anything else

---

## critical flaws

- Current overflow behavior is decided too late and by the wrong owner. Slot packing still decides survival after branch-heavy work was already generated.
- Current runtime work explodes one visible tree into branch work before the system decides whether that tree even deserves detail.
- `SceneBranches[]` is still the wrong static owner for the urgent path. It is bounded, but measured data already proved bounded is still too expensive for dense repeated trees.
- Branch-shell BFS is the wrong abstraction for the urgent production fix. It adds runtime state, branch decision state, shell-node buffers, and scan work without solving near-camera correctness.
- The current urgent draft overcommitted to direct whole-tree meshes for every tier. That is too rigid for `L0`.
- Using `impostorMesh` as the emergency near fallback is wrong. That replaces missing trees with an obviously incorrect near representation.
- Current docs blurred two different goals:
  - guarantee one cheap visible representation per tree
  - provide optional expanded detail for the nearest trees
- The old budget draft over-reserved by tier. Hard reserves for every detail tier waste headroom and are the opposite of runtime-memory-first design.

---

## missing pieces

- A hard invariant for the minimum representation of one visible tree under pressure.
- A tree-first acceptance stage before any expanded branch work is generated.
- A no-BFS branch representation contract for `L1/L2/L3`.
- A precise definition of what `L0` means after BFS removal.
- A compact runtime ownership model that does not pre-populate `SceneBranches[]` for the whole container.
- A cost model for promotion that accounts for branch-expanded tiers being more expensive than one-tree proxies.
- Deterministic nearest-first promotion with stable tie-breaks and hysteresis.
- Validation for one-container dense-forest guarantees.
- Telemetry that proves the new path is branch-memory-light and no longer survival-by-slot-order.

---

## terminology

| Term | Status | Purpose |
| --- | --- | --- |
| `Visible instance` | Shipped | One final draw-ready packed instance emitted into the shared container-scoped visible-instance buffer. |
| `Draw slot` | Shipped | One exact runtime submission bucket defined by `Mesh + Material + MaterialKind`. |
| `Indirect submission` | Shipped | One final indirect draw for one active draw slot in one pass. |
| `TreeVisibilityRecord` | Planned redesign | Per-visible-tree frame record used for tree-first acceptance and promotion before branch expansion. |
| `TreeL3 floor` | Planned redesign | One whole-tree `L3` mesh used as the mandatory near/mid fallback under pressure. This is not `impostorMesh`. |
| `Expanded tree tier` | Planned redesign | Tree accepted above the `TreeL3 floor` and expanded into branch placements using one canopy mesh plus one wood mesh per survived branch tier. |
| `Survived branch` | Planned redesign | One branch placement that survives coarse branch visibility tests for an already accepted expanded tree. No shell-node traversal is involved. |
| `Branch split tier` | Planned redesign | One baked branch prototype canopy mesh plus one baked branch wood mesh for `L1`, `L2`, or `L3`. No BFS, no shell frontier, no shell-node arrays. |
| `CompactExpandedBranchWorkItem[]` | Planned redesign | Compact per-frame branch worklist generated only for promoted trees. This replaces `SceneBranches[]` in the urgent path. |
| `Legacy scene branch owner` | Legacy path | `SceneBranches[]` static registration for every scene branch in the container. This is not allowed in the urgent production path. |

Critical naming rule:
- `L0` no longer means branch-shell traversal or expanded hierarchy decode
- `L0` means: survived branch placements rendered with original branch meshes
- `L1/L2/L3` no longer mean shell hierarchy frontiers
- `L1/L2/L3` mean: survived branch placements rendered with one baked canopy mesh plus one baked wood mesh for that tier
- the guaranteed tree floor inside near and mid distance bands is `TreeL3`, a whole-tree mesh
- `impostorMesh` stays far-only

---

## current shipped lifecycle

### registration / flattening path

```text
VegetationRuntimeContainer.registeredAuthorings
-> VegetationTreeAuthoringRuntime[]
-> VegetationRuntimeRegistry
   -> DrawSlots[]
   -> TreeInstances[]
   -> SceneBranches[]
   -> BranchPrototypes[]
   -> ShellNodesL1/L2/L3[]
   -> SpatialGrid
```

### per-camera / per-frame path

```text
Camera
-> ClassifyCells
-> ClassifyTrees
-> ClassifyBranches on full SceneBranches[]
-> CountTrees
-> CountBranches on full SceneBranches[]
-> BuildSlotStarts
-> EmitTrees
-> EmitBranches on full SceneBranches[]
-> FinalizeIndirectArgs
-> submit every registered slot after bind
```

Why this is broken:
- tree acceptance is too late
- branch work is global instead of near-tree-driven
- BFS metadata exists but does not solve the urgent bug
- the runtime still allocates and uploads static branch-owned data for the whole container

Measured proof from the dense repeated-branch sample:
- `trees=6087`
- `sceneBranches=316524`
- about `129.2 MiB` of branch-review buffers before the visible-instance capacity buffer
- `17` non-zero emitted slots versus `768` registered slots

That is enough evidence. The production issue is not theoretical.

---

## redesign target

### hard scope rules

- Urgent implementation scope is one container only.
- Multi-container near-field conflicts remain a documented limitation for now.
- Near-camera correctness is solved by tree-first acceptance, not by occlusion.
- BFS is removed from the production runtime path.
- Pre-populated `SceneBranches[]` is removed from the production runtime path.
- Runtime memory wins are more important than preserving the old branch decode structure.

### non-negotiable runtime invariants

- Every visible near or mid tree must render at least one `TreeL3` representation every frame.
- `impostorMesh` is not the emergency near fallback. `TreeL3` is.
- Overflow must degrade quality, not erase arbitrary trees.
- Nearer visible trees must promote before farther visible trees.
- Survival must be decided before expanded branch work is generated.
- Slot order must never decide survival.
- `L0` means survived branches rendered with original meshes. No BFS. No shell hierarchy. No frontier traversal.
- `L1/L2/L3` expanded branch tiers mean survived branches rendered with one baked canopy mesh plus one baked wood mesh per branch prototype tier. No BFS. No shell hierarchy. No frontier traversal.
- Final submission compaction is downstream from acceptance. It removes empty submissions; it does not decide survival.

---

## representation contract

### 1. Far-only representation

- `Impostor`
  - one baked whole-tree far mesh
  - used only in the far band
  - not used as the emergency near fallback

### 2. Mandatory near / mid floor

- `TreeL3`
  - one baked whole-tree `L3` mesh
  - used as the guaranteed representation for any visible tree inside the expanded bands when budgets are tight
  - chosen specifically because runtime memory comes first and one tree must always collapse to one cheap accepted instance

### 3. Expanded tree tiers

- `L2`
  - tree expands into survived branch placements
  - each survived branch uses the branch prototype's baked `L2` canopy mesh plus baked `L2` wood mesh
  - no shell-node arrays
  - no BFS

- `L1`
  - tree expands into survived branch placements
  - each survived branch uses the branch prototype's baked `L1` canopy mesh plus baked `L1` wood mesh
  - no shell-node arrays
  - no BFS

- `L0`
  - tree expands into survived branch placements
  - each survived branch uses the original source branch mesh pair
  - this is the highest detail urgent tier
  - no shell-node arrays
  - no BFS

Hard rule:
- one visible tree chooses exactly one accepted tree tier for the frame:
  - `Impostor`
  - `TreeL3`
  - `L2`
  - `L1`
  - `L0`
- mixed branch tiers inside one tree are out of scope for the urgent path

### 4. Branch authoring simplification

Branch authoring after the redesign must stop persisting branch-shell hierarchies for runtime use.

Required branch runtime bake outputs:
- original `woodMesh` / `foliageMesh` for `L0`
- one baked branch `L1` canopy mesh
- one baked branch `L1` wood mesh
- one baked branch `L2` canopy mesh
- one baked branch `L2` wood mesh
- one baked branch `L3` canopy mesh
- one baked branch `L3` wood mesh

Hard rule:
- no `shellNodesL0`
- no `shellNodesL1`
- no `shellNodesL2`
- no runtime BFS metadata
- no per-tier shell frontier decode

Important note:
- branch `L3` still exists as an authored simplification product
- urgent runtime floor is still `TreeL3`, not branch-expanded `L3`, because branch-expanded fallback cannot guarantee one visible tree = one cheap accepted instance

---

## ownership model

### static runtime owners after redesign

- `SpatialGrid`
  - tree-to-cell visibility ownership only

- `TreeInstances[]`
  - authoritative urgent-path runtime owner
  - owns tree transforms, bounds, blueprint handle, tree `L3` slot identity, impostor slot identity, and accepted tier state

- `TreeBlueprints[]`
  - owns tree-level meshes and per-tree static LOD cost data
  - must include:
    - `treeL3Mesh`
    - `impostorMesh`
    - branch placement span / count
    - static tier costs for `L2`, `L1`, and `L0`

- `BlueprintBranchPlacements[]`
  - reusable branch placement data per blueprint
  - not duplicated per scene tree

- compact branch prototype tier table
  - reusable branch prototype mesh/material handles for:
    - `L0 original`
    - `L1 canopy mesh`
    - `L1 wood mesh`
    - `L2 canopy mesh`
    - `L2 wood mesh`
    - `L3 canopy mesh`
    - `L3 wood mesh`

- `DrawSlots[]`
  - final submission identities only

### legacy runtime owners removed from urgent path

- `SceneBranches[]`
- `ShellNodesL1/L2/L3[]`
- branch-shell BFS metadata
- full-container branch decision buffers
- full-container branch classify / count / emit stages

### hard rule on `SceneBranches[]`

- production urgent path must not pre-populate `SceneBranches[]`
- if branch-expanded work exists, it must be generated only for trees already accepted above `TreeL3`
- scene-wide static branch ownership is banned in the urgent path

This is the runtime-memory-first requirement. Minimizing `SceneBranches[]` is weaker than removing it from the default path.

---

## budget model

The old draft used too many hard caps. That is unnecessary complexity.
Urgent path keeps one hard runtime budget only:

```text
availableVisibleInstanceCapacity
= VegetationRuntimeContainer.maxVisibleInstanceCapacity - safetyMargin
```

Hard rules:
- do not add a separate complex `VegetationRuntimeBudgetProfile`
- keep one visible-instance budget per container
- every visible non-far tree is guaranteed to render
- if optional detail consumed too much capacity, demote farther accepted content first
- farther trees fall back before nearer trees
- `impostorMesh` is already the far fallback
- `TreeL3` is the mandatory non-far fallback
- if even the mandatory baseline cannot fit after all optional detail is removed, the container configuration is invalid and validation must fail explicitly

### promotion model

Promotion depends on two things only:
- distance to camera
- static cost of the target LOD tier

That cost must be precomputed and conservative.

Required static cost model:
- `Impostor cost = 1`
- `TreeL3 cost = 1`
- `L2 cost = static blueprint cost`
- `L1 cost = static blueprint cost`
- `L0 cost = static blueprint cost`

Hard rule:
- no per-camera dynamic promotion cost estimation
- no branch-survival-dependent promotion budgeting
- nearest-first ordering comes only from distance to camera
- cost check comes only from static tier cost

Acceptance order:
1. far trees are assigned `Impostor`
2. every visible non-far tree is assigned `TreeL3`
3. nearest trees are promoted to `L2`, then `L1`, then `L0` while capacity remains
4. if capacity is exceeded, remove farther optional promotions first
5. if capacity is still exceeded, push farther trees down to their farther valid tier
6. a visible non-far tree may never disappear; the system must keep at least `TreeL3`

---

## new runtime records

```text
TreeVisibilityRecord
- int treeIndex
- float treeDistance
- int priorityRing
- bool visible
- int desiredTier
- int acceptedTier
- int acceptedTierCost
- bool acceptedTreeL3
- bool acceptedExpanded
```

```text
ExpandedTreeRecord
- int treeIndex
- int acceptedTier          // L2, L1, or L0 only
- int blueprintIndex
- int branchPlacementStart
- int branchPlacementCount
```

```text
ExpandedBranchWorkItem
- int treeIndex
- int branchPlacementIndex
- int acceptedTier          // tree-wide accepted tier
```

Hard rule:
- `ExpandedBranchWorkItem` is generated only from `ExpandedTreeRecord`
- there is no static `SceneBranches[]` array for the whole container

---

## new frame pipeline

### Stage A - classify visible trees

- classify visible cells
- classify visible trees
- reject by absolute cull distance
- classify far trees directly to `Impostor`
- classify near / mid trees into desired tree tier candidates

At this stage there is still no branch-expanded work.

### Stage B - accept mandatory `TreeL3` floor

- build `TreeVisibilityRecord` for every visible near / mid tree
- accept one `TreeL3` representation for every visible near / mid tree first
- remove farther optional promotions first if they block the guarantee
- if the baseline still cannot fit, the container is invalid

Result:
- every visible non-far tree is guaranteed to render

### Stage C - nearest-first promotion

- sort or bucket visible trees nearest-first
- promote trees from `TreeL3` upward through:
  - `TreeL3 -> L2 -> L1 -> L0`
- use remaining capacity under the one shared visible-instance budget
- use distance order only plus static tier cost
- stop promoting when the next promotion would break the guarantee

Operational rule:
- promotion replaces the previously accepted representation
- if a tree cannot afford the next tier, it stays at the last accepted lower tier

### Stage D - generate expanded branch work only for promoted trees

- build `ExpandedTreeRecord` only for trees accepted at `L2/L1/L0`
- generate `CompactExpandedBranchWorkItem[]` only from those promoted trees
- iterate branch placements from blueprint-owned data
- derive world-space branch transforms and bounds on demand from:
  - tree transform
  - blueprint placement
  - branch prototype local bounds

Critical consequence:
- no prebuilt `SceneBranches[]`
- no branch buffer for the whole container
- no branch decision work for trees that never left `TreeL3`

### Stage E - survive branches without BFS

For each `ExpandedBranchWorkItem`:
- run coarse branch visibility against the camera
- if the branch survives, emit separate canopy and wood content from the tree's accepted tier:
  - `L2` -> branch prototype `L2` canopy mesh + `L2` wood mesh
  - `L1` -> branch prototype `L1` canopy mesh + `L1` wood mesh
  - `L0` -> original foliage mesh + original wood mesh

No BFS rules:
- no shell-node arrays
- no hierarchy child links
- no subtree traversal
- no frontier decode

### Stage F - count, pack, and emit accepted content

- count:
  - accepted far impostors
  - accepted `TreeL3`
  - survived branches from accepted expanded trees
- build packed slot starts only after acceptance is final
- emit only accepted content into the shared visible-instance buffer

Slot packing is now a late packing detail, not a survival policy.

### Stage G - compact final submissions

- build compact active-slot indices from non-zero emitted slots only
- submit only those active slots in depth and color passes

Hard rule:
- this stage removes empty submission waste only
- it does not influence acceptance or overflow survival

---

## why this solves the urgent production bug

Current runtime fails because:
- it creates branch work for too much content
- it stores too much static branch-owned state
- it lets slot packing decide what survives

The redesigned path fixes that because:
- every visible near / mid tree gets `TreeL3` first
- only the nearest trees spend promotion budget
- only promoted trees generate compact branch-expanded work
- branch-expanded tiers use separate canopy and wood meshes per survived branch, not BFS
- `SceneBranches[]` is removed from the default urgent path
- shell-node runtime memory disappears from the production path

Worst case:
- all visible near / mid trees still render as `TreeL3`

Better case:
- nearest trees promote to `L2`, then `L1`, then `L0`

This is the correct trade:
- stable presence first
- optional detail second
- no branch hierarchy state unless the tree already earned it

---

## required contract changes

### authoring / bake

- add `treeL3Mesh` as a required tree-level runtime floor mesh
- keep `impostorMesh` as far-only
- remove runtime dependence on `shellNodesL0/L1/L2`
- bake one branch canopy mesh and one branch wood mesh per prototype for `L1/L2/L3`
- keep original branch meshes for `L0`
- validate monotonic detail reduction and bounds for:
  - `treeL3Mesh`
  - branch `L1` canopy + wood
  - branch `L2` canopy + wood
  - branch `L3` canopy + wood
  - `impostorMesh`

### runtime registry

- move urgent-path ownership to tree-first records
- remove pre-populated `SceneBranches[]`
- remove shell-node and BFS registry surfaces
- keep only reusable per-blueprint branch placements, compact branch prototype tier handles, and generated `CompactExpandedBranchWorkItem[]`

### runtime compute / emission

- remove full-container branch classify / count / emit stages
- add tree-first acceptance and promotion stages
- add promoted-tree-only branch work generation
- add active-slot compaction

### validation

- explicit failure if `treeL3Mesh` is missing
- explicit failure if one container cannot satisfy its guaranteed `TreeL3` floor count
- explicit failure if branch tier meshes required by the urgent path are missing

---

## dense-forest validation

Required scenario:
- one container
- `6000`-`12000` tightly packed trees
- camera inside the forest

Must prove:
- every visible near / mid tree renders at least `TreeL3`
- nearer trees promote before farther trees
- `L0` renders survived original branches only
- `L1/L2/L3` use separate canopy and wood meshes per branch tier
- no shell-node buffers are allocated
- no pre-populated `SceneBranches[]` buffer is allocated
- final submissions track non-zero emitted slots only

Required telemetry:
- visible tree count
- accepted `TreeL3` count
- promoted tree counts for `L2/L1/L0`
- rejected promotions
- accepted tier-cost usage and remaining headroom
- generated expanded-tree count
- generated compact expanded-branch work-item count
- registered draw slots
- non-zero emitted slots
- final submitted slots
- exact branch-owned runtime bytes

Success condition for branch-owned runtime bytes:
- no scene-wide branch-owned static buffer should remain in the urgent path

---

## migration plan

### Phase 1 - delete BFS from the urgent path

- stop planning branch-shell BFS as the urgent solution
- remove `shellNodesL1/L2/L3[]` from the urgent runtime path
- freeze `L0` as survived original branches
- freeze `L1/L2/L3` branch tiers as separate baked canopy and wood meshes per branch prototype
- require tree `L3` mesh as the guaranteed floor

### Phase 2 - tree-first acceptance

- add `TreeVisibilityRecord`
- accept `TreeL3` for every visible near / mid tree first
- add nearest-first promotion through `L2 -> L1 -> L0`
- use one shared visible-instance budget only

### Phase 3 - remove static scene branch ownership

- delete pre-populated `SceneBranches[]` from the urgent path
- generate expanded branch work only for promoted trees
- derive branch world data from tree transform plus blueprint placement on demand

### Phase 4 - slot compaction

- compact active slots to non-zero emitted slots
- stop submitting all registered slots after bind

### Phase 5 - last-priority telemetry

- add acceptance telemetry
- add branch-memory telemetry proving the old scene-branch path is gone

---

## done criteria

- one visible near / mid tree never disappears solely because another tree consumed the budget first
- one visible near / mid tree always has at least `TreeL3`
- `impostorMesh` stays far-only
- `L0` means survived original branch meshes only
- `L1/L2/L3` no longer depend on BFS or shell-node traversal
- branch authoring keeps separate canopy and wood runtime tiers plus original source meshes
- urgent runtime path no longer pre-populates `SceneBranches[]`
- urgent runtime path no longer allocates shell-node buffers
- nearest trees promote first inside one container
- final submissions follow non-zero emitted slots only
- multi-container prioritization is documented as unresolved follow-up, not silently implied as solved

---

## resolved decisions

1. `L1/L2/L3` branch tiers use separate canopy and wood meshes per level. They must stay separate because materials and submission paths are separate.
2. Urgent implementation scope is one container only. Multi-container clashes stay as a later note.
3. `SceneBranches[]` is removed from the urgent path and replaced by `CompactExpandedBranchWorkItem[]` generated only for promoted trees.
4. Budgeting stays simple: one shared visible-instance budget per container, no complex profile with many hard caps.
5. Promotion order depends on distance to camera only. Cost checks depend on static LOD tier cost only.
6. Every visible non-far tree is guaranteed to render. If capacity is tight, farther optional detail is removed first and farther trees are pushed to their farther valid tier before a visible non-far tree loses `TreeL3`.

## open questions

1. Do we want explicit hysteresis bands per tree tier to prevent `TreeL3 <-> L2` popping once the urgent refactor is working?
Answer: no.
2. When multi-container prioritization becomes urgent later, do we want one camera-local arbiter as the first follow-up?
Answer: skip for now. Ask again when full urgent redesign landed and verified.

## Repo notes (clean after big feature/design is done)
- Project-local custom vegetation materials are not first-class yet because runtime rendering still resolves final materials through `VegetationIndirectMaterialFactory` rather than an explicit compatible-material contract.
- `TreeInstances[]` is now the approved redesign target for urgent-path acceptance ownership. It should own tree-first visibility, `TreeL3` floor identity, and nearest-first promotion decisions, while branch-expanded work is generated only for promoted trees from reusable blueprint placements, compact branch prototype tier meshes with separate canopy/wood per level, and a compact per-frame promoted-tree branch worklist instead of `SceneBranches[]`.
- The current redesign authority no longer treats branch-shell BFS as the next urgent step. Urgent task `#1` is  tree-first acceptance with guaranteed `TreeL3` floor plus nearest-first promotion into branch-expanded `L2/L1/L0`, with `L0` defined as survived original branches and BFS removed from the production path.
- `VegetationRendererFeature`  owns the shared `VegetationClassify.compute` asset reference through `VegetationFoliageFeatureSettings.ClassifyShader`, feature-scoped runtime diagnostics through `VegetationFoliageFeatureSettings.EnableDiagnostics`, and consumes active `AuthoringContainerRuntime` instances from the shared runtime-owner registry instead of discovering `VegetationRuntimeContainer` directly.
- Current shipped runtime-review telemetry is behind `VegetationFoliageFeatureSettings.EnableDiagnostics` and now logs per-container registration counts, `TreeBlueprints[]`, exact allocated GPU bytes for `branchBuffer`, `branchDecisionBuffer`, prototype buffer, shell-node buffers, `visibleInstanceCapacityBytes`, and one-shot prepared-frame readback totals for `nonZeroEmittedSlots`, `emittedVisibleInstances`, and `emittedVisibleInstanceBytes`.
- `DetailedDocs/urgentRedesign.md` now sequences the urgent path explicitly: near-tree prioritization decides one accepted tree tier per visible tree first (`TreeL3`, `L2`, `L1`, or `L0`), branch-expanded work is generated only for promoted trees through a compact per-frame worklist, and active-slot filtering compacts final submissions down to the non-zero emitted slot set. Submission compaction must not be allowed to influence survival decisions.
- The current shell bake path can intentionally collapse compact `L1/L2` tiers to a single root node when the compact mesh is cheaper than keeping the child frontier. The observed `759/1/1` shell-tier shape is therefore an intended bake outcome for that prototype, not a runtime registration bug.
- Runtime container debug exposure is batch-level only in production flow; exact CPU-side visible-instance mirrors are not available.
- Current shipped overflow behavior is still structurally weak: branch and shell detail are classified independently, accepted implicitly by slot-order packing, and then clamped by packed-buffer capacity. `DetailedDocs/urgentRedesign.md` is the current redesign authority for replacing slot-order overflow with guaranteed tree presence plus nearest-first promotion buckets. Occlusion is explicitly deferred from that urgent path.
- Large forests can be split across multiple `VegetationRuntimeContainer` roots to avoid one-container overflow, but this is chunking, not a global coordinator. Total scene memory and visible capacity are the sum of all visible containers, and there is still no cross-container near-detail prioritization.
Urgent path keeps one hard runtime budget only:
- Runtime has two separate limits that must not be conflated: `VegetationRuntimeContainer.maxVisibleInstanceCapacity` is the per-container packed visible-instance cap, while `registry.DrawSlots.Count` is the draw-slot submission surface produced by unique mesh/material/material-kind combinations.
- Current shipped hierarchy state is weaker than the old docs implied: tree runtime data is still a flat branch span (`SceneBranchStartIndex + SceneBranchCount`), and branch-shell BFS metadata is uploaded prototype-local but not traversed through child links in the shader yet.
- `registry.SceneBranches[]` is currently a bounded static registration snapshot, not an exponential or unowned frame-growth structure. It grows linearly with active tree authorings and blueprint branch placements during registration rebuild, then stays fixed until the next `RefreshRuntimeRegistration()`.
- That is no longer enough to justify keeping `registry.SceneBranches[]`. The approved urgent redesign removes pre-populated `SceneBranches[]` and runtime shell-node ownership from the default path instead of preserving a slimmer scene-branch surface.

---

END
