# Progress

Purpose: track the active milestone, the current blockers, and the next concrete tasks only.

## Current Milestone

- Milestone: `Milestone 2 - Wind and Production Improvements`
- Scope authority: [Milestone2.md](../DetailedDocs/Milestone2.md)
- Runtime/data authority: [UnityAssembledVegetation_FULL.md](../DetailedDocs/UnityAssembledVegetation_FULL.md)
- Finished baseline: [Milestone1.md](../DetailedDocs/Milestone1.md)

## Immediate Tasks

### Completed

- Landed the urgent tree-first runtime redesign from `urgentRedesign.md`.
- Added the required `treeL3Mesh` authoring/runtime contract and branch split canopy/wood tiers for `L1/L2/L3`.
- Removed urgent-path runtime ownership of `SceneBranches[]` and prototype shell-node buffers from registration, GPU classification, and submission.
- Replaced the frame path with tree-first acceptance, nearest-first promotion into branch-expanded `L2/L1/L0`, promoted-tree-only compact branch work generation, and final indirect submission over registered draw slots after bind.
- Added urgent-path telemetry for visible trees, accepted `TreeL3`, promoted trees, rejected promotions, expanded branch work-item count, accepted tier-cost usage, and non-zero emitted slots.
- Added shader-level shadow support to the bundled package materials: main-light shadow attenuation in the forward pass and `ShadowCaster` passes for canopy, trunk, and far-mesh shaders.
- Added real URP main-light shadow-atlas submission in `VegetationRendererFeature` for indirect vegetation, using the package `ShadowCaster` passes and cascade-specific resident-frame preparation derived from the camera-visible vegetation set.
- Replaced the broken "replay one camera-visible frame into every cascade" shadow submission path with cascade-specific resident-frame preparation so multi-cascade main-light shadows can render again without the previous pathological GPU overdraw.
- Fixed the D3D12 editor crash / device-removal regression in dense shadowed foliage by defaulting shadow preparation to the cheap `TreeL3`/impostor path and disabling expanded branch shadow promotion unless explicitly enabled.
- Removed the per-call delegate capture from `VegetationIndirectRenderer.RenderInternal()` by introducing explicit command-buffer draw wrappers for `CommandBuffer` and `IRasterCommandBuffer`, and normalized the render-graph callback in `VegetationRendererFeature` to a static method.
- Hardened the runtime render path to be no-throw at URP/render-graph boundaries: per-container prep faults now disable that runtime owner, and `VegetationRendererFeature` / `VegetationRenderPass` fault-disable instead of throwing from rendering callbacks.
- Fixed the `DrawMainLightShadowAtlas()` container-mask capacity regression that threw `IndexOutOfRangeException` during cascade shadow submission.
- Hardened the urgent runtime against retained GPU resources after render faults: faulted containers now defer cleanup of compute buffers, indirect args, and runtime material state instead of leaving those allocations resident for the rest of the editor session.
- Added deterministic editor/play-mode reset coverage for `VegetationActiveAuthoringContainerRuntimes` so static runtime-owner state does not survive across sessions and pin stale runtime allocations.
- Expanded urgent-path diagnostics to report total compute-buffer bytes, total graphics-buffer bytes, total GPU-buffer bytes, registered draw-slot count, runtime material-copy count, and the major backing buffer sizes instead of only a partial branch-focused subset.
- Expanded prepared-frame diagnostics further to log every non-zero emitted slot with per-slot emitted instance count, per-instance index count, and estimated total index count for the next dense-scene crash repro.
- Added compute-shader guard rails on draw-slot, cell, blueprint, lod-profile, placement, and prototype indices so invalid runtime data drops work items instead of issuing undefined UAV writes that can remove the D3D12 device.
- Cleared stale `VegetationRendererFeature` container snapshot references when the active runtime count shrinks so old runtime owners are not kept alive by pass-local arrays after they disappear from the discovery registry.
- Fixed indirect-args base-vertex serialization to preserve signed `BaseVertexLocation` end-to-end instead of reinterpreting it as `uint`, and expanded render diagnostics to print per-slot mesh/shader/index/start/base-vertex metadata for the submitted subset.
- Removed broad `CopyPropertiesFromMaterial()` state transfer from runtime material creation so the package runtime shaders only inherit explicit supported surface properties, and expanded the submitted-slot diagnostics again to print vertex count, submesh count, topology, index format, and source material name.
- Removed package-path runtime material copies from `VegetationIndirectRenderer`: slot state now binds through per-draw command-buffer globals, and the package vegetation shaders now carry `DepthOnly` passes so the provided materials can be used directly across color/depth/shadow submission.
- Kept a no-op `VegetationIndirectMaterialFactory` placeholder file only to satisfy stale generated Rider/Unity project files until the next full Unity solution regeneration; the runtime no longer references it.
- Fixed the render-graph contract regression from the direct-material refactor by allowing global-state mutation in vegetation raster passes, so `SetGlobalBuffer` / `SetGlobalInt` bindings are legal again during depth/color submission.
- Fixed the dense-forest D3D12 crash on the urgent runtime path by removing hot-path synchronous GPU readbacks from prepare, splitting camera and explicit-frustum resident-pipeline ownership, and replacing the single-thread O(N^2) `AcceptTreeTiers` admission kernel with linear priority-ring ordered admission.
- Moved editor-side compilation/reload teardown into the package editor assembly as `VegetationEditorLifecycleReset`, and it now disposes scene-owned `VegetationRuntimeContainer` runtime owners before assembly reload and editor quit, then clears the shared active-runtime registry so vegetation GPU buffers do not survive Unity recompiles.
- Recompiled through `Compile by Rider MSBuild` with no compile errors. `Fully Compile by Unity` was attempted for the latest render-graph work but was blocked because another Unity editor instance already had the project open.

