# Vegetation RenderGraph GPU-Driven Cutout

Purpose: define the next runtime architecture for removing C# preparation-job synchronization and thread switching from the vegetation RenderGraph path.

Status: target design. The current runtime still uses a scheduled C# preparation job plus CPU-side buffer upload. That shape is an intermediate slice only; it cannot be the production performance path.

## Problem

The current graph path records URP RenderGraph passes, but the expensive decision work is still outside the graph:

```text
RecordRenderGraph
-> schedule C# preparation job
-> wait/reuse completed job slot
-> RenderGraph compute pass uploads NativeArray data into GPU buffers
-> raster pass draws grouped indirect
```

This removes the worst `JobHandle.Complete()` inside graph execution, but it still has a fundamental sync boundary:

1. cull/select/budget/compact/args generation lives in a Unity job, not in GPU commands
2. graph execution depends on CPU-visible completed slots
3. instance/args data is staged in `NativeArray` and uploaded through command-buffer `SetBufferData`
4. active groups are CPU-visible state, which prevents a fully GPU-owned frame
5. diagnostics counters are CPU-owned unless explicitly read back later

The proper production path is not more job scheduling. The proper path is a GPU preparation chain declared as RenderGraph compute passes, with raster passes depending on the produced GPU buffers.

## URP RenderGraph Contract

Use URP RenderGraph as a dependency graph, not as a callback wrapper.

Required shape:

```text
RecordRenderGraph
-> import persistent compiled-data GPU buffers
-> create/import per-frame output buffers
-> AddComputePass: clear counters and args
-> AddComputePass: page/cell visibility
-> AddComputePass: packet admission and budget decisions
-> AddComputePass: prefix/compact visible instances
-> AddComputePass: write indirect args
-> AddRasterRenderPass: draw grouped indirect using the produced buffers
```

Rules:

1. `RecordRenderGraph()` declares resources and pass dependencies only.
2. No `JobHandle`, no `NativeArray` output staging, no blocking completion, no synchronous readback in the production path.
3. Compute passes declare every buffer with `UseBuffer(..., AccessFlags.Read/Write/ReadWrite)`.
4. Raster passes declare instance, args, and contract buffers as read dependencies.
5. Color/depth raster passes do not use `AllowGlobalStateModification`.
6. Shadow atlas injection may keep the current `AllowGlobalStateModification(true)` exception only for cascade view/projection/bias compatibility.
7. Inactive groups use zero instance count in indirect args. Do not require a CPU active-group list to skip them.

## GPU Buffer Contract

Persistent provider buffers, rebuilt only when compiled providers change:

1. `GpuPageBuffer`
   bounds center/extents, first cell, cell count, page HLOD packet range
2. `GpuCellBuffer`
   bounds center/extents, page index, HLOD range, near-detail tier ranges, near-detail byte cost
3. `GpuPacketBuffer`
   bounds, group index, first static instance, instance count, work cost, residency, representation kind, shadow mode, shadow packet index
4. `GpuPacketLookupBuffer`
   packed packet-range lookup indices
5. `GpuStaticInstanceBuffer`
   immutable compiled instance payload copied from page assets
6. `GpuGroupMetadataBuffer`
   index count, index start, base vertex, material/group draw metadata
7. `GpuNearDetailStateBuffer`
   resident/requested/last-used state by cell

Per-frame output buffers:

1. `GpuVisiblePageMask`
2. `GpuVisibleCellCandidateBuffer`
3. `GpuSelectedPacketBuffer`
4. `GpuGroupCounters`
5. `GpuGroupStartOffsets`
6. `GpuVisibleInstanceBuffer`
7. `GpuIndirectArgsBuffer`
8. `GpuFrameCounters`

The raster path binds `GpuVisibleInstanceBuffer` and `GpuIndirectArgsBuffer`. The shader continues to index instances through `_VegetationInstanceDataBaseOffset`; the base offset comes from the per-group start offset written into draw state or from grouped args layout.

## Compute Pass Chain

### 1. Clear

Clears per-frame counters, visible masks, group counters, indirect args instance counts, and frame counters.

This pass also writes static indirect args fields per group entry:

```text
indexCount
instanceCount = 0
indexStart
baseVertex
startInstance = 0
```

### 2. Page/Cell Visibility

One thread per page or cell.

Inputs:

1. frustum planes for camera or shadow cascades
2. page/cell bounds

Outputs:

1. visible page mask
2. visible cell candidate entries
3. per-candidate frustum mask

For shadows, one preparation frame covers all main-light cascades. The frustum mask is a bitmask over cascades.

### 3. Admission And Budget

This is the critical design point.

Exact nearest-cell global ordering is expensive on GPU if implemented as a full sort every frame. The production version should use deterministic bucketed admission:

1. compute a distance/screen-error bucket per visible cell
2. process buckets from near to far
3. atomically charge global work and instance budgets
4. select the best allowed tier, then degrade to cheaper near tiers, then HLOD

