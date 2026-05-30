# Technical Context

Purpose: compact toolchain, package, and verification reference for the current repo. Uses recommended Unity package workflow with explicit separation of editor/runtime and non-essential samples.

## Engine and Language

- Unity: `6000.3.15f1`
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
  - `Runtime/Rendering/` holds the production runtime path: `VegetationRenderWorld`, compiled provider registration through `VegetationRuntimeContainer` and `Vegetation.SubScene`, global page/cell culling, packet selection, BRG batch resources, shader wind binding, and fallback URP RenderGraph grouped-indirect passes. The retired tree-first runtime family was deleted.
  - `Runtime/Rendering/Compiled/` holds the active compiled renderer asset contract: `FoliageAssemblyAsset`, `FoliagePageAsset`, `FoliageAssetGroup`, `FoliageRepresentationPacket`, packet residency, compiled shadow mode, static wind metadata, static page tree/cell/instance records, compiler settings, and build reports.
  - `Runtime/Shaders/VegetationCanopyLit.shader`, `VegetationTrunkLit.shader`, `VegetationFarMeshLit.shader`, `VegetationDepthOnly.shader`, and `VegetationIndirectCommon.hlsl` are the runtime shader suite. They support BRG/DOTS metadata for matrices, `_VegetationPackedLeafTint`, and `_VegetationWind`, plus grouped-indirect `_VegetationInstanceData` only for procedural non-DOTS variants. Shader wind uses compiled tree phase/anchor data plus global wind and leaf-flutter settings. Shadow support is main-light directional only through `VegetationShadowMode.Off` / `CheapTree`.
  - `VoxelizerV2/` hosts the CPU voxel utilities; the production canopy, generated branch wood, and simplified trunk paths use the CPU volume + bounded surface mesh path from this folder, including optional coplanar-face reduction through `CpuVoxelSurfaceMeshBuilder`
- runtime rendering authority is `VegetationRenderWorld` through `Runtime/Rendering`; it consumes compiled `FoliagePageAsset` packets from classic-scene and SubScene providers. Supported raw-buffer APIs use one `BatchRendererGroup` batch per compiled `FoliageAssetGroup`; Direct3D12 or unsupported/faulted BRG setup uses URP RenderGraph grouped-indirect color/depth/shadow passes. Selection is page/cell based, nearest-to-farthest, budgeted globally, HLOD-backed, and free of synchronous GPU readbacks or live ScriptableObject reads inside BRG callbacks. Do not register custom vegetation BRG on Direct3D12 Unity `6000.3.15f1`; native `InjectShadowDrawCommands` crashes from the registration itself.
- The package vegetation shaders now carry their own `DepthOnly` pass in addition to forward and shadow-caster passes, so the runtime can bind the provided materials directly instead of cloning per-slot runtime materials for package-compatible content.
- `Packages/com.voxgeofol.vegetation/SubScene`
  - DOTS-only support assembly for Unity runtime closed-`SubScene`. It bakes compiled assembly/page object references from the sibling `VegetationRuntimeContainer` and registers one `VegetationRenderWorld` provider when the baked entity loads.
- `Packages/com.voxgeofol.vegetation/Editor`
  - canopy/wood/trunk bake tooling, Phase C preview/inspector/window tooling, `VegetationRuntimeContainer` inspector compilation, the `VegetationTreeAuthoringEditorPanel` gate summary, and the editor-only simplification/fallback helpers used by canopy, wood, and trunk generation
  - `Editor/Compiled/` holds the page compiler. It validates compile-required opaque inputs, precomputes static transforms/bounds/cell records/packet ranges, enforces page/cell/packet/near-detail-byte caps, emits PageHLOD/CellHLOD instance packets by collapsing trees to baked `impostorMesh` assets, marks HLOD packets always-resident, marks `TreeL0/L1/L2` packets near-detail, compiles shadow/wind metadata, reports estimated resident mesh payload, and emits generated page assets for the render world. Wind metadata uses the owning tree bounds for phase and anchor height across trunk, branch, canopy, and HLOD packets so branch packets inherit trunk sway at their world height.
- `Packages/com.voxgeofol.vegetation/Tests/Editor`
  - vegetation EditMode coverage that now ships with the package, including compiler coverage for deterministic output, cap enforcement, page splitting, contiguous packet ranges, absence of shadow-proxy asset groups, HLOD residency, shadow metadata, wind metadata, near-detail byte cap failures, non-blocking authoring budget/tier warnings, and render-world packet selection for near detail, near-detail tier degradation, HLOD fallback, near-detail resident/upload budget blocking and eviction, and CheapTree shadows
- `Packages/com.voxgeofol.vegetation/Samples~/VegetationDemo`
  - distributable non-essential demo assets for public package consumers
- `Assets/VegetationDemo`
  - local workspace mirror of the vegetation demo assets so repo scenes keep working