### Next Validation

- Run the dense-forest one-container scene and confirm the telemetry/visual behavior against `DetailedDocs/urgentRedesign.md`: every visible non-far tree holds at least `TreeL3`, promotions happen nearest-first, and the current submitted-slot surface matches the non-zero emitted subset reported by telemetry.
- Verify the new runtime shadow path visually: the main-light shadow atlas receives vegetation casters across cascades, no branch/trunk pass mismatch appears in the atlas, and the documented limitation remains true that only camera-visible vegetation contributes.
- Verify the editor no longer hits D3D12 device removal with vegetation shadows enabled in the dense close-range bush case, and confirm the default shadow result is acceptable with `TreeL3`/impostor casters.
- Re-run the previous dense-forest crash scene at and above the old `1100+` tree threshold and confirm the failure does not return after the linear admission + split-pipeline + readback-removal fix.
- Re-run the scene-view repro that currently faults in `Vegetation Depth Pass` and confirm the render-graph raster path no longer throws `Modifying global state from this command buffer is not allowed` after the pass-level global-state opt-in.
- Trigger a Unity script recompile with an active vegetation scene open and confirm the editor no longer keeps vegetation GPU buffers resident across the compilation boundary.
- Reproduce once with `-force-d3d12-debug` and confirm whether the new compute guard rails eliminate any invalid UAV write / device-removal warnings before blaming container capacity.
- Force one runtime fault path and confirm the next frame tears down `VegetationGpuDecisionPipeline` and `VegetationIndirectRenderer` allocations instead of keeping them alive after the container fault-disables itself.
- Profile again with the same scene and confirm the previous `WaitForGPU` regression is gone or materially reduced now that each cascade prepares its own resident frame instead of replaying the full camera-visible set into every cascade.
- Implement GPU-side active-slot compaction and actual-work-item dispatch sizing so the runtime stops iterating registered slots and stops dispatching branch count/emit against full visible-instance capacity.
- Close all Unity editor instances on the project and rerun `Fully Compile by Unity` so the render-graph integration is validated by the editor-side compiler too.

### Milestone 2 Breakdown

- Freeze the public runtime material compatibility contract: direct-compatible materials, explicit adapter/binding path, and hard-fail validation for unsupported materials.
- Remove `VegetationIndirectMaterialFactory` as the public material authority so runtime does not silently replace authored materials with package-only shaders.
- Enforce shader contract for the custom materials, test on the package material (enforce to use package supportVegetation.hlsl, not yet implemented).
- Resolve draw-slot identity from the final runtime-compatible material pair, not from stale pre-conversion assumptions.
- Freeze the first-pass hierarchical wind contract: global wind inputs, species-level profile, per-tree phase seed, and tier-specific deformation rules.
- Implement wind support in the compatible material path so custom project materials are not locked out of the wind system.
- Freeze the `GPUVoxelizer` masked-quad bake input contract and implement canopy-shell generation from quad and alpha-masked branch inputs.
- Split editor-preview shell data away from the runtime-facing asset graph once the current urgent runtime path is stable, so preview-only shell payloads stop riding along with production runtime assets.
- Run a clean URP package-consumer smoke pass and tighten docs around custom materials, masked-quad bake support, depth-pass requirements, and runtime setup.

## Improvement right after Milestone

- Dithered LOD transitions
- DFS hierarchy migration plus subtree spans
- Scale quantization optimization
- Full HiZ depth pyramid occlusion for tree and branch work after prioritization is shipped and measured

## Deferred

- Feature-grade placement tools
- Optional texture/quad-card branch baking follow-up
