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
        public int WoodDrawSlotL0;
        public int WoodDrawSlotL1;
        public int WoodDrawSlotL2;
        public int WoodDrawSlotL3;
        public int FoliageDrawSlotL0;
        public Matrix4x4 LocalToWorld;
        public Vector3 SphereCenterWorld;
        public float BoundingSphereRadius;
        internal VegetationIndirectInstanceData WoodUploadInstanceData;
        internal VegetationIndirectInstanceData FoliageUploadInstanceData;
    }
}
