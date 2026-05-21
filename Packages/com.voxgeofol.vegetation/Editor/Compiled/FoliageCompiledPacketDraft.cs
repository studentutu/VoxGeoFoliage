#nullable enable

using UnityEngine;
using VoxGeoFol.Features.Vegetation.Rendering;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Compiler-local packet before static instances are sorted into contiguous asset-group ranges.
    /// </summary>
    internal sealed class FoliageCompiledPacketDraft
    {
        public FoliageCompiledPacketDraft(
            FoliageRepresentationKind representationKind,
            int sourceTreeIndex,
            int cellIndex,
            int assetGroupIndex,
            FoliagePacketInstance instance,
            Bounds worldBounds,
            int workCost,
            FoliagePacketResidency residency,
            FoliageShadowPacketMode shadowMode,
            string debugLabel)
        {
            RepresentationKind = representationKind;
            SourceTreeIndex = sourceTreeIndex;
            CellIndex = cellIndex;
            AssetGroupIndex = assetGroupIndex;
            Instance = instance;
            WorldBounds = worldBounds;
            WorkCost = Mathf.Max(1, workCost);
            Residency = residency;
            ShadowMode = shadowMode;
            DebugLabel = debugLabel;
        }

        public FoliageRepresentationKind RepresentationKind { get; }

        public int SourceTreeIndex { get; }

        public int CellIndex { get; }

        public int AssetGroupIndex { get; }

        public FoliagePacketInstance Instance { get; }

        public Bounds WorldBounds { get; }

        public int WorkCost { get; }

        public FoliagePacketResidency Residency { get; }

        public FoliageShadowPacketMode ShadowMode { get; }

        public string DebugLabel { get; }
    }
}
