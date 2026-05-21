#nullable enable

using System;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Static instance payload for one compiled packet draw.
    /// </summary>
    [Serializable]
    public struct FoliagePacketInstance
    {
        [SerializeField] private int sourceTreeIndex;
        [SerializeField] private int assetGroupIndex;
        [SerializeField] private Matrix4x4 objectToWorld;
        [SerializeField] private Matrix4x4 worldToObject;
        [SerializeField] private Bounds worldBounds;
        [SerializeField] private int packedLeafTint;
        [SerializeField] private FoliageWindMetadata windMetadata;

        public FoliagePacketInstance(
            int sourceTreeIndex,
            int assetGroupIndex,
            Matrix4x4 objectToWorld,
            Matrix4x4 worldToObject,
            Bounds worldBounds,
            uint packedLeafTint,
            FoliageWindMetadata windMetadata)
        {
            this.sourceTreeIndex = sourceTreeIndex;
            this.assetGroupIndex = assetGroupIndex;
            this.objectToWorld = objectToWorld;
            this.worldToObject = worldToObject;
            this.worldBounds = worldBounds;
            this.packedLeafTint = unchecked((int)packedLeafTint);
            this.windMetadata = windMetadata;
        }

        public int SourceTreeIndex => sourceTreeIndex;

        public int AssetGroupIndex => assetGroupIndex;

        public Matrix4x4 ObjectToWorld => objectToWorld;

        public Matrix4x4 WorldToObject => worldToObject;

        public Bounds WorldBounds => worldBounds;

        public uint PackedLeafTint => unchecked((uint)packedLeafTint);

        public FoliageWindMetadata WindMetadata => windMetadata;
    }
}
