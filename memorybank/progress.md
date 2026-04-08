# Progress

Purpose: track immediate tasks and current milestone status.

## Current Milestone

- Milestone: `Milestone 1 - MVP Assembled Vegetation`
- Scope authority: [Milestone1.md](../DetailedDocs/Milestone1.md)
- Runtime/data authority: [UnityAssembledVegetation_FULL.md](../DetailedDocs/UnityAssembledVegetation_FULL.md)

## Status Snapshot

- `Phase A` through `Phase D` are implemented. Phase D landed on `2026-04-09` with runtime registration/flattening, deterministic spatial-grid registration, CPU reference classification/decode, renderer-neutral per-slot visible outputs, and a GPU parity hook.
- Current runtime shell-node rule is explicit and conservative: visible internal nodes expand, visible leaves emit, and finer intra-tier collapse is deferred.
- Compile validation succeeded through `Fully Compile by Unity`.
- Unity EditMode tests passed with one intentional ignore: `VegetationRuntimeFoundationTests.GpuDecisionPipeline_MatchesCpuReferenceForL1ShellBranch` is skipped when the batch environment imports `VegetationClassify.compute` without exposing the expected kernels. The runtime path throws explicit `NotSupportedException` in that case instead of silently faking GPU success.

## Immediate Tasks

### Phase E: Hybrid Decode Rendering Pipeline

- [ ] `E-01` Freeze renderer ownership between `VegetationRuntimeManager`, `VegetationIndirectRenderer`, and `VegetationRendererFeature`.
- [ ] `E-02` Implement `VegetationIndirectRenderer` consumption of Phase D draw-slot outputs, visible-instance payloads, indirect-arg seeds, and rebuilt per-slot bounds.
- [ ] `E-03` Implement the URP render-pass integration and the four-shader runtime suite against one instance-payload contract.
- [ ] `E-04` Replace the current parity-only GPU hook with the real non-blocking GPU-primary decode/readback bridge and investigate the batch-mode kernel-import issue on `VegetationClassify.compute`.
- [ ] `E-05` End-to-end demo-scene verification for expanded trees, impostors, per-slot counts, and rebuilt `worldBounds`.

## Next Up

- `Phase E` starts only after Phase D outputs are stable.
- `E-01` Freeze renderer ownership between `VegetationRuntimeManager`, `VegetationIndirectRenderer`, and `VegetationRendererFeature`.
- `E-02` Implement the four-shader render suite with one consistent instance payload contract.
- `E-03` Implement per-slot indirect renderer upload, indirect args, and visible-data bounds rebuild.
- `E-04` Wire runtime scene registration/resources into renderer consumption without editor leakage.
- `E-05` Integrate URP render passes and validate expanded/impostor rendering parity in a demo scene.
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
