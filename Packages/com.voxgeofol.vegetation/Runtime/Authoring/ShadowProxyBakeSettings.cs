#nullable enable

using System;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Authoring
{
    /// <summary>
    /// Editor-time tree-level shadow proxy baking settings for one tree blueprint.
    /// </summary>
    [Serializable]
    public sealed class ShadowProxyBakeSettings
    {
        [SerializeField] [Min(2)] private int voxelResolutionL0 = 8;
        [SerializeField] [Min(2)] private int voxelResolutionL1 = 6;
        [SerializeField] private bool skipReduction;
        [SerializeField] private bool skipSimplifyFallback;

        public int VoxelResolutionL0 => voxelResolutionL0;

        public int VoxelResolutionL1 => voxelResolutionL1;

        public bool SkipReduction => skipReduction;

        public bool SkipSimplifyFallback => skipSimplifyFallback;
    }
}
