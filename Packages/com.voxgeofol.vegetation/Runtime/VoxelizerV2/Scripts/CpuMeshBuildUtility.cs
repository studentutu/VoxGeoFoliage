using System.Collections.Generic;
using UnityEngine;

namespace VoxelSystem
{
    public static class CpuMeshBuildUtility
    {
        /// <summary>
        ///     CPU based mesh voxelization.
        ///     Important: not saved anywhere, so if you need persistence, make sure to save it.
        /// </summary>
        /// <param name="fromMesh">From mesh.</param>
        /// <param name="resolution">Smaller resolution means bigger voxels.</param>
        /// <returns>Voxelized mesh</returns>
        public static Mesh BuildVoxelizedMesh(Mesh fromMesh, int resolution = 24)
        {
            List<Voxel_t> voxels;
            float unit;
            CPUVoxelizer.Voxelize(fromMesh, resolution, out voxels, out unit);

            return VoxelMesh.Build(voxels.ToArray(), unit, false);
        }
    }
}