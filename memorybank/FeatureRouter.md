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
  - [Package README](../Packages/com.voxgeofol.vegetation/README.md) - package consumer contract, compiled page asset flow, render-world lifecycle, grouped packet submission, settings, and limitations
  - [VegetationRuntimeArchitecture](../DetailedDocs/VegetationRuntimeArchitecture.md) - current runtime architecture reference for compiled providers, render-world ownership, packet submission, and shadow flow
  - [VegetationGenerationalRedesign](../DetailedDocs/VegetationGenerationalRedesign.md) - current hard-cutover authority for the non-HZB production baseline. Classic-scene and SubScene providers now flow through `VegetationRenderWorld`; remaining work is procedural providers, screen-error LOD, externalized async payload providers, and production validation
  - [fixShadows](../DetailedDocs/fixShadows.md) - shadow-specific notes; treat the render-world `CheapTree` packet path as the current production direction
  - [projectrules](projectrules.md) - current SubScene provider/runtime ownership rules until a dedicated SubScene doc exists
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
