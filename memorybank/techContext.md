# Technical Context

Purpose: compact toolchain, package, and verification reference for the current repo. Uses recommended Unity package workflow with explicit separation of editor/runtime and non-essential samples.

## Engine and Language

- Unity: `6000.3.7f1`
- C#: `8` target style, current generated projects report `LangVersion 9.0`
- API compatibility: `.NET Standard 2.1`

## Core Packages

- `com.unity.inputsystem 1.18.0`
- `com.unity.test-framework 1.6.0`
- `com.unity.cinemachine 3.1.4`
- `com.unity.addressables 2.8.1`
- `com.unity.render-pipelines.universal 17.3.0`
- `com.unity.collections 2.6.2`
- `com.unity.mathematics 1.3.3`
- `com.unity.burst 1.8.27`
- `com.unity.entities 1.4.5`
- `com.unity.entities.graphics 1.4.18`
- `com.unity.physics 1.4.5`

Repo-used libraries and plugins:

- none

## Repo Structure

- `Packages/com.voxgeofol.vegetation`
  - embedded public package for the vegetation feature
- `Packages/com.voxgeofol.vegetation/Runtime`
  - runtime-side authoring data and shared vegetation code
  - `Runtime/Rendering/` holds the production runtime path: runtime-safe tree contracts, `AuthoringContainerRuntime`, classic-scene provider `VegetationRuntimeContainer`, active-runtime-owner registry, tree-first contracts/registration, the GPU-resident compute accept/count/emit path, the indirect renderer submission path, and the URP renderer (render graph compatible) feature/pass integration
  - current render-material ownership on the package-compatible path uses the provided materials directly; `Runtime/Rendering/VegetationIndirectMaterialFactory.cs` is now only a no-op placeholder kept around to satisfy stale generated solution files until the next full Unity solution regeneration
  - `Runtime/Shaders/VegetationClassify.compute` is the compute entry point for the urgent tree-first runtime: visible-cell classification, visible-tree tier acceptance, promoted-tree-only branch work generation, count/pack, and indirect emission
  - `Runtime/Shaders/VegetationCanopyLit.shader`, `VegetationTrunkLit.shader`, `VegetationFarMeshLit.shader`, `VegetationDepthOnly.shader`, and `VegetationIndirectCommon.hlsl` are the runtime shader suite used by indirect vegetation submission; the package shaders now carry main-light shadow attenuation and `ShadowCaster` passes, and `VegetationRendererFeature` now appends indirect vegetation casters into the URP main-light shadow atlas through a render-graph unsafe pass. Current shadow contract is still limited to main-light directional shadows using cascade-specific resident frames derived from the camera-visible vegetation set, with default `TreeL3`/impostor shadow casters and expanded branch shadow promotion disabled unless explicitly enabled.
  - `VoxelizerV2/` hosts the CPU/GPU voxel utilities; the production canopy, generated branch wood, simplified trunk, and impostor bake paths now use the CPU volume + bounded surface mesh path from this folder, including optional coplanar-face reduction through `CpuVoxelSurfaceMeshBuilder`
- runtime rendering authority is `Graphics.RenderMeshIndirect` through `Runtime/Rendering`; current shipped implementation accepts one tree tier per visible tree on GPU through `VegetationGpuDecisionPipeline`, expands branch work only for promoted trees, binds shared GPU-written instance/args buffers through `VegetationIndirectRenderer`, preserves signed `BaseVertexLocation` in indirect args, and currently filters submission to the non-zero emitted slot subset through a synchronous CPU readback safety path
- The package vegetation shaders now carry their own `DepthOnly` pass in addition to forward and shadow-caster passes, so the runtime can bind the provided materials directly instead of cloning per-slot runtime materials for package-compatible content.
- urgent-path diagnostics now include per-container total compute-buffer bytes, total graphics-buffer bytes, total GPU-buffer bytes, registered draw-slot count, and runtime material-copy count so dense-scene GPU investigations can separate resource footprint from device-removal faults
- `Packages/com.voxgeofol.vegetation/SubScene`
  - DOTS-only support assembly for unity runtime closed-`SubScene`. It contains `SubSceneAuthoring`, bakers, baked runtime-safe tree/container records, and the bootstrap system that instantiates `AuthoringContainerRuntime` from baked data while keeping the main `Vegetation` asmdef DOTS-free.
- `Packages/com.voxgeofol.vegetation/Editor`
  - Milestone1 Phase B hierarchical shell/trunk/impostor bake tooling, Phase C preview/inspector/window tooling, the Milestone1 Phase C.5 gate summary in `VegetationTreeAuthoringEditorPanel`, and the editor-only simplification/fallback helpers used by shell, wood, trunk, and impostor generation
- `Packages/com.voxgeofol.vegetation/Tests/Editor`
  - vegetation EditMode coverage that now ships with the package
- `Packages/com.voxgeofol.vegetation/Samples~/VegetationDemo`
  - distributable non-essential demo assets for public package consumers
- `Assets/VegetationDemo`
  - local workspace mirror of the vegetation demo assets so repo scenes keep working
- `Assets/Tree/VoxFoliage/GeneratedMeshes`
  - explicit native `.mesh` assets generated by the vegetation shell/impostor bake pipeline for the local demo assets
- `Assets/Scripts/MassPlacement`
  - editor-triggered scatter utility that raycasts down onto physical ground
