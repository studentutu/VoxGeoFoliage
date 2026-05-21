#nullable enable

using System;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Compiler-emitted cell bounds and source-tree range for one page.
    /// </summary>
    [Serializable]
    public struct FoliagePageCell
    {
        [SerializeField] private int cellIndex;
        [SerializeField] private Bounds worldBounds;
        [SerializeField] private Vector3 sphereCenter;
        [SerializeField] private float sphereRadius;
        [SerializeField] private int firstTreeIndex;
        [SerializeField] private int treeCount;

        public FoliagePageCell(
            int cellIndex,
            Bounds worldBounds,
            Vector3 sphereCenter,
            float sphereRadius,
            int firstTreeIndex,
            int treeCount)
        {
            this.cellIndex = cellIndex;
            this.worldBounds = worldBounds;
            this.sphereCenter = sphereCenter;
            this.sphereRadius = Mathf.Max(0f, sphereRadius);
            this.firstTreeIndex = firstTreeIndex;
            this.treeCount = Mathf.Max(0, treeCount);
        }

        public int CellIndex => cellIndex;

        public Bounds WorldBounds => worldBounds;

        public Vector3 SphereCenter => sphereCenter;

        public float SphereRadius => sphereRadius;

        public int FirstTreeIndex => firstTreeIndex;

        public int TreeCount => treeCount;
    }
}
