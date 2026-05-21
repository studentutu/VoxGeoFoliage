#nullable enable

using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;
using VoxGeoFol.Features.Vegetation.Rendering;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Compiler-local tree record before page asset serialization.
    /// </summary>
    internal sealed class FoliageCompiledTreeRecord
    {
        public FoliageCompiledTreeRecord(
            int sourceTreeIndex,
            int blueprintIndex,
            TreeBlueprintSO blueprint,
            string stableTreeId,
            string debugName,
            Matrix4x4 localToWorld,
            Matrix4x4 worldToObject,
            Bounds worldBounds,
            int cellX,
            int cellY,
            int cellZ)
        {
            SourceTreeIndex = sourceTreeIndex;
            BlueprintIndex = blueprintIndex;
            Blueprint = blueprint;
            StableTreeId = stableTreeId;
            DebugName = debugName;
            LocalToWorld = localToWorld;
            WorldToObject = worldToObject;
            WorldBounds = worldBounds;
            CellX = cellX;
            CellY = cellY;
            CellZ = cellZ;
        }

        public int SourceTreeIndex { get; }

        public int BlueprintIndex { get; }

        public TreeBlueprintSO Blueprint { get; }

        public string StableTreeId { get; }

        public string DebugName { get; }

        public Matrix4x4 LocalToWorld { get; }

        public Matrix4x4 WorldToObject { get; }

        public Bounds WorldBounds { get; }

        public int CellX { get; }

        public int CellY { get; }

        public int CellZ { get; }

        public int PageCellIndex { get; set; }

        public FoliagePageTree ToPageTree()
        {
            return new FoliagePageTree(
                SourceTreeIndex,
                BlueprintIndex,
                PageCellIndex,
                StableTreeId,
                DebugName,
                LocalToWorld,
                WorldToObject,
                WorldBounds,
                WorldBounds.center,
                WorldBounds.extents.magnitude);
        }
    }
}
