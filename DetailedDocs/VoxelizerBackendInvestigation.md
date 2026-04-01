# Voxelizer Backend Investigation

## Current State (`2026-04-01`)

- Hierarchical canopy baking now uses `CPUVoxelizer.VoxelizeToVolume(...)` inside `Runtime/MeshVoxelizerV1/MeshVoxelizerHierarchyBuilder.cs`.
- Production impostor baking also uses `Runtime/VoxelizerV2/Scripts/CPUVoxelizer.cs` plus `CpuVoxelSurfaceMeshBuilder.cs`.
- `MeshVoxelizerV1` remains in the repo as a compatibility folder and legacy reference, but the live hierarchy occupancy path no longer depends on `MeshVoxelizer` or `MeshRayTracer`.

## Migration Outcome

- `MeshVoxelizerHierarchyBuilder.CreateVoxelLevel(...)` now wraps `CPUVoxelizer.VoxelizeToVolume(...)` for each of the `80/16/6` canopy levels.
- Node-local shell meshes now reuse `CpuVoxelSurfaceMeshBuilder` with per-node bounds ownership.
- The existing hierarchy contract was preserved:
  - `Depth`
  - `FirstChildIndex`
  - `ChildMask`
  - per-node `L0/L1/L2` meshes
- Canopy and impostor baking now share the same indexed CPU voxel backend.

## Recommendation

- Next step should be removing or archiving the obsolete ray-traced voxelizer path after real-asset validation, not doing further optimization work inside `MeshRayTracer` / `MeshVoxelizer`.
- A separate cleanup pass should:
  - remove dead dependencies on `MeshVoxelizer.cs` / `MeshRayTracer.cs` from the production bake flow,
  - decide whether the compatibility folder/namespace should be renamed out of `MeshVoxelizerV1`,
  - keep `MeshVoxelizerHierarchyBuilder` API stability for editor callers unless a deliberate public rename is planned.
