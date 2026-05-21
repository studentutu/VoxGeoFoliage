#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Runtime-readable compiled foliage page with static packet ranges and culling records.
    /// </summary>
    public sealed class FoliagePageAsset : ScriptableObject
    {
        [SerializeField] private string pageId = string.Empty;
        [SerializeField] private Bounds worldBounds;
        [SerializeField] private FoliagePageCell[] cells = Array.Empty<FoliagePageCell>();
        [SerializeField] private FoliagePageTree[] trees = Array.Empty<FoliagePageTree>();
        [SerializeField] private FoliagePacketInstance[] instances = Array.Empty<FoliagePacketInstance>();
        [SerializeField] private FoliageRepresentationPacket[] packets = Array.Empty<FoliageRepresentationPacket>();
        [SerializeField] private FoliageCompilerBuildReport? buildReport;

        public string PageId => pageId;

        public Bounds WorldBounds => worldBounds;

        public IReadOnlyList<FoliagePageCell> Cells => cells;

        public IReadOnlyList<FoliagePageTree> Trees => trees;

        public IReadOnlyList<FoliagePacketInstance> Instances => instances;

        public IReadOnlyList<FoliageRepresentationPacket> Packets => packets;

        public FoliageCompilerBuildReport? BuildReport => buildReport;

        /// <summary>
        /// [INTEGRATION] Replaces one generated page payload with compiler-owned static records.
        /// </summary>
        public void InitializeForCompiler(
            string compiledPageId,
            Bounds compiledWorldBounds,
            FoliagePageCell[] compiledCells,
            FoliagePageTree[] compiledTrees,
            FoliagePacketInstance[] compiledInstances,
            FoliageRepresentationPacket[] compiledPackets,
            FoliageCompilerBuildReport report)
        {
            // Range: accepts one page of prevalidated static data. Condition: instance ranges are already contiguous by asset group. Output: immutable page asset consumed by later cutover runtime code.
            pageId = compiledPageId ?? string.Empty;
            worldBounds = compiledWorldBounds;
            cells = compiledCells ?? Array.Empty<FoliagePageCell>();
            trees = compiledTrees ?? Array.Empty<FoliagePageTree>();
            instances = compiledInstances ?? Array.Empty<FoliagePacketInstance>();
            packets = compiledPackets ?? Array.Empty<FoliageRepresentationPacket>();
            buildReport = report ?? throw new ArgumentNullException(nameof(report));
        }
    }
}