- `Assets/Scripts`
  - repo-local debug/demo scripts; `GameRuntime.asmdef` now references `Vegetation` so local comparison components can exercise the package voxel runtime directly
- `Assets/EditorTests`
  - non-package repo-local EditMode tests
- `Assets/Editor`
  - Editor tools, utilities and visualization (in editor)
- `DetailedDocs`
  - feature-specific architecture and ASCII authority docs
- `memorybank`
  - compact cross-cutting repo guidance and routing

## Build / Verification Flow

- Build entry points are defined in `.vscode/tasks.json`.
- Fast compile: `Compile by Rider MSBuild`
- Mandatory full compile when new `.cs` or `.asmdef` files are added: `Fully Compile by Unity`
- Test runner wrapper: `runTestsFromRoot.sh`
- Result parser: `runParsetests.sh`
- Authoritative outputs:
  - `CI/CITestOutput.xml`
  - `CI/CompileErrorsAfterUnityRun.txt` (search for `error CS...` lines plus `## Script Compilation Error` blocks with a 50-line capture window of Burst generated error)

## Constraints

- EditMode tests must not rely on Unity lifecycle callbacks.
- No useless maintenance. Scripts are either completely dropped or fully migrated to new api, no obsolete wrappers!
- No useless abstractions, no bloatware.
- Because Unity generates the authoritative compile/test outputs, the Unity editor must be closed before running the required compile/test scripts.
- New or renamed `.cs` files require Full Unity compile path so the generated solution is rebuilt from Unity itself.
- Vegetation package code lives under `Packages/com.voxgeofol.vegetation`; do not add new vegetation scripts back under `Assets/Scripts`.
- Generated vegetation meshes must be written into project `Assets/` space, never into `Packages/`, so public package installs stay writable.
- Editor-baked voxel artifacts must be clipped back to authoritative source bounds; this applies to canopy shells, generated branch wood, simplified trunk meshes, and impostor meshes. Persisted shell-node `localBounds` are authored ownership bounds, not mesh-tight bounds.
- Current vegetation runtime authority is the urgent tree-first path: `Impostor` far only, `TreeL3` mandatory non-far floor, and promoted branch-expanded `L2/L1/L0` built from reusable blueprint placements plus compact branch prototype tier meshes. Pre-populated `SceneBranches[]` and runtime shell-node ownership are removed from the production path.
- Runtime vegetation ownership is  split cleanly: `AuthoringContainerRuntime` is the only runtime owner, `VegetationRuntimeContainer` is the classic-scene lifecycle provider, and `SubSceneAuthoring` plus `Vegetation.SubScene` are the closed-`SubScene` bake/bootstrap provider path. The main `Vegetation` assembly stays DOTS-free.
- `VegetationRuntimeContainer` runtime registration is currently snapshot-based and container-scoped. After the container is enabled, transform edits and other registration-affecting `VegetationTreeAuthoring` changes are not live-synced until `RefreshRuntimeRegistration()` runs. Each container  converts its explicit serialized authorings list into shared `VegetationTreeAuthoringRuntime` records, and every referenced authoring must remain inside that container's hierarchy. Multiple containers are supported specifically to enable streamed/addressable ownership boundaries.
- Closed `SubScene` runtime loading  requires `SubSceneAuthoring` on the same GameObject as `VegetationRuntimeContainer`. The plain container alone is only the classic-scene provider and will not bootstrap runtime ownership from baked `SubScene` data.
- Runtime is GPU-resident only. Missing compute support, missing `VegetationClassify.compute`, or shader-import failures are hard blockers because the old CPU/readback runtime path was removed. Current render-loop contract is no-throw: URP feature and per-container render prep must log and skip or fault-disable instead of throwing inside render-graph execution.
- Render-graph raster passes that submit vegetation indirect draws must opt into global-state mutation because the renderer binds shared instance buffers and slot indices through command-buffer globals instead of per-slot material copies.
- Recent dense-forest D3D12 failures point to device-removal risk, not just raw local-VRAM exhaustion; investigate retained runtime allocations, indirect-args/state lifetime, signed indirect-args fields such as `BaseVertexLocation`, and compute-shader index safety before assuming the fix is only lowering visible-instance capacity.
- That budget is container-scoped, not global-scene-scoped. One container owns one packed visible-instance buffer and one GPU pipeline. Multiple active containers mean multiple independent budgets and multiple independent packed buffers.
- The current scripting toolchain tops out at `LangVersion 9.0`; use block-scoped namespaces, not file-scoped namespaces.
- Use message-bus or singleton when cross communication is needed (prefer message bus with explicit sender and data).
- All static runtime classes must have explicit `Reset` method that is invoked once in the package-owned editor lifecycle hook `VegetationEditorLifecycleReset.cs`.
- Editor lifecycle teardown now lives in the package editor assembly and includes `AssemblyReloadEvents.beforeAssemblyReload` and `EditorApplication.quitting` in addition to play-mode exit, and it resets both scene-owned `VegetationRuntimeContainer` providers and the shared active-runtime registry so GPU buffers do not survive Unity recompiles.
- Existing vegetation EditMode bake tests keep bake settings explicit per fixture instead of relying on authoring defaults
- Use Univesral Render Pipeline compatible shaders.
- Hierarchical wind is still a Milestone 2 target, not a shipped runtime feature.
