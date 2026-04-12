# Urgent Redesign - Priority-Bucket Runtime Submission

## Purpose

This document defines the urgent redesign required for runtime vegetation submission under dense-forest budget pressure.

Target failure case:
- camera stands inside a very dense forest
- one container may own around `6000` tightly packed trees
- current runtime budget must never cause whole visible trees to disappear just because detailed branch and shell output consumed the packed-instance buffer first

Explicit scope:
- add primitive priority by distance to camera
- do not add URP-pass occlusion to this urgent redesign
- do not add HiZ / depth-pyramid occlusion to this urgent redesign
- guarantee stable rendering under overflow by degrading detail, not by dropping arbitrary trees

Reason for the scope cut:
- the current production bug is driven by one shared per-container visible-instance capacity plus slot-order packing
- URP-pass occlusion does not fix the near-tree overflow bug where shells can survive while trunks and branch wood disappear
- current-frame or history-based occlusion stays a later optimization only after prioritization and telemetry prove the remaining bottleneck

---

## critical flaws

- Current overflow behavior is decided by draw-slot packing order, not by visual importance. `BuildSlotStarts` walks slot indices, then later slots lose capacity first.
- Every visible branch is classified independently before any budget acceptance. That means detail demand is generated globally, but there is no global accept/reject policy.
- The runtime has no guaranteed minimum representation per visible tree. A tree can be visible, classified correctly, and still disappear because the shared packed-instance buffer filled with other content first.
- The current packed-instance budget is a raw instance count only. It does not distinguish mandatory tree-presence cost from optional detail cost.
- Dense forests are operationally unsafe. A few expensive expanded trees can consume enough instances to starve large parts of the forest.
- The current code exposes two different limits and the redesign draft blurred them:
  - `maxVisibleInstanceCapacity` is a per-container packed-instance limit
  - draw slots are the registry/render-submission surface and are not fixed by changing that packed-instance cap
- Splitting into multiple containers is only a workaround. It does not fix the underlying overflow policy.

## missing pieces

- A guaranteed tree-presence contract under overflow.
- An explicit emergency fallback allowed inside the normal near-distance bands.
- Priority buckets that decide acceptance before per-slot packing.
- A promotion/demotion rule so trees upgrade nearest-first and fall back safely when budgets are exhausted.
- A clear distinction between:
  - packed visible-instance pressure
  - draw-slot submission count
- Validation rules for dense-forest scenarios. Today there is no testable contract saying what must still render when detail overflows.
- Telemetry. This is required, but it is a last-priority task after the acceptance logic is correct.

## terminology

| Term | Status | Where Used | Purpose |
| --- | --- | --- | --- |
| `registeredAuthorings` | Shipped | `VegetationRuntimeContainer.registeredAuthorings` | Explicit container ownership list for live `VegetationTreeAuthoring` inputs. |
| `VegetationTreeAuthoringRuntime` | Shipped | `VegetationRuntimeContainer.BuildRuntimeTreeAuthorings()` -> `AuthoringContainerRuntime` | Runtime-safe tree snapshot so registration does not depend on live `MonoBehaviour` traversal after the handoff. |
| `VegetationRuntimeRegistry` | Shipped | Built by `VegetationRuntimeRegistryBuilder`, consumed by `VegetationGpuDecisionPipeline` and `VegetationIndirectRenderer` | Frozen flattened runtime snapshot for one container. |
| `Visible instance` | Shipped | `residentInstanceBuffer`, bounded by `maxVisibleInstanceCapacity` | One packed runtime instance record that is already draw-ready. |
| `Draw slot` | Shipped | `VegetationRuntimeRegistry.DrawSlots`, `BuildSlotStarts`, `VegetationIndirectRenderer` | One exact runtime bucket defined by `Mesh + Material + MaterialKind`. One draw slot owns one slot index, one indirect-args record, one packed-range start, and one potential indirect submission per pass. |
| `Indirect submission` | Shipped | `VegetationIndirectRenderer.Render()` | One final `DrawMeshInstancedIndirect` call for one active draw slot in one pass. This is downstream from visible-instance acceptance. |
| `Tree branch span` | Shipped | `VegetationTreeInstanceRuntime.SceneBranchStartIndex + SceneBranchCount`, tree/branch kernels | One tree points to its contiguous branch slice inside the flat `SceneBranches[]` array. This is not a full-tree hierarchy. |
| `Scene branch record` | Shipped | `VegetationRuntimeRegistry.SceneBranches[]`, branch classify/count/emit | One bounded static registration record for one scene branch placement. It is not a final submission owner. |
| `Branch-shell BFS metadata` | Shipped limitation | `VegetationBranchShellNodeRuntimeBfs`, `ShellNodesL1/L2/L3`, compute upload | `FirstChildIndex + ChildMask` child-link metadata exists per shell tier, but the shipped shader still does not use it for real frontier traversal or subtree skip. This is branch-shell BFS, not tree BFS. |
| `PresenceProxyOnly` | Planned redesign | This document, Stage `C.5` | Tree is accepted only as one whole-tree proxy for this frame and must skip branch kernels entirely. |
| `PromotedExpanded` | Planned redesign | This document, Stages `C.5`, `C.6`, `D` | Tree is accepted for expensive branch-level work this frame. |
| `Promoted-tree compaction` | Planned redesign | This document, Stage `C.5` | Build a dense promoted-tree worklist before branch kernels so non-promoted trees never pay branch traversal cost. |

