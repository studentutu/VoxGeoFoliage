#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Runtime-side flattened scene tree instance payload.
    /// The urgent path keeps tree-first ownership here and derives promoted branch work from reusable blueprint placements on demand.
    /// </summary>
    public struct VegetationTreeInstanceRuntime
    {
        public VegetationTreeAuthoringRuntime Authoring;
        public Matrix4x4 LocalToWorld;
        public Matrix4x4 WorldToObject;
        public Bounds WorldBounds;
        public Bounds TrunkFullWorldBounds;
        public Bounds TrunkL3WorldBounds;
        public Bounds TreeL3WorldBounds;
        public Bounds ImpostorWorldBounds;
        public Vector3 SphereCenterWorld;
        public float BoundingSphereRadius;
        public int BlueprintIndex;
        public int CellIndex;
        internal VegetationIndirectInstanceData UploadInstanceData;
    }
}
