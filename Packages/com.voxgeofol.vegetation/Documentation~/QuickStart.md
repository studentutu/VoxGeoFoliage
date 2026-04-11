# Quick Start

## Package Layout

- `Runtime/Authoring` contains the public runtime-side authoring API.
- `Editor` contains the editor-only baking and asset refresh tooling.
- `Tests/Editor` contains the package EditMode tests.
- `Samples~/VegetationDemo` contains all non-essential demo content for distribution.

## Importing the Demo

Import `VegetationDemo` from Package Manager to copy the sample assets into your project's `Assets/Samples/...` area.

## Generated Assets

The bake pipeline persists generated `.mesh` assets as standalone files:

- beside the owning asset under `GeneratedMeshes/` when the owner is inside `Assets/`
- under `Assets/VoxGeoFol.Generated/Vegetation/Meshes/` when the owner asset lives inside `Packages/`

This keeps the package safe for embedded, local, or registry-based installs.
