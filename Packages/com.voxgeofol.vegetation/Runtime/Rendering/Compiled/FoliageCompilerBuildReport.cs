#nullable enable

using System;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Deterministic compiler output summary used to audit budgets before runtime.
    /// </summary>
    [Serializable]
    public sealed class FoliageCompilerBuildReport
    {
        [SerializeField] private int pageCount;
        [SerializeField] private int cellCount;
        [SerializeField] private int treeCount;
        [SerializeField] private int packetCount;
        [SerializeField] private int assetGroupCount;
        [SerializeField] private int staticInstanceCount;
        [SerializeField] private long alwaysResidentBytes;
        [SerializeField] private long nearDetailColdBytes;
        [SerializeField] private long maxPageBytes;
        [SerializeField] private long maxCellBytes;
        [SerializeField] private int colorCommandUpperBound;
        [SerializeField] private int shadowCommandUpperBound;
        [SerializeField] private int maxPacketCountPerPage;
        [SerializeField] private int maxPacketCountPerCell;
        [SerializeField] private int pageSplitCount;
        [SerializeField] private int validationFailureCount;
        [SerializeField] private string[] messages = Array.Empty<string>();

        public FoliageCompilerBuildReport(
            int pageCount,
            int cellCount,
            int treeCount,
            int packetCount,
            int assetGroupCount,
            int staticInstanceCount,
            long alwaysResidentBytes,
            long nearDetailColdBytes,
            long maxPageBytes,
            long maxCellBytes,
            int colorCommandUpperBound,
            int shadowCommandUpperBound,
            int maxPacketCountPerPage,
            int maxPacketCountPerCell,
            int pageSplitCount,
            int validationFailureCount,
            string[] messages)
        {
            this.pageCount = Mathf.Max(0, pageCount);
            this.cellCount = Mathf.Max(0, cellCount);
            this.treeCount = Mathf.Max(0, treeCount);
            this.packetCount = Mathf.Max(0, packetCount);
            this.assetGroupCount = Mathf.Max(0, assetGroupCount);
            this.staticInstanceCount = Mathf.Max(0, staticInstanceCount);
            this.alwaysResidentBytes = Math.Max(0L, alwaysResidentBytes);
            this.nearDetailColdBytes = Math.Max(0L, nearDetailColdBytes);
            this.maxPageBytes = Math.Max(0L, maxPageBytes);
            this.maxCellBytes = Math.Max(0L, maxCellBytes);
            this.colorCommandUpperBound = Mathf.Max(0, colorCommandUpperBound);
            this.shadowCommandUpperBound = Mathf.Max(0, shadowCommandUpperBound);
            this.maxPacketCountPerPage = Mathf.Max(0, maxPacketCountPerPage);
            this.maxPacketCountPerCell = Mathf.Max(0, maxPacketCountPerCell);
            this.pageSplitCount = Mathf.Max(0, pageSplitCount);
            this.validationFailureCount = Mathf.Max(0, validationFailureCount);
            this.messages = messages ?? Array.Empty<string>();
        }

        public int PageCount => pageCount;

        public int CellCount => cellCount;

        public int TreeCount => treeCount;

        public int PacketCount => packetCount;

        public int AssetGroupCount => assetGroupCount;

        public int StaticInstanceCount => staticInstanceCount;

        public long AlwaysResidentBytes => alwaysResidentBytes;

        public long NearDetailColdBytes => nearDetailColdBytes;

        public long MaxPageBytes => maxPageBytes;

        public long MaxCellBytes => maxCellBytes;

        public int ColorCommandUpperBound => colorCommandUpperBound;

        public int ShadowCommandUpperBound => shadowCommandUpperBound;

        public int MaxPacketCountPerPage => maxPacketCountPerPage;

        public int MaxPacketCountPerCell => maxPacketCountPerCell;

        public int PageSplitCount => pageSplitCount;

        public int ValidationFailureCount => validationFailureCount;

        public string[] Messages => messages;
    }
}
