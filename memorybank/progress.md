# Progress

Purpose: track immediate tasks and current milestone status.

## Current Milestone

- Milestone: `Milestone 1 - MVP Assembled Vegetation`
- Scope authority: [Milestone1.md](../DetailedDocs/Milestone1.md)
- Runtime/data authority: [UnityAssembledVegetation_FULL.md](../DetailedDocs/UnityAssembledVegetation_FULL.md)

## Status Snapshot

- `Phase A` through `Phase C.5` are complete. Runtime/data contract sync was finalized on `2026-04-05`.
- Active work is `Phase D`: runtime registration/flattening, visibility/classification/decode foundation, and stable Phase E handoff outputs.
- No meaningful Phase D runtime/rendering scaffolding exists yet, so the work must start with contracts and a deterministic reference mirror instead of renderer-first integration.

## Immediate Tasks

### Phase D: Spatial Grid + Runtime Data Foundation

- [ ] `D-01` Freeze the Phase D runtime/output contracts: flattened tree/blueprint/branch/prototype payloads, BFS shell-node payloads, branch/node decision payloads, per-slot visible-instance outputs, indirect-arg seed inputs, and visible-data bounds.
- [ ] `D-02` Implement `VegetationSpatialGrid` with deterministic tree-to-cell registration, authoritative cell bounds, and visible-cell query output.
- [ ] `D-03` Implement runtime registration/flattening from authored data into tree spheres, branch spheres, draw-slot registries, and BFS shell-node caches without touching editor-only fields.
- [ ] `D-04` Implement a deterministic CPU reference mirror for tree classification, branch tier selection, shell-node decisions, trunk selection, and BFS frontier decode.
- [ ] `D-05` Implement the GPU-primary visibility/classification/decision path against the same contracts. CPU fallback may consume only completed non-blocking async readback results.
- [ ] `D-06` Emit stable per-slot visible-instance data, indirect-arg seed inputs, and visible-data bounds for Phase E. Keep renderer submission out of Phase D.
- [ ] `D-07` Add spatial-grid tests plus CPU/GPU classification/decode parity checks and frame-capture validation.
- [ ] `D-08` Run compile validation and targeted manual verification.

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
