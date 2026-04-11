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
        public uint PackedLeafTint;
        public Matrix4x4 LocalToWorld;
        public Matrix4x4 WorldToObject;
        public Bounds WorldBounds;
        public Bounds WoodWorldBoundsL0;
        public Bounds WoodWorldBoundsL1;
        public Bounds WoodWorldBoundsL2;
        public Bounds WoodWorldBoundsL3;
        public Bounds FoliageWorldBoundsL0;
        public Bounds ShellWorldBoundsL1;
        public Bounds ShellWorldBoundsL2;
        public Bounds ShellWorldBoundsL3;
        public Vector3 SphereCenterWorld;
        public float BoundingSphereRadius;
        public int DecisionStartL1;
        public int DecisionCountL1;
        public int DecisionStartL2;
        public int DecisionCountL2;
        public int DecisionStartL3;
        public int DecisionCountL3;
        internal VegetationIndirectInstanceData WoodUploadInstanceData;
        internal VegetationIndirectInstanceData FoliageUploadInstanceData;
    }
}
