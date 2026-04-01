#nullable enable

using System;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Authoring
{
    /// <summary>
    /// Editor-time shell baking settings for one branch prototype.
    /// </summary>
    [Serializable]
    public sealed class ShellBakeSettings
    {
        [SerializeField] [Min(1)] private int maxOctreeDepth = 4;
        [Tooltip("Highest detail shell resolution. Defaults to the verified 80 subdivision level.")]
        [SerializeField] [Min(4)] private int voxelResolutionL0 = 80;
        [Tooltip("Mid detail shell resolution. Defaults to the verified 16 subdivision level.")]
        [SerializeField] [Min(4)] private int voxelResolutionL1 = 16;
        [Tooltip("Lowest detail shell resolution. Defaults to the verified 10 subdivision level.")]
        [SerializeField] [Min(4)] private int voxelResolutionL2 = 10;
        [SerializeField] [Min(1)] private int minimumSurfaceVoxelCountToSplit = 8;

        public int MaxOctreeDepth => maxOctreeDepth;

        public int VoxelResolutionL0 => voxelResolutionL0;

        public int VoxelResolutionL1 => voxelResolutionL1;

        public int VoxelResolutionL2 => voxelResolutionL2;

        public int MinimumSurfaceVoxelCountToSplit => minimumSurfaceVoxelCountToSplit;
    }
}