This preserves the user-facing rule: visible vegetation degrades instead of disappearing. It avoids per-frame CPU sorting and avoids O(N^2) rescans.

Near-detail residency can stay GPU-owned:

1. resident bits are persistent GPU state
2. upload budget is a frame counter
3. cells that fail residency fall back to HLOD
4. CPU-side external streaming may later consume an async readback of request counters, but rendering must not wait for it

### 4. Count And Prefix

Selected packets atomically add their instance counts into `(frustum, group)` counters.

Then prefix-sum counters to produce group start offsets. Use Unity SRP Core GPU prefix-sum utilities if they are usable from this package; otherwise add a small local scan compute path for group-entry counts. Group-entry count is small enough for a simple first implementation.

### 5. Compact

One thread range per selected packet copies immutable static instances into `GpuVisibleInstanceBuffer` using group start offsets plus atomic write offsets.

For shadows, copy to entry:

```text
entryIndex = cascadeIndex * groupCount + groupIndex
```

### 6. Write Args

Writes final instance counts into `GpuIndirectArgsBuffer`.

Raster submits all group entries. Zero-count args make inactive entries cheap without CPU-side culling or readback.

## Raster Contract

Color/depth:

```text
AddRasterRenderPass
-> SetRenderAttachment / SetRenderAttachmentDepth
-> UseBuffer(visibleInstanceBuffer, Read)
-> UseBuffer(indirectArgsBuffer, Read)
-> draw all asset groups with DrawMeshInstancedIndirect
```

Shadow:

```text
AddRasterRenderPass
-> SetRenderAttachmentDepth(mainShadowsTexture, ReadWrite)
-> UseBuffer(visibleInstanceBuffer, Read)
-> UseBuffer(indirectArgsBuffer, Read)
-> for each cascade: bind cascade matrices/bias, draw all asset groups for that cascade entry
```

The shadow pass remains the only allowed global-state exception until URP exposes a cleaner main-light shadow atlas append contract.

## Diagnostics

Do not put diagnostics readback in the hot path.

Required diagnostics mode:

1. GPU frame counters are written every frame.
2. CPU diagnostics use async readback only when `EnableDiagnostics` is true.
3. Diagnostics may be one or more frames late.
4. Rendering never waits for diagnostic readback.

Counters:

1. visible page count
2. visible cell count
3. selected packet count
4. selected instance count
5. selected near-detail packet count
6. selected HLOD packet count
7. selected shadow packet count
8. active group-entry count
9. budget-denied near-detail cells
10. HLOD fallback count

## Migration Slices

### Slice 1: GPU Args Ownership

Keep CPU/job packet selection temporarily, but stop CPU args generation and active-group state. Upload selected packet records, then have a RenderGraph compute pass clear, compact, and write args.

Success criteria:

1. no CPU active-group list required by raster
2. raster submits all group entries with zero-count args for inactive entries
3. no `SetBufferData` for indirect args in the RenderGraph compute pass

### Slice 2: GPU Visibility

Move page/cell frustum tests into compute. CPU records only camera position and frustum planes.

Success criteria:

1. no CPU page/cell broad phase
2. no C# job for visibility
3. frame renders through RenderGraph compute/raster dependencies only

### Slice 3: GPU Admission/Budget

Move tier selection, budget charging, selected-packet list generation, and HLOD fallback into compute.

Success criteria:

1. no `PrepareFrameJob`
2. no preparation `NativeArray` output slots
3. no CPU-visible selected packet list
4. no render-frame skip/reuse caused by incomplete jobs

### Slice 4: GPU Residency State

Move near-detail resident/request/last-used state to persistent GPU buffers. CPU streaming integration, if needed later, consumes async readback requests without blocking rendering.

Success criteria:

1. near-detail budgets still cap detail work
2. distant vegetation still renders as HLOD
3. wind and shadows remain active at cull distance

## Non-Negotiable Cutout

Delete these concepts from the production path after Slice 3:

1. `PrepareFrameJob`
2. preparation slot ring used for completed C# jobs
3. `CompleteRenderGraphPreparation` CPU upload path
4. `NativeArray` frame output buffers
5. CPU `activeGroupIndices`
6. CPU render-time selected packet counters except async diagnostics

Keep CPU provider graph building. It is not per-frame rendering work and remains the correct place to translate compiled ScriptableObject assets into persistent GPU buffers.

## Validation

Use Render Graph Viewer and Frame Debugger to verify:

1. vegetation compute passes appear before vegetation raster passes
2. color/depth vegetation passes do not request global-state mutation
3. buffers have declared read/write dependencies
4. no vegetation pass calls job completion or blocking readback
5. `ExecuteRenderGraph` no longer contains worker-thread job gaps
6. GPU time scales with selected draw volume, not CPU preparation synchronization
