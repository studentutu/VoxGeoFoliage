# Quick Start

## Package Layout

- `Runtime/Authoring` contains the public runtime-side authoring API.
- `Editor` contains the editor-only baking and asset refresh tooling.
- `Tests/Editor` contains the package EditMode tests.
- `Samples~/Vegetation Demo` contains all non-essential demo content for distribution.

## Importing the Demo

Import `Vegetation Demo` from Package Manager to copy the sample assets into your project's `Assets/Samples/...` area.

The repo still keeps a working copy of the demo assets under `Assets/Tree/` so the local scenes continue to function, but the distributable public source of those assets is now the package sample.

## Generated Assets

The bake pipeline persists generated `.mesh` assets as standalone files:

- beside the owning asset under `GeneratedMeshes/` when the owner is inside `Assets/`
- under `Assets/VoxGeoFol.Generated/Vegetation/Meshes/` when the owner asset lives inside `Packages/`

This keeps the package safe for embedded, local, or registry-based installs.
