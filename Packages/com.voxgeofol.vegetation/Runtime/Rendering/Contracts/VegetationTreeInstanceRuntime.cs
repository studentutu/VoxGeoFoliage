#nullable enable

using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Runtime-side flattened scene tree instance payload.
    /// </summary>
    public struct VegetationTreeInstanceRuntime
    {
        public VegetationTreeAuthoring Authoring;
        public Matrix4x4 LocalToWorld;
        public Matrix4x4 WorldToObject;
        public Bounds WorldBounds;
        public Vector3 SphereCenterWorld;
        public float BoundingSphereRadius;
        public int BlueprintIndex;
        public int SceneBranchStartIndex;
        public int SceneBranchCount;
        public int CellIndex;
    }
}
