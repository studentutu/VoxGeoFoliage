# Vegetation Runtime Architecture

Purpose: one current runtime authority. ASCII first. No design fan-fiction.

## Keep This Doc Set

- `Packages/com.voxgeofol.vegetation/README.md`
  consumer setup and package contract
- `DetailedDocs/VegetationRuntimeArchitecture.md`
  runtime ownership, budgets, and migration target
- `DetailedDocs/Milestone1.md`
  shipped baseline summary only
- `DetailedDocs/Milestone2.md`
  current open work only

Everything else in `DetailedDocs/` is archive or redirect only.

## Current Runtime Problems

- Camera and shadow preparation still mutate one shared submission surface.
- Shadow support duplicates one full GPU pipeline per container instead of reusing pooled per-view scratch.
- `maxVisibleInstanceCapacity` is incorrectly used as:
  - visible-instance buffer size
  - expanded-branch work-item capacity
  - accepted-tier work budget
- `TreeL3` floor is not a hard invariant in shipped code.
- Branch count/emit still dispatches against configured capacity, not actual generated work.
- Submission still iterates registered draw slots. Non-zero emitted slots are telemetry, not a real compact submission list.

## Runtime Pipeline

```text
Authoring Assets
  -> VegetationRuntimeContainer
  -> BuildRuntimeTreeAuthorings()
  -> VegetationRuntimeRegistryBuilder
  -> VegetationContainerStaticState
     - cells
     - lod profiles
     - blueprints
     - blueprint placements
     - branch prototypes
     - trees
     - slot metadata

Per View Request
  -> PrepareView(camera | shadow cascade)
  -> VegetationPreparedView
     - cell visibility
     - tree visibility
     - priority ordering
     - expanded branch work items
     - slot counts
     - slot packed starts
     - visible instances
     - indirect args

GPU View Work
  -> ClassifyCells
  -> ClassifyTrees
  -> AcceptBaseline(TreeL3 for visible non-far color trees)
  -> PromoteNearest(TreeL3 -> L2 -> L1 -> L0)
  -> BuildExpandedBranchWorkItems(promoted trees only)
  -> CountTrees
  -> CountExpandedBranches(actual work count only)
  -> BuildSlotStarts
  -> EmitTrees
  -> EmitExpandedBranches
  -> FinalizeIndirectArgs

Submission
  -> Render(camera handle or shadow handle)
  -> bind view-scoped global buffers
  -> iterate bounded registered draw slots
  -> DrawMeshInstancedIndirect per slot
```

## Ownership Rules

`VegetationContainerStaticState`
- one per container
- owns persistent CPU + GPU registration state
- rebuilt only on `RefreshRuntimeRegistration()`

`VegetationPreparedView`
- pooled per-view scratch
- owns transient buffers for one camera or one shadow cascade
- not shared mutably between camera and shadow

`VegetationPreparedViewHandle`
- explicit submission token
- returned by prepare
- passed into render
- replaces "whatever was bound last"

`VegetationIndirectRenderer`
- immutable slot metadata only
- no persistent "current frame" ownership
- no slot-shared mutable `MaterialPropertyBlock`

## Budget Rules

Do not alias one integer across unrelated units.

Required runtime budget split:

```text
VegetationRuntimeBudget
- ColorMaxVisibleInstances
- ColorMaxExpandedBranchWorkItems
- ColorMaxApproxWorkUnits
- ShadowMaxVisibleInstances
- ShadowMaxExpandedBranchWorkItems
- ShadowMaxApproxWorkUnits
- MaxRegisteredDrawSlots
```

Meaning:
- visible instances = memory cap
- expanded branch work items = queue cap
- approx work units = quality/perf cap
- registered draw slots = submission surface cap

## Hard Invariants

- Visible non-far color trees must never disappear silently. Minimum accepted color representation is `TreeL3`.
- `Impostor` stays far-only.
- Promotion is nearest-first.
- Branch work exists only for trees already accepted above `TreeL3`.
- Slot order must not decide survival.
- Shadow is allowed to be cheaper than color.

## Default Shadow Policy

```text
Shadow default:
  far      -> Impostor
  non-far  -> TreeL3
  expanded branch promotion -> off

Optional first upgrade:
  allow L2 in shadows
  do not allow L1/L0 by default
```

Reason:
- shadow is secondary
- current runtime already pays too much for it

## Current Backend Truth

- Backend is still exact-slot indirect submission.
- `BatchRendererGroup` is out.
- Real GPU-only active-slot submission compaction is not the current production path.
- Zero-instance indirect draws are acceptable for now if registered slot count is hard-bounded.

## Validation Rules

- Fail registration if required runtime meshes are missing:
  - `treeL3Mesh`
  - branch tier meshes
  - `impostorMesh`
- Fail the container if the color baseline cannot fit.
- Fail or split containers when registered draw-slot count exceeds the allowed cap.

## Migration Order

1. Split persistent container state from per-view prepared state.
2. Introduce explicit prepared-view handles.
3. Remove renderer-global bound-frame ownership.
4. Split budgets into instances, work items, work units, and slot cap.
5. Dispatch branch count/emit from actual generated work count.
6. Keep shadow cheap by default.

## What Is Not Solved Yet

- cross-container prioritization
- wind
- custom-material contract
- masked-quad `GPUVoxelizer` bake path
- better submission backend than static registered-slot iteration
