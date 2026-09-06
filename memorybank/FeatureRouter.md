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
  - [VegetationGenerationalRedesign](../DetailedDocs/VegetationGenerationalRedesign.md) - whole-system redesign proposal for the non-HZB production baseline and hard replacement migration: editor-compiled representation packets, compiled pages, global render world, page/cell HLOD, active budgets, CullingGroup page/cell broad phase, CheapTree shadow packets, shader wind, grouped indirect submission, authoring-time static data compilation, no maintained old/new renderer split, no production `TreeL3` floor, no production `ShadowProxyL0/L1`, and scalable 100k to 1M streamed forests
  - [fixShadows](../DetailedDocs/fixShadows.md) - shadow-specific current-design review and proposed `ShadowMode.CheapTree` redesign with cascade-0 self-shadowing and 5 m offscreen caster ring
  - [projectrules](projectrules.md) - current SubScene provider/runtime ownership rules until a dedicated SubScene doc exists
  - [Milestone2](../DetailedDocs/Milestone2.md) - current milestone status and open work only
  - [Milestone1](../DetailedDocs/Milestone1.md) - shipped baseline summary only

## CI and Tests

- Triggers:
  - tests
  - ci
  - kiss-unity-mcp
  - tooling setup
- Read:
  - [Technical context verification flow](techContext.md#compilation--verification-flow) - installed tooling, configuration, and first-import requirement.
  - Build entry points are defined in `.vscode/tasks.json`.
  - Use kiss-unity-mcp for CI/Compilation/Verification/Shaders-compilation/Tests.


## Scene Placement Utilities

- Triggers:
  - mass placement
  - placement
  - scatter
- Read:
  - [MassPlacement](../Assets/Scripts/MassPlacement/MassPlacement.cs) - editor-only downward-raycast placement utility
