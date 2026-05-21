#nullable enable

using System;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Hard compiler caps for generated foliage page assets.
    /// </summary>
    [Serializable]
    public struct FoliageCompilerSettings
    {
        private static readonly Vector3 DefaultCellSize = new Vector3(32f, 32f, 32f);
        private const int DefaultMaxTreesPerPage = 256;
        private const int DefaultMaxTreesPerCell = 128;
        private const int DefaultMaxPacketsPerPage = 8192;
        private const int DefaultMaxAssetGroups = 4096;
        private const int DefaultMaxNearDetailBytesPerPage = 16 * 1024 * 1024;

        [SerializeField] private Vector3 cellSize;
        [SerializeField] private int maxTreesPerPage;
        [SerializeField] private int maxTreesPerCell;
        [SerializeField] private int maxPacketsPerPage;
        [SerializeField] private int maxAssetGroups;
        [SerializeField] private int maxNearDetailBytesPerPage;

        public FoliageCompilerSettings(
            Vector3 cellSize,
            int maxTreesPerPage,
            int maxTreesPerCell,
            int maxPacketsPerPage,
            int maxAssetGroups)
            : this(
                cellSize,
                maxTreesPerPage,
                maxTreesPerCell,
                maxPacketsPerPage,
                maxAssetGroups,
                DefaultMaxNearDetailBytesPerPage)
        {
        }

        public FoliageCompilerSettings(
            Vector3 cellSize,
            int maxTreesPerPage,
            int maxTreesPerCell,
            int maxPacketsPerPage,
            int maxAssetGroups,
            int maxNearDetailBytesPerPage)
        {
            this.cellSize = SanitizeCellSize(cellSize);
            this.maxTreesPerPage = Mathf.Max(1, maxTreesPerPage);
            this.maxTreesPerCell = Mathf.Max(1, maxTreesPerCell);
            this.maxPacketsPerPage = Mathf.Max(1, maxPacketsPerPage);
            this.maxAssetGroups = Mathf.Max(1, maxAssetGroups);
            this.maxNearDetailBytesPerPage = Mathf.Max(1, maxNearDetailBytesPerPage);
        }

        public Vector3 CellSize => SanitizeCellSize(cellSize);

        public int MaxTreesPerPage => maxTreesPerPage > 0 ? maxTreesPerPage : DefaultMaxTreesPerPage;

        public int MaxTreesPerCell => maxTreesPerCell > 0 ? maxTreesPerCell : DefaultMaxTreesPerCell;

        public int MaxPacketsPerPage => maxPacketsPerPage > 0 ? maxPacketsPerPage : DefaultMaxPacketsPerPage;

        public int MaxAssetGroups => maxAssetGroups > 0 ? maxAssetGroups : DefaultMaxAssetGroups;

        public int MaxNearDetailBytesPerPage => maxNearDetailBytesPerPage > 0
            ? maxNearDetailBytesPerPage
            : DefaultMaxNearDetailBytesPerPage;

        public static FoliageCompilerSettings Default => new FoliageCompilerSettings(
            DefaultCellSize,
            DefaultMaxTreesPerPage,
            DefaultMaxTreesPerCell,
            DefaultMaxPacketsPerPage,
            DefaultMaxAssetGroups,
            DefaultMaxNearDetailBytesPerPage);

        public static Vector3 SanitizeCellSize(Vector3 candidate)
        {
            return new Vector3(
                candidate.x > 0f ? candidate.x : DefaultCellSize.x,
                candidate.y > 0f ? candidate.y : DefaultCellSize.y,
                candidate.z > 0f ? candidate.z : DefaultCellSize.z);
        }
    }
}
