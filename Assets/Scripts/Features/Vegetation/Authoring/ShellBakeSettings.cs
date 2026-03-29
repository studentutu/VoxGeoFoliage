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
        [SerializeField] [Min(4)] private int voxelResolutionL0 = 32;
        [SerializeField] [Min(4)] private int voxelResolutionL1 = 16;
        [SerializeField] [Min(4)] private int voxelResolutionL2 = 8;
        [SerializeField] [Min(1)] private int targetTrianglesL0 = 2000;
        [SerializeField] [Min(1)] private int targetTrianglesL1 = 500;
        [SerializeField] [Min(1)] private int targetTrianglesL2 = 150;
        [SerializeField] [Range(1f, 180f)] private float smoothNormalAngle = 60f;

        public int VoxelResolutionL0 => voxelResolutionL0;

        public int VoxelResolutionL1 => voxelResolutionL1;

        public int VoxelResolutionL2 => voxelResolutionL2;

        public int TargetTrianglesL0 => targetTrianglesL0;

        public int TargetTrianglesL1 => targetTrianglesL1;

        public int TargetTrianglesL2 => targetTrianglesL2;

        public float SmoothNormalAngle => smoothNormalAngle;
    }
}