## current shipped lifecycle

### registration / flattening path

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
   -> TreeInstances[]             one per active tree
   -> SceneBranches[]             bounded static registration snapshot for all active tree branches in flat branch array
   -> BranchPrototypes[]          reusable per-prototype decode data
   -> ShellNodesL1/L2/L3[]        reusable prototype-local branch-shell BFS metadata
   -> SpatialGrid                 tree-cell ownership for visibility classification
```

### per-camera / per-frame submission path

```text
Camera
-> VegetationRendererFeature depth/color pass setup
-> VegetationActiveAuthoringContainerRuntimes.GetActive()
-> AuthoringContainerRuntime.PrepareFrameForCamera()
-> VegetationGpuDecisionPipeline.PrepareResidentFrame()
   -> ClassifyCells
   -> ClassifyTrees
   -> ClassifyBranches      current shipped limitation: full flat SceneBranches[] dispatch
   -> ResetSlotCounts
   -> CountTrees
   -> CountBranches         current shipped limitation: full flat SceneBranches[] dispatch
   -> BuildSlotStarts
   -> EmitTrees
   -> EmitBranches          current shipped limitation: full flat SceneBranches[] dispatch
   -> FinalizeIndirectArgs
-> residentInstanceBuffer            packed visible instances
-> residentArgsBuffer                indirect args per draw slot
-> slotPackedStartsBuffer            packed range start per draw slot
-> VegetationIndirectRenderer.BindGpuResidentFrame()
-> for each registered draw slot:
   bind shared buffers
   keep slot active
-> URP Depth Pass: DrawMeshInstancedIndirect per active draw slot
-> URP Color Pass: DrawMeshInstancedIndirect per active draw slot
-> final URP indirect submissions
```

### current ownership split

Static registry owners:

- `SpatialGrid`
  Owns tree-to-cell registration and visible-cell query bounds only.
- `TreeInstances[]`
  Owns per-tree static scene registration and the handle into the flat branch array.
- `SceneBranches[]`
  Owns one bounded static scene-branch registration record per placed branch in the container snapshot. It grows linearly during `RefreshRuntimeRegistration()`. It is not a final submission owner.
- `BranchPrototypes[] + ShellNodesL1/L2/L3[]`
  Own reusable branch module decode data and reusable branch-shell BFS metadata.
- `DrawSlots[]`
  Own final submission identities only.

Per-frame worklists:

- `cellVisibilityBuffer`, `treeModesBuffer`, `branchDecisionBuffer`
  Current frame classification outputs derived from the static registry.
- `residentInstanceBuffer`, `residentArgsBuffer`, `slotPackedStartsBuffer`
  Current frame accepted-content and submission outputs.
- future `PromotedTreeIndices` and `PromotedBranchWorkItems`
  Compact worklists that should replace full-scene branch dispatch once tree prioritization lands.

### redesign insertion point

```text
Current shipped:
ClassifyTrees
-> ClassifyBranches on the full flat SceneBranches[] array
-> Count/Emit

