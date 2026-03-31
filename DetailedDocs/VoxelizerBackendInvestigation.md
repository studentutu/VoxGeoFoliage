# Voxelizer Backend Investigation

## Current State (`2026-04-01`)

- Hierarchical canopy baking still uses `Runtime/MeshVoxelizerV1/MeshVoxelizerHierarchyBuilder.cs`.
- Production impostor baking now uses `Runtime/VoxelizerV2/Scripts/CPUVoxelizer.cs` plus `CpuVoxelSurfaceMeshBuilder.cs`.
- `MeshVoxelizerV1` stays in the repo as the current canopy path and rollback reference.

## Hot Path Review

- `MeshVoxelizerHierarchyBuilder.CreateVoxelLevel(...)` constructs a fresh `MeshVoxelizer` for each of the `16/12/8` resolutions.
- Each `MeshVoxelizer.Voxelize(...)` constructs a fresh `MeshRayTracer`, builds its AABB tree, and then traces every `x/y` column through the mesh along `+z`.
- The new impostor path uses `CPUVoxelizer.VoxelizeToVolume(...)`, which:
  - iterates only the candidate voxel range inside each triangle AABB,
  - uses the existing SAT triangle-vs-AABB overlap test,
  - fills the interior once through a front/back sweep,
  - exposes indexed occupancy so downstream mesh builders do not need to rerun voxelization.

## Recommendation

- Next step should be swapping hierarchy occupancy generation to the CPU volume backend, not optimizing `MeshRayTracer` / `MeshVoxelizer` first.
- The hierarchy baker needs reusable indexed occupancy for per-node ownership and mesh extraction more than it needs the current ray-traced implementation details.
- The new CPU volume API already matches that need:
  - one voxelization pass per resolution,
  - direct occupancy lookup by `x/y/z`,
  - direct reuse by future node-local surface extraction,
  - no dependency on rebuilding a BVH for every canopy bake level.

## Follow-Up Shape

- next canopy rewrite:
  - replace `CreateVoxelLevel(...)` with `CPUVoxelizer.VoxelizeToVolume(...)`,
  - keep the existing node split contract (`Depth`, `FirstChildIndex`, `ChildMask`, per-node `L0/L1/L2` meshes),
  - reuse `CpuVoxelSurfaceMeshBuilder` for node-local shell surfaces,
  - remove the V1 canopy path.
