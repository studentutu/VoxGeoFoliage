# Progress

Purpose: track immediate tasks and current milestone status.

## Current Milestone

- Milestone: `Milestone 1 - MVP Assembled Vegetation`
- Scope authority: [Milestone1.md](../DetailedDocs/Milestone1.md)
- Runtime/data authority: [UnityAssembledVegetation_FULL.md](../DetailedDocs/UnityAssembledVegetation_FULL.md)

## Status Snapshot

- `Phase A` through `Phase E` are implemented.
- `2026-04-10`: Phase E render infrastructure landed. `VegetationIndirectRenderer` now uploads per-slot instance payloads/args/bounds, `VegetationRendererFeature` schedules indirect depth/color passes, the runtime shader suite is in place, `VegetationRuntimeManager` prepares camera-visible frames for rendering, and `DebugVegetationDemo` exposes the uploaded batch state in Scene view.
- `2026-04-11`: Removed the constant per-frame `TransformBounds` corner-array GC from Phase E visible-bounds rebuilds, removed render-graph `managers.ToArray()` garbage in `VegetationRendererFeature`, and added profiler markers across frame prep, decode, upload, and render submission so Playground captures can isolate the next bottleneck.
- `2026-04-11`: Removed the wasted full `VegetationFrameDecisionState.Reset()` on completed GPU readback consume, replaced temporary visible-cell array rebuilds with in-place mask expansion, changed decode output to write `VegetationIndirectInstanceData` directly into slot outputs, and removed the per-slot CPU repack loop in `VegetationIndirectRenderer`. New markers now split GPU readback consume, decode, instance-buffer upload, and args upload.
- `2026-04-11`: Reworked `VegetationDecisionDecoder.DecodeTrees` so it rejects whole trees before branch traversal, rejects whole shell tiers before scanning node decisions, and consumes precomputed per-tree/per-branch/per-node world bounds from runtime registration instead of recomputing transformed draw-slot bounds in the decode hot loop.
- `2026-04-11`: Removed more decode hot-path payload churn by caching per-tree/per-branch `VegetationIndirectInstanceData` at registration time, switching frame-output append calls to readonly-ref payload/bounds flow, and disabling per-instance debug capture on the runtime-manager frame output unless diagnostics are enabled.
- `2026-04-11`: Replaced the runtime prepared-frame hot path with a GPU-resident decode/emission path. `VegetationGpuDecisionPipeline` now classifies, emits exact indirect instance payloads into a shared GPU instance buffer, writes one indirect-args record per draw slot, and `VegetationIndirectRenderer` consumes those GPU-written buffers directly through per-slot args offsets. `DecodeTrees` is no longer part of the normal `GpuResident` render path.
- Current runtime shell-node rule is explicit and conservative: visible internal nodes expand, visible leaves emit, and finer intra-tier collapse is deferred.
- Current prepared-frame reality changed materially: `GpuResident` is now the intended runtime path, while CPU reference remains as a fallback/parity path and `GpuDecisionReadback` remains an optional delayed debug/validation bridge.
- `VegetationRuntimeManager` registration is currently snapshot-based, not live-synced: transform edits on registered `VegetationTreeAuthoring` instances, plus other registration-affecting authoring or scene changes after manager enable, require explicit `RefreshRuntimeRegistration()` or a disable/enable cycle.
- Current `GpuDecisionReadback` manager behavior has another hard caveat: CPU bootstrap while async readback is pending is intentionally disabled, so startup and pending-readback frames can reuse stale uploaded data or show nothing until the first completed readback is available.
- `LastFrameOutput` detailed per-instance debug capture is diagnostics-gated now; with diagnostics disabled the runtime manager keeps upload-ready slot payloads only.
- Compile validation succeeded through `Fully Compile by Unity`.

## Phase E Clarification

- Milestone 1 `Phase E` implementation work is only partially complete against [Milestone1.md](../DetailedDocs/Milestone1.md).
- Code-side render infrastructure from Milestone `E1` through `E5` exists in the repo.
- `Phase E` is not accepted complete.
- The previous CPU-decode ownership gap is closed for the main runtime path, but `Phase E` still needs manual Playground validation and hardening of the GPU-resident path before it can be called accepted complete.

## Known `VegetationRuntimeManager` Limitations

- Runtime transform edits are not live-synced. After the manager is enabled, moving, rotating, or scaling a `VegetationTreeAuthoring` does not update the frozen registry until `RefreshRuntimeRegistration()` runs.
- Registration-affecting content changes are not live-synced either. Adding or removing authorings, changing blueprint placement data, or swapping generated meshes after enable also requires `RefreshRuntimeRegistration()`.
- `GpuDecisionReadback` is delayed and non-blocking. The current implementation does not fall back to a fresh CPU-prepared frame while readback is pending, so first frames can render stale data or nothing.
- Detailed `LastFrameOutput` instance debug data exists only with diagnostics enabled.

## Immediate Tasks

### Low hanging fruits

- Validate in Playground that `VegetationRuntimeManager.PrepareGpuResidentFrame` removed `VegetationDecisionDecoder.DecodeTrees` from the runtime hot path and that the CPU spike is gone in practice.
- Capture a new Playground profile and compare `VegetationGpuDecisionPipeline.ResetIndirectArgs`, `VegetationGpuDecisionPipeline.EmitTreeInstances`, `VegetationGpuDecisionPipeline.EmitBranchInstances`, and `VegetationGpuDecisionPipeline.FinalizeIndirectArgs`.
- Validate that indirect draws consume the GPU-written shared instance/args buffers correctly for expanded trees, trunks, shells, and impostors in both depth and color passes.
- If the GPU-resident path still misses budget, investigate slot-count pressure and draw-count pressure next instead of returning to CPU decode work.

### Phase E: Hybrid Decode Rendering Pipeline

- `E-05` End-to-end demo-scene verification in the Playground scene with `DebugVegetationDemo`.
- Investigate the batch-mode kernel-import issue on `VegetationClassify.compute` so the optional GPU decision-readback mode stops being editor-only confidence work.
- Decide whether the shared-slot GPU-resident path needs double buffering / one-frame latency to improve GPU scheduling, or whether same-frame compute-to-indirect submission is sufficient for MVP.
- Add diagnostics or validation for GPU-resident exact per-slot counts if Scene-view inspection needs more than conservative batch bounds and unknown counts.
- `Phase F` remains optional follow-up work. Texture-driven voxelization is still parked and should not dilute current Phase D/E delivery.

## Deferred

- HiZ depth pyramid occlusion
- LOD transition dithering / cross-fade
- Runtime streaming / dynamic loading
- Hierarchical wind system
- Scale quantization optimization
- Feature-grade placement tools (terrain scatter, paint). Basic editor-only `MassPlacement` physical-ground scatter already exists under `Assets/Scripts/MassPlacement`.
- Cell streaming
- Optional branch baking using quad-cards and uv-sampled-texture workflow (see voxelization based on quadcards and alpha-masked-texture in `GPUTextureDemo` and underlying usage of `GPUVoxelizer`)