Required redesign:
ClassifyTrees
-> TreeVisibilityRecord[] build
-> PresenceProxyOnly acceptance for every visible tree
-> nearest-tree promotion to PromotedExpanded
-> promoted-tree compaction
-> branch classify/count/emit for promoted trees only
-> later targeted branch-shell BFS for promoted shell branches only
-> final count/pack/emit of accepted content
```

Operational consequence:

- today the runtime can cheaply reject whole trees at the tree sphere stage, but once a tree stays expanded it still pulls branch work from a flat branch span
- the urgent redesign must intercept between tree classification and branch work
- targeted branch-shell BFS is only worth adding after that interception exists

## best alternatives

- Tree-presence proxy first, then nearest-tree promotion.
  Strong baseline. This is the urgent fix because it directly addresses the current failure mode.

- Branch priority rings inside promoted trees.
  Good second stage. Exact sort is unnecessary; distance rings are enough for stable near-first behavior.

- Active-slot filtering.
  Useful only if telemetry later proves draw submission count is still a real bottleneck after prioritization. It is separate from the visible-instance overflow bug.

- Container splitting only.
  Operational workaround only. It reduces one-container pressure but does not define correct overflow behavior.

- Current-frame occlusion / HiZ.
  Deferred. It may reduce wasted work later, but it does not fix the current near-tree capacity bug and adds new failure modes too early.

## recommended combined design

### 1. Non-negotiable runtime invariants

- Every visible tree must emit at least one whole-tree representation every frame.
- Overflow must never be resolved by draw-slot order.
- Nearest trees must be promoted before farther trees.
- Branch detail inside promoted trees must also be admitted nearest-first.
- Overflow must degrade representation quality, not erase arbitrary visible vegetation.
- Urgent redesign success is defined by accepted-content correctness first. Occlusion and submission-count optimization are explicitly deferred.

### 2. Representation ladder

- `PresenceProxy`
  One whole-tree instance using `impostorMesh`. This is allowed for any visible tree under budget pressure, even if the tree is inside the normal `Expanded` distance band.

- `PromotedExpanded`
  Tree is upgraded from `PresenceProxy` to expanded runtime submission. Proxy is suppressed for that tree once promotion succeeds.

- `PromotedExpanded + BranchDetail`
  Promoted tree receives branch detail buckets in priority order.

Critical rule:
- tree presence is mandatory
- expanded detail is optional

### 3. New budget model

Replace the current single undifferentiated cap with an explicit budget profile.

Recommended profile:

```text
VegetationRuntimeBudgetProfile
- int totalVisibleInstanceCapacity
- int reservedTreePresenceCapacity
- int reservedTrunkCapacity
- int reservedL0Capacity
- int reservedL1Capacity
- int reservedL2Capacity
- int reservedL3Capacity
- int safetyMargin
```

Minimum hard rule:
- `reservedTreePresenceCapacity` must be large enough for the worst-case number of visible trees for the container chunk

For the urgent dense-forest target:
- if one container may need to show `6000` tightly packed trees, that container must reserve at least `6000` tree-presence instances

If a container configuration cannot satisfy its own guaranteed tree-presence reserve:
- validation must fail explicitly

### 4. New runtime records

Add explicit per-frame acceptance records before per-slot packing.

Recommended records:

```text
TreeVisibilityRecord
- int treeIndex
- float treeDistance
- int priorityRing
- bool visible
- bool wantsExpanded
- bool acceptedProxy
- bool acceptedExpanded
- int desiredExpandedInstanceCost
- int acceptedDetailInstanceCost

