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
        [SerializeField] [Min(4)] private int voxelResolutionL0 = 16;
        [SerializeField] [Min(4)] private int voxelResolutionL1 = 12;
        [SerializeField] [Min(4)] private int voxelResolutionL2 = 8;
        [SerializeField] [Min(1)] private int minimumSurfaceVoxelCountToSplit = 4;

        public int MaxOctreeDepth => maxOctreeDepth;

        public int VoxelResolutionL0 => voxelResolutionL0;

        public int VoxelResolutionL1 => voxelResolutionL1;

        public int VoxelResolutionL2 => voxelResolutionL2;

        public int MinimumSurfaceVoxelCountToSplit => minimumSurfaceVoxelCountToSplit;
    }
}
