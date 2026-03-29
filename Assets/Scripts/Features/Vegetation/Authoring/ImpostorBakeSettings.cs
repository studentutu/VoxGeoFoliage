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
        [SerializeField] [Min(1)] private int targetTriangles = 200;
        [SerializeField] [Min(0.0001f)] private float weldThreshold = 0.01f;

        public int TargetTriangles => targetTriangles;

        public float WeldThreshold => weldThreshold;
    }
}
