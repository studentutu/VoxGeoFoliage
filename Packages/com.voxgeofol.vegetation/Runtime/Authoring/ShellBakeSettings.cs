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
        [Tooltip("Mid detail shell resolution. Defaults to the verified 30 subdivision level.")]
        [SerializeField] [Min(4)] private int voxelResolutionL1 = 30;
        [Tooltip("Lowest detail shell resolution. Defaults to the verified 20 subdivision level.")]
        [SerializeField] [Min(4)] private int voxelResolutionL2 = 20;
        [Tooltip("Generated wood resolution for the R1 shell preview attachment.")]
        [SerializeField] [Min(2)] private int woodVoxelResolutionL1 = 50;
        [Tooltip("Generated wood resolution for the R2 shell preview attachment.")]
        [SerializeField] [Min(2)] private int woodVoxelResolutionL2 = 20;
        [SerializeField] [Min(1)] private int minimumSurfaceVoxelCountToSplit = 8;
        [SerializeField] private bool skipReduction;
        [SerializeField] private bool skipL0Reduction;
        [SerializeField] private bool skipSimplifyFallback;

        public int MaxOctreeDepth => maxOctreeDepth;

        public int VoxelResolutionL0 => voxelResolutionL0;

        public int VoxelResolutionL1 => voxelResolutionL1;

        public int VoxelResolutionL2 => voxelResolutionL2;

        public int WoodVoxelResolutionL1 => woodVoxelResolutionL1;

        public int WoodVoxelResolutionL2 => woodVoxelResolutionL2;

        public int MinimumSurfaceVoxelCountToSplit => minimumSurfaceVoxelCountToSplit;

        public bool SkipReduction => skipReduction;

        public bool SkipL0Reduction => skipL0Reduction;

        public bool SkipSimplifyFallback => skipSimplifyFallback;
    }
}
