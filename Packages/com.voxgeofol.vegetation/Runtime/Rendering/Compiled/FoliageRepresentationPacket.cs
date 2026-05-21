#nullable enable

using System;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Contiguous static instance range for one compiled representation and asset group.
    /// </summary>
    [Serializable]
    public sealed class FoliageRepresentationPacket
    {
        [SerializeField] private FoliageRepresentationKind representationKind;
        [SerializeField] private int sourceTreeIndex;
        [SerializeField] private int cellIndex;
        [SerializeField] private int assetGroupIndex;
        [SerializeField] private int firstInstance;
        [SerializeField] private int instanceCount;
        [SerializeField] private Bounds worldBounds;
        [SerializeField] private int workCost;
        [SerializeField] private FoliagePacketResidency residency;
        [SerializeField] private FoliageShadowPacketMode shadowMode;
        [SerializeField] private int shadowPacketIndex;
        [SerializeField] private string debugLabel = string.Empty;

        public FoliageRepresentationPacket(
            FoliageRepresentationKind representationKind,
            int sourceTreeIndex,
            int cellIndex,
            int assetGroupIndex,
            int firstInstance,
            int instanceCount,
            Bounds worldBounds,
            int workCost,
            FoliagePacketResidency residency,
            FoliageShadowPacketMode shadowMode,
            int shadowPacketIndex,
            string debugLabel)
        {
            this.representationKind = representationKind;
            this.sourceTreeIndex = sourceTreeIndex;
            this.cellIndex = cellIndex;
            this.assetGroupIndex = assetGroupIndex;
            this.firstInstance = firstInstance;
            this.instanceCount = Mathf.Max(0, instanceCount);
            this.worldBounds = worldBounds;
            this.workCost = Mathf.Max(1, workCost);
            this.residency = residency;
            this.shadowMode = shadowMode;
            this.shadowPacketIndex = shadowPacketIndex;
            this.debugLabel = debugLabel ?? string.Empty;
        }

        public FoliageRepresentationKind RepresentationKind => representationKind;

        public int SourceTreeIndex => sourceTreeIndex;

        public int CellIndex => cellIndex;

        public int AssetGroupIndex => assetGroupIndex;

        public int FirstInstance => firstInstance;

        public int InstanceCount => instanceCount;

        public Bounds WorldBounds => worldBounds;

        public int WorkCost => workCost;

        public FoliagePacketResidency Residency => residency;

        public FoliageShadowPacketMode ShadowMode => shadowMode;

        public int ShadowPacketIndex => shadowPacketIndex;

        public string DebugLabel => debugLabel;
    }
}
