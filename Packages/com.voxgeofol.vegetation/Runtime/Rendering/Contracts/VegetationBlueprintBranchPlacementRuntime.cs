#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Runtime-side flattened asset placement payload for one tree blueprint branch.
    /// </summary>
    public struct VegetationBlueprintBranchPlacementRuntime
    {
        public Matrix4x4 LocalToTree;
        public Matrix4x4 TreeToLocal;
        public int PrototypeIndex;
        public Vector3 LocalBoundsCenter;
        public Vector3 LocalBoundsExtents;
        public float BoundingSphereRadius;
    }
}
