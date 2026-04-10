# Progress

Purpose: track immediate tasks and current milestone status.

## Current Milestone

- Milestone: `Milestone 1 - MVP Assembled Vegetation`
- Scope authority: [Milestone1.md](../DetailedDocs/Milestone1.md)
- Runtime/data authority: [UnityAssembledVegetation_FULL.md](../DetailedDocs/UnityAssembledVegetation_FULL.md)

## Status Snapshot

- `Phase A` through `Phase D` are implemented. Phase D landed on `2026-04-09` with runtime registration/flattening, deterministic spatial-grid registration, CPU reference classification/decode, renderer-neutral per-slot visible outputs, and a GPU parity hook.
- `2026-04-10`: Phase E render infrastructure landed. `VegetationIndirectRenderer` now uploads per-slot instance payloads/args/bounds, `VegetationRendererFeature` schedules indirect depth/color passes, the runtime shader suite is in place, `VegetationRuntimeManager` prepares camera-visible frames for rendering, and `DebugVegetationDemo` exposes the uploaded batch state in Scene view.
- Current runtime shell-node rule is explicit and conservative: visible internal nodes expand, visible leaves emit, and finer intra-tier collapse is deferred.
- Current prepared-frame reality is not the final target architecture yet: CPU reference output is still the default render source, while the optional GPU mode is a delayed non-blocking decision-readback bridge rather than a fully GPU-decoded frontier.
- Compile validation succeeded through `Fully Compile by Unity` on `2026-04-10`.
- Unity EditMode tests were not rerun after Phase E because the repo guidance prefers manual user-triggered Unity test runs for the long editor path.

## Phase E Clarification

- Milestone 1 `Phase E` implementation work is only partially complete against [Milestone1.md](../DetailedDocs/Milestone1.md).
- Code-side render infrastructure from Milestone `E1` through `E5` exists in the repo.
- `Phase E` is not accepted complete.
- The remaining explicit milestone work is `E6` demo-scene verification and the manual portion of `E7`.
- There is also an architectural gap versus the milestone target: the current shipped path still defaults to CPU-prepared visible output, so the intended GPU-primary decode ownership is not fully satisfied yet.

## Immediate Tasks

### Phase E: Hybrid Decode Rendering Pipeline

- [x] `E-01` Freeze renderer ownership between `VegetationRuntimeManager`, `VegetationIndirectRenderer`, and `VegetationRendererFeature`.
- [x] `E-02` Implement `VegetationIndirectRenderer` consumption of Phase D draw-slot outputs, visible-instance payloads, indirect-arg seeds, and rebuilt per-slot bounds.
- [x] `E-03` Implement the URP render-pass integration and the four-shader runtime suite against one instance-payload contract.
- [x] `E-04` Implement the runtime/renderer wiring plus the optional non-blocking GPU decision readback bridge. The batch-mode kernel-import issue on `VegetationClassify.compute` is still open.
- [ ] `E-05` End-to-end demo-scene verification for expanded trees, impostors, per-slot counts, and rebuilt `worldBounds`.

## Next Up

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