BranchPriorityRecord
- int treeIndex
- int branchIndex
- float branchDistance
- int priorityRing
- int desiredTier
- int acceptedTier
- int estimatedInstanceCost
- float coverageWeight
```

These records exist to preserve priority information until after budget acceptance. The current slot-first counters destroy that information too early.

### 5. New frame pipeline

#### Stage A: visibility and desired LOD

- classify visible cells
- classify visible trees
- classify desired branch tiers exactly as today

#### Stage B: mandatory tree-presence acceptance

- build one `TreeVisibilityRecord` for every visible tree
- accept one `PresenceProxy` for every visible tree first
- this stage consumes only the tree-presence reserve

Result:
- every visible tree is guaranteed to render

#### Stage C: nearest-tree promotion

- compute exact or tightly bounded expanded cost per visible tree
- walk tree priority rings nearest to farthest
- promote trees from `PresenceProxy` to `PromotedExpanded` while promotion budget remains
- promotion cost is `expandedCost - 1`, because the tree already owns one proxy instance

If promotion cannot fit:
- tree stays `PresenceProxy`

This is the first urgent implementation milestone. It alone prevents the catastrophic failure mode where many visible trees disappear.

#### Stage C.5: tree work states and promoted-tree compaction

After the first tree-level prioritization pass, stop treating every `Expanded` tree as branch work immediately.

Required target states:

- `PresenceProxyOnly`
  Tree is accepted only as a proxy for this frame. It must not enter branch kernels.

- `PromotedExpanded`
  Tree is accepted for full branch-level work this frame.

Required compaction:

- build a compact promoted-tree list after nearest-tree promotion
- build promoted-branch work only from that compact tree list
- branch classify/count/emit must stop dispatching against the full scene branch array once this redesign lands

This is the simple pre-pass needed to trim decisions as much as possible:
- far or budget-constrained trees stay `PresenceProxyOnly`
- only promoted near trees pay per-branch cost
- shell traversal is skipped entirely for non-promoted trees

#### Stage C.6: targeted branch-shell BFS for promoted shell branches only

After promoted-tree compaction is in place, finally use the existing branch-shell BFS metadata in a targeted way.

Scope:

- apply only to `PromotedExpanded` trees
- apply only to shell tiers `L1/L2/L3`
- do not run this for `PresenceProxyOnly` trees
- do not run this for `L0` branches, because `L0` already emits source branch meshes directly

Required behavior:

- build promoted-branch work only from the compact promoted-tree list
- for each promoted shell branch, start from the root node of the selected shell tier
- use `FirstChildIndex + ChildMask` to visit only the reachable frontier instead of linearly scanning the whole selected shell tier
- if a node is outside the frustum, reject the whole subtree
- if a node is visible and still has children, expand children
- if a node is visible and is a leaf, emit that node

Why this stage is worth adding:

- current shipped branch-shell BFS data is just uploaded metadata; it is not paying for itself yet
- after tree-level prioritization, the remaining branch set is small enough that targeted hierarchy traversal becomes worth the implementation cost
- this reduces shell-node work for promoted trees without pretending it solves the bigger flat-tree branch-span problem

Non-goals:

- this is not a full-tree hierarchy
- this does not replace tree-level prioritization
- this does not replace branch compaction
- this does not add occlusion

Hard rule:

- tree-level prioritization and promoted-tree compaction must land before targeted branch-shell BFS work starts
- otherwise the runtime will still waste work touching too many expanded trees and the branch-shell BFS stage will be solving the wrong problem

#### Stage D: branch detail buckets inside promoted trees

- only promoted trees can request branch detail
- build branch records for promoted trees only
- accept branches by priority buckets and distance rings, nearest first

Recommended bucket order:

```text
1. mandatory promoted-tree trunk
2. L0 branches
3. L1 branches
4. L2 branches
5. L3 branches
```

Within each bucket:
- process nearest branch rings first
- stop when that bucket budget is exhausted

#### Stage E: promotion safety check

If a promoted tree cannot reach minimum stable completeness:
- demote it back to `PresenceProxy`

The minimum stable completeness for the first production-safe version should be conservative:
- full trunk
- enough branch coverage to avoid an obviously broken near-tree silhouette

Do not allow half-upgraded trees with missing trunk or broken canopy coverage to replace the proxy.

#### Stage F: count, pack, emit only accepted content

- accepted trees and accepted branches contribute to per-slot counters
- only after acceptance do we build per-slot starts
- only accepted content is emitted into the packed visible-instance buffer

This is the key redesign point:
- slot packing becomes a late packing detail
- it is no longer the mechanism that decides what survives
- promoted-tree compaction must happen before branch kernels so the runtime stops touching every branch of every expanded tree

### 6. Why this solves the 6000-tree dense-forest case

Current runtime cannot guarantee this case because every detailed branch competes in one monolithic pool.

The redesigned runtime can guarantee it because:

- the first reserved budget is tree presence, not branch detail
- `6000` visible trees cost `6000` proxy instances, which is trivial compared with detail-scale capacities
- nearest trees are promoted only after presence is already guaranteed
- worst case under extreme pressure:
  all visible trees still render as whole-tree proxies
- best case under available detail budget:
  nearest trees are promoted to expanded detail first

This is the only sane guarantee that can be made without mixing in more systems before the acceptance path is correct.

### 7. Required contract changes

- `impostorMesh` must be allowed as an emergency runtime fallback even inside the nominal expanded distance bands
- runtime budget must stop being treated as one undifferentiated packed-instance cap
- container validation must know the target guaranteed visible-tree count for that chunk
- debug and profiling output must report accepted versus dropped content by bucket
- urgent redesign work must not be blocked on occlusion work
- docs and code comments must use the same hard terminology for `visible instance`, `draw slot`, and final `indirect submission`

### 8. Telemetry and validation

Required diagnostics:

- visible tree count
- accepted proxy tree count
- promoted tree count
- demoted tree count
- requested expanded-tree promotions
- rejected promotions
- requested versus accepted branch counts for `L0/L1/L2/L3`
- total requested instances
- total accepted instances
- non-zero accepted slot count
- promoted tree count
- promoted branch count
- total scene branch count versus promoted branch count
- bucket usage and headroom

Required validation scenario:

- one container with `6000`-`12000` tightly packed trees
- camera placed inside the forest
- runtime proves:
  - every visible tree renders at least one representation
  - nearest trees receive expanded detail first
  - overflow never drops arbitrary trees due to slot order

Telemetry priority:
- add telemetry only after the acceptance logic above is implemented and stable

### 9. Migration plan

#### Phase 1 - urgent safety fix

- add `PresenceProxy` acceptance for every visible tree
- add nearest-tree promotion with explicit budgets
- add `PresenceProxyOnly` / `PromotedExpanded` tree work states
- compact promoted trees before branch kernels
- remove slot-order-based survival as the de facto overflow policy

#### Phase 2 - fuller detail quality

- replace shell-tier flat scans with targeted branch-shell BFS frontier traversal for promoted shell branches only
- add branch priority rings for promoted trees
- add demotion logic when promoted-tree completeness is unstable

#### Phase 3 - last-priority observability

- add overflow telemetry
- add non-zero-slot telemetry so we can distinguish instance pressure from draw-slot submission pressure

#### Phase 4 - later follow-up

- optional active-slot filtering if submission count is still too high
- optional current-frame occlusion / HiZ after prioritization is verified
- optional cross-container/global coordinator
- optional DFS hierarchy migration and more accurate subtree cost estimation

### 10. Done criteria

- a visible tree never disappears solely because other slots consumed the packed-instance buffer first
- a `6000`-tree dense-forest container can still render every visible tree at least as `PresenceProxy`
- nearest visible trees are promoted before farther visible trees
- branch detail inside promoted trees follows priority buckets instead of slot order
- overflow diagnostics are explicit and testable once telemetry lands

## open questions

1. Should emergency `PresenceProxy` always reuse `impostorMesh`, or do we need a dedicated near-safe whole-tree proxy mesh to avoid quality collapse in the closest few meters?
2. Should the dense-forest guarantee be configured as `maxGuaranteedVisibleTrees` per container, or should it default to `registry.TreeInstances.Count` when the user explicitly marks a container as a dense chunk?
3. Should branch-level promotion use exact measured cost per tree every frame, or a cheaper estimated cost with corrective demotion when the measured accepted content falls short?
4. Should active-slot filtering become part of this redesign only if telemetry later proves the submission count is still too high after prioritization?

---

END
