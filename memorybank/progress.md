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
- Current runtime shell-node rule is explicit and conservative: visible internal nodes expand, visible leaves emit, and finer intra-tier collapse is deferred.
- Current prepared-frame reality is not the final target architecture yet: CPU reference output is still the default render source, while the optional GPU mode is a delayed non-blocking decision-readback bridge rather than a fully GPU-decoded frontier.
- Compile validation succeeded through `Fully Compile by Unity`.

## Phase E Clarification

- Milestone 1 `Phase E` implementation work is only partially complete against [Milestone1.md](../DetailedDocs/Milestone1.md).
- Code-side render infrastructure from Milestone `E1` through `E5` exists in the repo.
- `Phase E` is not accepted complete.
- There is also an architectural gap versus the milestone target: the current shipped path still defaults to CPU-prepared visible output, so the intended GPU-primary decode ownership is not fully satisfied yet.

## Immediate Tasks

### Low hanging fruits

- Validate in Playground that the fixed `TransformBounds`/render-pass GC spike is actually gone under `GpuDecisionReadback`.
- Capture a new Playground profile and compare `VegetationRuntimeManager.TryConsumeGpuReadback`, `VegetationRuntimeManager.DecodeGpuReadback`, `VegetationRuntimeManager.UploadGpuReadback`, `VegetationGpuDecisionPipeline.CopyDecisionBuffers`, and `VegetationIndirectRenderer.SlotResources.UploadInstanceBuffer`.
- Capture a new Playground profile and compare `VegetationDecisionDecoder.DecodeTrees` against `VegetationDecisionDecoder.Decode` overall to confirm the tree-level and shell-tier rejects actually removed the hot path.
- If `UploadInstanceBuffer` still dominates with hundreds of active slots, stop pretending the bottleneck is decode and investigate slot-count reduction or shared-buffer upload architecture.

### Phase E: Hybrid Decode Rendering Pipeline

- `E-05` End-to-end demo-scene verification in the Playground scene with `DebugVegetationDemo`.
- Investigate the batch-mode kernel-import issue on `VegetationClassify.compute` so the optional GPU decision-readback mode stops being editor-only confidence work.
- Decide whether CPU should remain the default prepared-frame source or whether the delayed GPU readback bridge is acceptable as the default MVP path after manual scene validation.
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
