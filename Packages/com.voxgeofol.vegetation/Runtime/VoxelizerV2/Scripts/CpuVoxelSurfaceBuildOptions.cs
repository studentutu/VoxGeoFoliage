#nullable enable

namespace VoxelSystem
{
    /// <summary>
    /// Options controlling how CPU voxel surface meshes are generated.
    /// </summary>
    public readonly struct CpuVoxelSurfaceBuildOptions
    {
        public CpuVoxelSurfaceBuildOptions(bool reduceCoplanarFaces)
        {
            ReduceCoplanarFaces = reduceCoplanarFaces;
        }

        public bool ReduceCoplanarFaces { get; }

        public static CpuVoxelSurfaceBuildOptions Reduced => new CpuVoxelSurfaceBuildOptions(true);

        public static CpuVoxelSurfaceBuildOptions Raw => new CpuVoxelSurfaceBuildOptions(false);
    }
}
