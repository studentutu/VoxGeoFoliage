# Feature Router

Purpose: always-read routing index. Use this after the compact top-level memory-bank docs to decide which authoritative feature docs must be opened before planning or editing.

## Routing Table

## Vegetation System

- Triggers:
  - vegetation
  - tree
  - branch
  - shell
  - canopy
  - far mesh
  - foliage
  - BRG
  - indirect
  - RenderMeshIndirect
  - SubScene
  - sub scene
  - DOTS
  - baker
  - runtime owner
  - gpu oom
  - device removed
  - d3d12
  - base vertex
  - telemetry
  - scene gizmo
  - LOD
  - spatial grid
  - wind
  - custom material
  - shader compatibility
- Read:
  - [Package README](../Packages/com.voxgeofol.vegetation/README.md) - package consumer contract, compiled page asset flow, render-world lifecycle, BRG packet submission, settings, and limitations
  - [VegetationRuntimeArchitecture](../DetailedDocs/VegetationRuntimeArchitecture.md) - current runtime architecture reference for compiled providers, render-world ownership, BRG packet submission, fallback grouped-indirect submission, and shadow flow
  - [VegetationGenerationalRedesign](../DetailedDocs/VegetationGenerationalRedesign.md) - redesign authority for the non-HZB production baseline: BRG on supported raw-buffer APIs, RenderGraph grouped-indirect on Direct3D12 or fallback setup, with remaining work on D3D12 RenderGraph performance and jobified BRG culling
  - [projectrules](projectrules.md) - compact standing rules for runtime ownership, shadow correctness, D3D12 fallback, SubScene providers, and shader contracts
  - [Milestone2](../DetailedDocs/Milestone2.md) - current milestone status and open work only
  - [Milestone1](../DetailedDocs/Milestone1.md) - shipped baseline summary only

## CI and Tests

- Triggers:
  - tests
  - ci
- Read:
  - [RunUnityTestsReadme](../CI/RunUnityTestsReadme.md)

## Scene Placement Utilities

- Triggers:
  - mass placement
  - placement
  - scatter
- Read:
  - [MassPlacement](../Assets/Scripts/MassPlacement/MassPlacement.cs) - editor-only downward-raycast placement utility
