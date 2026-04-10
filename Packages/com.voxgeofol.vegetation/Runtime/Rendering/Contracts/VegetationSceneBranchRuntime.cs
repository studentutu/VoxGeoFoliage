#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Runtime-side flattened scene branch instance payload.
    /// </summary>
    public struct VegetationSceneBranchRuntime
    {
        public int TreeIndex;
        public int BranchPlacementIndex;
        public int PrototypeIndex;
        public Matrix4x4 LocalToWorld;
        public Matrix4x4 WorldToObject;
        public Bounds WorldBounds;
        public Vector3 SphereCenterWorld;
        public float BoundingSphereRadius;
        public int DecisionStartL1;
        public int DecisionCountL1;
        public int DecisionStartL2;
        public int DecisionCountL2;
        public int DecisionStartL3;
        public int DecisionCountL3;
    }
}
