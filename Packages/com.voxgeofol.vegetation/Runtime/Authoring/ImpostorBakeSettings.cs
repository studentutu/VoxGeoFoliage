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

        public int VoxelResolution => voxelResolution;
    }
}
