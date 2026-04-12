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
  - scene gizmo
  - LOD
  - spatial grid
  - wind
  - custom material
  - shader compatibility
- Read:
  - [UnityAssembledVegetation_FULL](../DetailedDocs/UnityAssembledVegetation_FULL.md) - architecture authority
  - [AddSubSceneSupport](../DetailedDocs/AddSubSceneSupport.md) - runtime-only SubScene registration bridge authority
  - [urgentRedesign](../DetailedDocs/urgentRedesign.md) - urgent runtime prioritization and dense-forest overflow redesign authority
  - [Milestone2](../DetailedDocs/Milestone2.md) - current milestone authority for hierarchical wind, custom-material compatibility, and production improvements
  - [Milestone1](../DetailedDocs/Milestone1.md) - finished MVP baseline and completion record

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
