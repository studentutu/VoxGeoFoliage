# Progress

Purpose: track immediate tasks and current milestone status.

## Current Milestone

- Milestone: `Milestone 1 - MVP Assembled Vegetation`
- Scope authority: [Milestone1.md](../DetailedDocs/Milestone1.md)
- Runtime/data authority: [UnityAssembledVegetation_FULL.md](../DetailedDocs/UnityAssembledVegetation_FULL.md)

## Status Snapshot

- `Phase A` through `Phase E` are implemented.
- `2026-04-10`: Phase E render infrastructure landed. `VegetationIndirectRenderer`, `VegetationRendererFeature`, the runtime shader suite, and `VegetationRuntimeContainer` now prepare and submit camera-visible vegetation frames through indirect depth/color passes.
- `2026-04-11`: Removed the constant per-frame `TransformBounds` corner-array GC from Phase E visible-bounds rebuilds, removed render-graph `managers.ToArray()` garbage in `VegetationRendererFeature`, and added profiler markers across frame prep, decode, upload, and render submission so Playground captures can isolate the next bottleneck.
- `2026-04-11`: Removed the wasted full `VegetationFrameDecisionState.Reset()` on completed GPU readback consume, replaced temporary visible-cell array rebuilds with in-place mask expansion, changed decode output to write `VegetationIndirectInstanceData` directly into slot outputs, and removed the per-slot CPU repack loop in `VegetationIndirectRenderer`. New markers now split GPU readback consume, decode, instance-buffer upload, and args upload.
- `2026-04-11`: Reworked `VegetationDecisionDecoder.DecodeTrees` so it rejects whole trees before branch traversal, rejects whole shell tiers before scanning node decisions, and consumes precomputed per-tree/per-branch/per-node world bounds from runtime registration instead of recomputing transformed draw-slot bounds in the decode hot loop.
- `2026-04-11`: Removed more decode hot-path payload churn by caching per-tree/per-branch `VegetationIndirectInstanceData` at registration time, switching frame-output append calls to readonly-ref payload/bounds flow, and disabling per-instance debug capture on the old runtime frame-output path unless diagnostics were enabled.
- `2026-04-11`: Replaced the runtime prepared-frame hot path with a GPU-resident decode/emission path. `VegetationGpuDecisionPipeline` now classifies, emits exact indirect instance payloads into a shared GPU instance buffer, writes one indirect-args record per draw slot, and `VegetationIndirectRenderer` consumes those GPU-written buffers directly through per-slot args offsets. `DecodeTrees` is no longer part of the normal `GpuResident` render path.
- `2026-04-11`: Production cleanup removed the legacy runtime CPU-reference and GPU-readback container branches. `VegetationRuntimeContainer` now prepares only GPU-resident frames, and the old classification Scene-view demo was removed from the Playground scene.
- `2026-04-11`: Final rendering cleanup removed the remaining editor-only parity/decode path from `Runtime/Rendering`, deleted the runtime `DebugVegetationDemo`, and trimmed EditMode coverage to production rendering contracts only.
- `2026-04-11`: Renamed `VegetationRuntimeManager` to `VegetationRuntimeContainer` and scoped runtime registration to container-owned authorings. That ownership now lives in an explicit serialized authorings list that editor tooling can refill while excluding descendants claimed by nested child containers.
- `2026-04-11`: Moved `VegetationClassify.compute` assignment out of `VegetationRuntimeContainer` and into `VegetationFoliageFeatureSettings.ClassifyShader`, updated the Playground/runtime renderer assets, and removed runtime `GetComponent` / `GetComponentsInChildren` discovery from `Runtime/Rendering`.
- `2026-04-11`: Moved `enableDiagnostics` out of `VegetationRuntimeContainer` and into `VegetationFoliageFeatureSettings.EnableDiagnostics`. Diagnostics are now renderer-feature scoped for all containers rendered by that feature.
- `2026-04-11`: Runtime rendering hardening closed constructor-failure leak paths in `VegetationGpuDecisionPipeline` and `VegetationIndirectRenderer`, and removed steady-state diagnostics churn by deduplicating render/container/feature logs on scalar state before building strings.
- `2026-04-11`: Package-facing rendering setup docs were tightened: runtime inspector fields now carry tooltips, and the package README explicitly documents the render-graph/URP/instance-shader constraints plus the end-to-end GPU-resident rendering pipeline.
- `2026-04-11`: Package README now also explains the exposed runtime settings that materially affect behavior, including `VegetationRuntimeContainer.gridOrigin` / `cellSize` and `VegetationRendererFeature.DepthPassEvent` / `ColorPassEvent`.
- `2026-04-11`: Package README now documents the general vegetation runtime design and batching contract: one authoring references one blueprint, blueprints can mix or reuse branch prototypes, and draw calls scale with active draw slots keyed by `mesh + material + material kind`, not with raw tree count.
- `2026-04-11`: `Fully Compile by Unity` succeeded after the production cleanup and regenerated the solution without the removed legacy files.
- Current runtime shell-node rule is explicit and conservative: visible internal nodes expand, visible leaves emit, and finer intra-tier collapse is deferred.
- Current prepared-frame reality changed materially: `GpuResident` is the only shipped runtime path. Legacy CPU/decode helpers are removed from `Runtime/Rendering`.
- `VegetationRuntimeContainer` registration is currently snapshot-based, not live-synced: transform edits on registered `VegetationTreeAuthoring` instances, plus other registration-affecting authoring or scene changes after container enable, require explicit `RefreshRuntimeRegistration()` or a disable/enable cycle.

## Phase E Clarification

- Milestone 1 `Phase E` implementation work exists end-to-end in the repo with the production GPU-resident path.
- Current hardening focus is no longer CPU decode removal; that work is done.
- Remaining production work is contract hardening around multi-container streaming/addressables, lifecycle refresh behavior, and package-consumer integration clarity.

## Known `VegetationRuntimeContainer` Limitations

- Runtime transform edits are not live-synced. After the container is enabled, moving, rotating, or scaling a `VegetationTreeAuthoring` does not update the frozen registry until `RefreshRuntimeRegistration()` runs.
- Registration-affecting content changes are not live-synced either. Adding or removing authorings, changing blueprint placement data, or swapping generated meshes after enable also requires `RefreshRuntimeRegistration()`.
- Each container only owns active authorings from its serialized list. If foliage should belong to a specific streamed/addressable chunk, it must live under that chunk's `VegetationRuntimeContainer` transform and that container's serialized list must be refilled after hierarchy ownership changes.
- The container is GPU-resident only. Missing compute support, missing `VegetationClassify.compute`, or shader-import failures are hard runtime blockers because the old CPU/readback fallback path was removed.
- Missing `VegetationFoliageFeatureSettings.ClassifyShader` is now also a hard runtime blocker; containers no longer serialize their own compute shader reference.
- Exact CPU-side visible-instance debug output is no longer part of the production container contract. Runtime diagnostics stop at profiler markers plus conservative uploaded batch snapshots with unknown exact counts.

## Immediate Tasks

### Production Hardening

- Validate multi-container streamed/addressable ownership in a real chunked scene: sibling containers, nested child containers, instantiate/release, and additive-scene unload.
- Harden lifecycle behavior around `OnEnable`, `OnDisable`, and explicit `RefreshRuntimeRegistration()` so container refresh rules stay explicit and deterministic under scene streaming.
- Run a package-consumer smoke pass in a clean URP project to verify that the package README setup steps match actual import and renderer-feature wiring.
- Decide whether GPU-resident submission needs double buffering / one-frame latency as an optimization pass, not as a correctness fix.
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