- `Assets/Tree/VoxFoliage/GeneratedMeshes`
  - explicit native `.mesh` assets generated by the vegetation shell/trunk bake pipeline for the local demo assets
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
- Editor-baked voxel artifacts must be clipped back to authoritative source bounds; this applies to canopy tier meshes, generated branch wood, and simplified trunk meshes. Temporary voxel hierarchies are bake-internal only and must not persist on `BranchPrototypeSO`.
- Current vegetation runtime authority is the compiled page path: providers register `FoliageAssemblyAsset` / `FoliagePageAsset[]` with `VegetationRenderWorld`, and the render world owns budgets, packet selection, culling, wind, and submission.
- `VegetationRuntimeContainer` runtime registration is compiled-page based. After the container is enabled, transform edits and other registration-affecting `VegetationTreeAuthoring` changes are not live-synced; recompile page assets and run `RefreshRuntimeRegistration()`. The explicit serialized authorings list remains compiler input only.
- Closed `SubScene` runtime loading requires `SubSceneAuthoring` on the same GameObject as `VegetationRuntimeContainer`, and that container must already reference generated compiled foliage assets.
- Runtime is BRG/GPU-buffer based first on supported raw-buffer BRG APIs, with grouped-indirect RenderGraph submission for Direct3D12 and unsupported/faulted BRG setup. Missing compiled page assets, incompatible package shaders, or unsupported draw setup must log and skip/fault-disable instead of throwing inside render callbacks.
- Render-graph raster passes that submit fallback vegetation indirect draws must opt into global-state mutation where needed because the renderer binds `_VegetationInstanceData` through command-buffer global state for D3D12/SRP validation and also through per-draw property blocks, while wind constants remain global/per-draw state instead of per-slot material copies.
- DirectX rejects `Raw | IndirectArguments` graphics-buffer targets for vegetation indirect args. Use `GraphicsBuffer.Target.IndirectArguments` alone for indirect argument buffers.
- DirectX/Vulkan must not diverge on grouped instance-buffer lookup. Keep indirect-args `startInstance` zero and bind `_VegetationInstanceDataBaseOffset` per group through `MaterialPropertyBlock`; shaders index `_VegetationInstanceData` from that explicit base.
- BRG batches must own raw `GraphicsBuffer` instance data with matrix metadata byte addresses aligned to upload element offsets. BRG callbacks must use immutable culling snapshots, callback-local scratch, validated group-local visible instance ranges, cleared `BatchCullingOutput.customCullingResult[0]`, compacted visible instances, and deferred retired-resource disposal. Register `BatchCullingViewType.Light` only when `ShadowMode` is `CheapTree` and the graphics API light-view path is stable. Do not register custom vegetation BRG on Direct3D12 Unity `6000.3.15f1`; use the RenderGraph grouped-indirect backend there.
- DirectX also requires fallback vegetation shadow injection to be a render-graph raster pass that binds URP `mainShadowsTexture` as a `ReadWrite` depth attachment. Do not use unsafe passes or manual `SetRenderTarget` for the main shadow atlas. BRG shadow caster draws must stay renderer-owned.
- URP feature hard constraint: never call `ComputeBuffer.GetData`, `GraphicsBuffer.GetData`, or any blocking GPU-readback wait from render-pass setup, camera/frustum prepare, or submission callbacks. The dense-forest device-removal crash came from hot-path readbacks inside URP-driven repeated preparation.
- URP feature hard constraint: never restore one mutable per-container resident frame across camera and shadow consumers. The render world prepares camera frames per camera/settings and prepares one grouped shadow frame for all valid main-light cascades, then submits that shared frame only into cascades/groups with selected shadow packets.
- GPU admission hard constraint: never reintroduce a single-thread repeated full-tree rescan kernel for nearest-first promotion. URP can prepare the same container multiple times per frame, so O(N^2) acceptance work multiplies into a real runtime failure.
- Recent dense-forest D3D12 failures point to device-removal risk, not just raw local-VRAM exhaustion; investigate retained runtime allocations, indirect-args/state lifetime, signed indirect-args fields such as `BaseVertexLocation`, and shader/buffer index safety before assuming the fix is only lowering visible-instance capacity.
- Budgets are global to `VegetationRenderWorld`, not per container. Multiple active containers/SubScenes register pages into the same provider graph and share one prepared instance/args surface.
- The current scripting toolchain tops out at `LangVersion 9.0`; use block-scoped namespaces, not file-scoped namespaces.
- Use message-bus or singleton when cross communication is needed (prefer message bus with explicit sender and data).
- All static runtime classes must have explicit `Reset` method that is invoked once in the package-owned editor lifecycle hook `VegetationEditorLifecycleReset.cs`.
- Editor lifecycle teardown now lives in the package editor assembly and includes `AssemblyReloadEvents.beforeAssemblyReload` and `EditorApplication.quitting` in addition to play-mode exit, and it resets scene-owned `VegetationRuntimeContainer` providers plus `VegetationRenderWorld` so GPU buffers do not survive Unity recompiles.
- Existing vegetation EditMode bake tests keep bake settings explicit per fixture instead of relying on authoring defaults
- Use Universal Render Pipeline compatible shaders.
- Shader wind is shipped for the compiled render-world path through compiled `FoliageWindMetadata` plus global wind constants. Recompile generated page assets after wind metadata compiler changes.
- Cutover 2 compiled page assets are active classic-scene and SubScene renderer input. Do not add compatibility toggles or runtime bridges around the deleted tree-first path.
