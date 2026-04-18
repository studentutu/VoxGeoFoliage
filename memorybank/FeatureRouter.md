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
  - impostor
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
  - classification
  - classify debug
  - debug classify
  - gpu oom
  - device removed
  - d3d12
  - base vertex
  - impostor crash
  - telemetry
  - scene gizmo
  - LOD
  - spatial grid
  - wind
  - custom material
  - shader compatibility
- Read:
  - [Package README](../Packages/com.voxgeofol.vegetation/README.md) - package consumer contract, current tree-first runtime terminology, and lifecycle summary from container input to URP indirect submission
  - [VegetationRuntimeArchitecture](../DetailedDocs/VegetationRuntimeArchitecture.md) - exact bake, registration, color/depth, and shadow ASCII pipelines with payload ownership, current-code runtime review, and replacement runtime architecture authority
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
