#nullable enable

using System;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Authoring
{
    /// <summary>
    /// Editor-time impostor baking settings for one tree blueprint.
    /// </summary>
    [Serializable]
    public sealed class ImpostorBakeSettings
    {
        [SerializeField] [Min(2)] private int voxelResolution = 4;
        [SerializeField] private bool skipReduction;
        [SerializeField] private bool skipSimplifyFallback;

        public int VoxelResolution => voxelResolution;

        public bool SkipReduction => skipReduction;

        public bool SkipSimplifyFallback => skipSimplifyFallback;
    }
}
