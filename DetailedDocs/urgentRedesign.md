# Urgent Redesign - Priority-Bucket Runtime Submission

## Purpose

This document defines the urgent redesign required for runtime vegetation submission under dense-forest budget pressure.

Target failure case:
- camera stands inside a very dense forest
- one container may own around `6000` tightly packed trees
- current runtime budget must never cause whole visible trees to disappear just because detailed branch and shell output consumed the packed-instance buffer first

Explicit scope:
- add primitive priority by distance to camera
- skip HiZ / hard occlusion for this redesign
- guarantee stable rendering under overflow by degrading detail, not by dropping arbitrary trees

---

## critical flaws

- Current overflow behavior is decided by draw-slot packing order, not by visual importance. `BuildSlotStarts` walks slot indices, then later slots lose capacity first.
- Every visible branch is classified independently before any budget acceptance. That means detail demand is generated globally, but there is no global accept/reject policy.
- The runtime has no guaranteed minimum representation per visible tree. A tree can be visible, classified correctly, and still disappear because the shared packed-instance buffer filled with other content first.
- The current packed-instance budget is a raw instance count only. It does not distinguish tree-presence cost from optional detail cost.
- Dense forests are operationally unsafe. A few expensive expanded trees can consume enough instances to starve large parts of the forest.
- Splitting into multiple containers is only a workaround. It does not fix the underlying overflow policy.

## missing pieces

- A guaranteed tree-presence contract under overflow.
- An explicit emergency fallback allowed inside the normal near-distance bands.
- Priority buckets that decide acceptance before per-slot packing.
- A promotion/demotion rule so trees upgrade nearest-first and fall back safely when budgets are exhausted.
- Budget telemetry: requested versus accepted trees, branches, shell leaves, and per-bucket usage.
- Validation rules for dense-forest scenarios. Today there is no testable contract saying what must still render when detail overflows.

## best alternatives

- Full primitive sort by exact distance.
  Correct but expensive. It is too much churn for the urgent fix.

- Tree-presence proxy first, then nearest-tree promotion.
  Strong baseline. This is the only simple way to guarantee that all visible trees still render something.

- Branch priority rings inside promoted trees.
  Good second stage. Exact sort is unnecessary; distance rings are enough for stable near-first behavior.

- Container splitting only.
  Operational workaround only. It reduces one-container pressure but does not define correct overflow behavior.

- HiZ / depth-pyramid occlusion.
  Useful later, explicitly out of scope here. It is not required to solve the immediate dense-forest failure.

## recommended combined design

### 1. Non-negotiable runtime invariants

- Every visible tree must emit at least one whole-tree representation every frame.
- Overflow must never be resolved by draw-slot order.
- Nearest trees must be promoted before farther trees.
- Branch detail inside promoted trees must also be admitted nearest-first.
- Overflow must degrade representation quality, not erase arbitrary visible vegetation.

### 2. Representation ladder

- `PresenceProxy`
  One whole-tree instance using `impostorMesh`. This is allowed for any visible tree under budget pressure, even if the tree is inside the normal `Expanded` distance band.

- `ExpandedPromoted`
  Tree is upgraded from `PresenceProxy` to expanded runtime submission. Proxy is suppressed for that tree once promotion succeeds.

- `ExpandedPromoted + BranchDetail`
  Promoted tree receives branch detail buckets in priority order.

The critical rule is simple:
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
- `reservedTreePresenceCapacity` must be large enough for the worst-case number of visible trees for the container chunk.

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
- promote trees from `PresenceProxy` to `ExpandedPromoted` while promotion budget remains
- promotion cost is `expandedCost - 1`, because the tree already owns one proxy instance

If promotion cannot fit:
- tree stays `PresenceProxy`

This is the first urgent implementation milestone. It alone prevents the catastrophic failure mode where many visible trees disappear.

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

### 6. Why this solves the 6000-tree dense-forest case

Current runtime cannot guarantee this case because every detailed branch competes in one monolithic pool.

The redesigned runtime can guarantee it because:

- the first reserved budget is tree presence, not branch detail
- `6000` visible trees cost `6000` proxy instances, which is trivial compared with the current detail-scale capacities
- nearest trees are promoted only after presence is already guaranteed
- worst case under extreme pressure:
  all visible trees still render as whole-tree proxies
- best case under available detail budget:
  nearest trees are promoted to expanded detail first

This is the only sane guarantee that can be made without adding real occlusion.

### 7. Required contract changes

- `impostorMesh` must be allowed as an emergency runtime fallback even inside the nominal expanded distance bands
- runtime budget must stop being treated as one undifferentiated packed-instance cap
- container validation must know the target guaranteed visible-tree count for that chunk
- debug and profiling output must report accepted versus dropped content by bucket

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
- bucket usage and headroom

Required validation scenario:

- one container with `6000` tightly packed trees
- camera placed inside the forest
- runtime proves:
  - every visible tree renders at least one representation
  - nearest trees receive expanded detail first
  - overflow never drops arbitrary trees due to slot order

### 9. Migration plan

#### Phase 1 - urgent safety fix

- add `PresenceProxy` acceptance for every visible tree
- add nearest-tree promotion with explicit budgets
- remove slot-order-based survival as the de facto overflow policy

#### Phase 2 - fuller detail quality

- add branch priority rings for promoted trees
- add trunk and branch bucket telemetry
- add demotion logic when promoted-tree completeness is unstable

#### Phase 3 - later follow-up

- optional cross-container/global coordinator
- optional HiZ/depth-pyramid occlusion
- optional DFS hierarchy migration and more accurate subtree cost estimation

### 10. Done criteria

- a visible tree never disappears solely because other slots consumed the packed-instance buffer first
- a `6000`-tree dense-forest container can still render every visible tree at least as `PresenceProxy`
- nearest visible trees are promoted before farther visible trees
- branch detail inside promoted trees follows priority buckets instead of slot order
- overflow diagnostics are explicit and testable

## open questions

1. Should emergency `PresenceProxy` always reuse `impostorMesh`, or do we need a dedicated near-safe whole-tree proxy mesh to avoid quality collapse in the closest few meters?
2. Should the dense-forest guarantee be configured as `maxGuaranteedVisibleTrees` per container, or should it default to `registry.TreeInstances.Count` when the user explicitly marks a container as a dense chunk?
3. Should branch-level promotion use exact measured cost per tree every frame, or a cheaper estimated cost with corrective demotion when the measured accepted content falls short?
4. Should the long-term budget owner remain per-container, or should this redesign eventually move the accepted-content coordinator up to `VegetationRendererFeature` for cross-container priority?

---

END
