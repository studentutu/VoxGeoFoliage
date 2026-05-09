# Progress

Purpose: current milestone, current blockers, next tasks. Nothing else.

## Current Milestone

- Milestone: `Milestone 2 - Production Runtime Cleanup`
- Scope authority: [Milestone2.md](../DetailedDocs/Milestone2.md)
- Runtime authority: [VegetationRuntimeArchitecture.md](../DetailedDocs/VegetationRuntimeArchitecture.md)
  now includes full ASCII bake, registration, color/depth, and shadow pipelines with payload ownership and resident-memory surfaces
- Strategic redesign proposal: [VegetationGenerationalRedesign.md](../DetailedDocs/VegetationGenerationalRedesign.md)
  defines the recommended non-HZB production baseline and full replacement migration: authoring branch/tree graph -> editor-compiled assembly/page assets -> immutable representation packets -> streaming providers -> global render world -> CullingGroup page/cell broad phase -> packet selection and active budgets -> `CheapTree` shadow packets -> shader wind -> grouped indirect submission. Target scale is 100k to 1M loaded instances with streaming. No old/new renderer toggle, no maintained tree-first bridge, no production `TreeL3` floor, no production `ShadowProxyL0/L1`, no telemetry-gated active-slot submission, and no HZB before packet renderer production verification. The review explicitly moves branch placement matrices, bounds, packet costs, shadow mapping, wind metadata, command bounds, and page/cell split decisions to the editor compiler. The unityHISM review is captured there as a partial BRG reference for chunk blobs, sub-batch windows, culling callback command emission, and command compaction, not as the target vegetation architecture.
- Finished baseline: [Milestone1.md](../DetailedDocs/Milestone1.md)
- Latest completed cleanup: branch prototype authoring now persists only the split-tier runtime mesh chain (`branchL1/2/3CanopyMesh` + `branchL1/2/3WoodMesh`); obsolete shell-node authoring/runtime contracts and sample per-node shell assets were removed.

## Current Blockers

- shadow target is now `ShadowMode.Off` / `ShadowMode.CheapTree`, but current code still exposes legacy `RenderMainLightShadows` / `AllowExpandedTreePromotionInShadows`
- shadow currently reuses the same default budget shape as color, so explicit-frustum/shadow preparation doubles fixed residency without proving it needs to
- camera and explicit-frustum preparation still keep two full GPU pipelines per active container instead of the target pooled prepared-view residency
- current enabled shadow promotion can use independent `ShadowProxyL0/L1` tree proxies; production target requires same-as-color near `L0/L1` shadows and cheap tree-only farther `L2/TreeL3` shadows instead
- visible-instance clamping is still slot-order biased
- active-slot submission and actual-usage telemetry are latest async readback snapshots, so first prepared frames can still fall back to registered slots and reported counts can lag the frame being rendered
- dense-forest and shadow validation are still pending on the split-budget path

## Next Tasks

Goal: execute the full replacement migration to the packet renderer. Do not spend effort maintaining the old tree-first runtime beyond what is necessary to delete it cleanly.

1. Define `FoliageAssemblyAsset`, `FoliagePageAsset`, `FoliageRepresentationPacket`, and `AssetGroup` as the final compiled asset contract.
2. Add the editor compiler and one editor upgrade command that converts current container authorings into compiled page assets.
3. Compile page/cell HLOD packets, near-detail packet streams, `CheapTree` shadow packet mappings, wind metadata, and compiler build reports.
4. Add `VegetationRenderWorld` as the only global budget, packet selection, residency, telemetry, and command-emission owner.
5. Replace active-slot/draw-slot submission with grouped packet submission and delete legacy shadow toggles, shadow proxy promotion, and old active-slot submission.
6. Delete or fully repurpose the old tree-first GPU decision path so there is no old/new renderer selection in user settings.
7. Run production verification without HZB across 100k loaded instances, 1M streamed instances, mobile profile, VR stereo profile, shadows, and wind.

Target Result: one production packet renderer, no maintained bridge, no production `TreeL3` floor, no runtime branch-work generator, no active-slot readback submission, and production-ready opaque vegetation with global budgets, shadows, wind, and streaming.
