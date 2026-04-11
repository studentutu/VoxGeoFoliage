# Unity Foliage Assembly And Voxel-Based Rendering

![UnityPreview](PreviewForReadme.png)

## Overview

This repository hosts the authoritative vegetation package, repo-local docs and scenes, and the agentic development workflow around them outside of Unity Editor.

The feature goal is a Unity-side foliage-assembly workflow inspired by reusable branch modules, explicit voxel-derived shell tiers, opaque far meshes, and GPU-driven indirect rendering instead of masked foliage cards.

Inspired by Unreal Engine 5.7 foliage innovations (Assemblies, voxelized LOD, and hierarchical wind animation).

## Start Here

- Package readme: [Packages/com.voxgeofol.vegetation/README.md](Packages/com.voxgeofol.vegetation/README.md)
- Architecture authority: [DetailedDocs/UnityAssembledVegetation_FULL.md](DetailedDocs/UnityAssembledVegetation_FULL.md)
- Embedded package: [Packages/com.voxgeofol.vegetation](Packages/com.voxgeofol.vegetation)
- Playground scene: [Assets/Scenes/Playground.unity](Assets/Scenes/Playground.unity)
- Repo-local sample mirror: [Assets/Tree](Assets/VegetationDemo)
- demo terrain with mass-placement, see scene [BigPineForest.unity](Assets/Scenes/PineForest/BigPineForest.unity)


## What is Foliage Assembly and voxel-based rendering?

See Unreal 5.7 foliage assemblies (Nanite vegetation):
- Witcher 4 presentation [Presentation](https://youtu.be/EdNkm0ezP0o?si=YYlytLYKuexVYUOT) 
- Procedural Vegetation Editor/ Nanite vegetation (Nanite Foliage) [OfficialDocs](https://dev.epicgames.com/documentation/en-us/unreal-engine/nanite-foliage)

Short summary:
- No masked / alpha-tested materials, fully opaque based rendering (fully avoid transparency, as it breaks tile-based rendering on mobiles)
- each foliage consists of trunk + branches + leaves (Branch-based assembly with reusable modules)
- reuse branch modules across single tree to minimize memory footprint of the used meshes
- use canopy shells (voxelized mesh) for each of the level of detail.
- each branch consists of multi-level hierarchy of the canopy shells (includes leaves into a voxelized form), thus evenly preserved hight quality in the near and heavy minimize the level of details of obscured (behind the trunk) branches.
- last level of detail (imposter) is fully opaque based on the minimum requirements
- Nanite opaque rendering if very fast if no shader movement exists, thus wind is animated per branch bone (wind is animation, compute shader based)

### Limitation of Unity

We can't make it one-to-one right now, but we still have options:
- Unity doesn't have Nanite alternative, closest to it is an experimental virtual mesh package [VirtualMeshPackage](https://github.com/Unity-Technologies/com.unity.virtualmesh)
- Unity SRP (URP) does support similar mechanism to the assemblies, which is custom render batch by Indirect draw within a custom feature/render pass.
- Reduced geometry at all levels in SRP (custom lods) needs to be explicit for the manual render batch approach
- to keep SRP-friendly batching and allow variation we can use Unity API Renderer Shader User Value (RSUV) which is a tightly pack `uint` that is manually unpacked in shader for any form of variation for the instances.

## Feature Snapshot

- One `VegetationTreeAuthoring` references one `TreeBlueprintSO`.
  - meaning that grass can be a single instance of vegetation authority with multiple different branches (e.g. actual instances of the foliage grass blades or flowers).
- One `TreeBlueprintSO` contains trunk and far-mesh (geometry-imposter) data plus `BranchPlacement[]`.
- One `BranchPlacement` references one `BranchPrototypeSO`.
- Many authorings can share one blueprint, and one blueprint can mix different prototypes or reuse the same prototype many times.
- Runtime batching is by draw slot `mesh + material + material kind`, not by tree count or blueprint identity.
- Runtime is container-scoped, snapshot-based, GPU-resident, URP-only, and opaque-only.

For the full support matrix, batching rules, setup, settings, runtime pipeline, and constraints, use the package README above as the source of truth.

## Agentic Workflow

- Read [AGENTS.md](AGENTS.md) before repo work.
- For feature work, read [memorybank/techContext.md](memorybank/techContext.md), [memorybank/projectrules.md](memorybank/projectrules.md), and [memorybank/FeatureRouter.md](memorybank/FeatureRouter.md), then follow the routed detailed docs.
- Ask to `read memory bank` before deep repo work and `update memory bank` when a task finishes or documentation/state changes.
- Fast compile: `./rebuildSolutionWithRiderMsBuild.sh`
- Full Unity compile and solution refresh: `./rebuildSolutionFromUnityItself.sh`
- Run Unity tests: `./runTestsFromRoot.sh`
- Parse Unity test output: `./runParsetests.sh`
- If Unity, Rider, or Git Bash are installed in different locations, update the hardcoded paths in those scripts and in [.vscode/tasks.json](.vscode/tasks.json).

## What to change in order to get full agentic workflow

- CI/Tests/Compilation
  - change versions and path to Unity Editor and Rider in: [rebuildSolutionFromUnityItself](./rebuildSolutionFromUnityItself.sh), [parseTestErrors](./parseTestErrors.sh), [rebuildSolutionWithRiderMsBuild](./rebuildSolutionWithRiderMsBuild.sh), [runTestsBash](./runTestsBash.sh)
  - change solution for quick MSBuild (take solution that is generated by Unity Editor) change solution field 'SOLUTION_UNIX="$PROJECT_PATH/LightECS.sln"' in [rebuildSolutionWithRiderMsBuild](./rebuildSolutionWithRiderMsBuild.sh)
- VScode tasks use system git bash path: "C:\\Program Files\\Git\\bin\\bash.exe" (windows path, properly escaped)

## Repo Requirements

- Unity Hub with a Unity `6000.3+` editor installation.
- Git and Git Bash.
- Rider or an equivalent MSBuild-capable setup.
- VS Code is optional, but the repo task wrappers are configured there.

## Verification

- Fast compile output: `CI/RiderMsBuild.log`
- Unity compile output: `CI/CompileErrorsAfterUnityRun.txt`
- Unity test output: `CI/CITestOutput.xml`
- Unity test log: `CI/UnityLogs.log`

## License

- Root repository license: [LICENSE](LICENSE)
- Package license: [Packages/com.voxgeofol.vegetation/LICENSE.md](Packages/com.voxgeofol.vegetation/LICENSE.md)
