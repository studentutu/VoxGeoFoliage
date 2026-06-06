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
  - indirect
  - RenderMeshIndirect
  - RenderGraph
  - compute culling
  - SubScene
  - sub scene
  - DOTS
  - baker
  - runtime owner
  - gpu oom
  - device removed
  - base vertex
  - telemetry
  - scene gizmo
  - LOD
  - spatial grid
  - wind
  - custom material
  - shader compatibility
- Read:
  - [Package README](../Packages/com.voxgeofol.vegetation/README.md) - package consumer contract, compiled page asset flow, render-world lifecycle, RenderGraph grouped-indirect submission, settings, and limitations
  - [VegetationRuntimeArchitecture](../DetailedDocs/VegetationRuntimeArchitecture.md) - current runtime architecture reference for compiled providers, render-world ownership, RenderGraph preparation contract, grouped-indirect submission, and shadow flow
  - [VegetationGenerationalRedesign](../DetailedDocs/VegetationGenerationalRedesign.md) - redesign authority for the non-HZB production baseline: one RenderGraph grouped-indirect runtime path with scheduled preparation and remaining work on GPU-kernel preparation
  - [projectrules](projectrules.md) - compact standing rules for runtime ownership, shadow correctness, RenderGraph submission, SubScene providers, and shader contracts
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
