#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Deterministic tree-to-cell registration and visible-cell query authority.
    /// </summary>
    public sealed class VegetationSpatialGrid
    {
        public sealed class CellData
        {
            public int CellIndex;
            public Vector3Int Coordinate;
            public Bounds BaseBounds;
            public Bounds ResidentBounds;
            public bool HasResidentBounds;
            public readonly List<int> TreeIndices = new List<int>();

            public Bounds AuthoritativeBounds => HasResidentBounds ? ResidentBounds : BaseBounds;
        }

        private readonly Vector3 origin;
        private readonly Vector3 cellSize;
        private readonly CellData[] cells;

        public VegetationSpatialGrid(Vector3 origin, Vector3 cellSize, CellData[] cells)
        {
            this.origin = origin;
            this.cellSize = cellSize;
            this.cells = cells;
        }

        public Vector3 Origin => origin;

        public Vector3 CellSize => cellSize;

        public IReadOnlyList<CellData> Cells => cells;

        /// <summary>
        /// [INTEGRATION] Rebuilds the deterministic visible-cell mask consumed by classification.
        /// </summary>
        public void BuildVisibleCellMask(Plane[] frustumPlanes, uint[] targetMask, List<int> visibleCellIndices)
        {
            // Range: requires the current camera frustum planes plus a target mask sized to the registered cell count. Condition: cell bounds are already conservative for resident tree spheres. Output: stable visible-cell mask and ordered visible-cell indices for Phase D CPU/GPU classification.
            if (frustumPlanes == null)
            {
                throw new ArgumentNullException(nameof(frustumPlanes));
            }

            if (targetMask == null)
            {
                throw new ArgumentNullException(nameof(targetMask));
            }

            if (visibleCellIndices == null)
            {
                throw new ArgumentNullException(nameof(visibleCellIndices));
            }

            if (targetMask.Length != cells.Length)
            {
                throw new ArgumentException($"Visible-cell mask length {targetMask.Length} does not match registered cell count {cells.Length}.", nameof(targetMask));
            }

            visibleCellIndices.Clear();
            for (int i = 0; i < cells.Length; i++)
            {
                bool isVisible = GeometryUtility.TestPlanesAABB(frustumPlanes, cells[i].AuthoritativeBounds);
                targetMask[i] = isVisible ? 1u : 0u;
                if (isVisible)
                {
                    visibleCellIndices.Add(i);
                }
            }
        }

        /// <summary>
        /// [INTEGRATION] Converts one world-space tree center into the deterministic registration cell coordinate.
        /// </summary>
        public Vector3Int GetCellCoordinate(Vector3 worldPosition)
        {
            Vector3 relative = worldPosition - origin;
            return new Vector3Int(
                Mathf.FloorToInt(relative.x / cellSize.x),
                Mathf.FloorToInt(relative.y / cellSize.y),
                Mathf.FloorToInt(relative.z / cellSize.z));
        }

        internal static VegetationSpatialGrid Build(Vector3 origin, Vector3 cellSize, List<VegetationTreeInstanceRuntime> treeInstances)
        {
            if (cellSize.x <= 0f || cellSize.y <= 0f || cellSize.z <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), "Spatial-grid cellSize must stay strictly positive on every axis.");
            }

            Dictionary<Vector3Int, CellData> byCoordinate = new Dictionary<Vector3Int, CellData>();
            for (int treeIndex = 0; treeIndex < treeInstances.Count; treeIndex++)
            {
                VegetationTreeInstanceRuntime treeInstance = treeInstances[treeIndex];
                Vector3Int coordinate = GetCellCoordinate(origin, cellSize, treeInstance.SphereCenterWorld);
                if (!byCoordinate.TryGetValue(coordinate, out CellData? cell))
                {
                    cell = new CellData
                    {
                        Coordinate = coordinate,
                        BaseBounds = BuildCellBounds(origin, cellSize, coordinate)
                    };

                    byCoordinate.Add(coordinate, cell);
                }

                Bounds treeSphereBounds = new Bounds(
                    treeInstance.SphereCenterWorld,
                    Vector3.one * (treeInstance.BoundingSphereRadius * 2f));

                if (!cell.HasResidentBounds)
                {
                    cell.ResidentBounds = treeSphereBounds;
                    cell.HasResidentBounds = true;
                }
                else
                {
                    cell.ResidentBounds.Encapsulate(treeSphereBounds);
                }
            }

            List<Vector3Int> orderedCoordinates = new List<Vector3Int>(byCoordinate.Keys);
            orderedCoordinates.Sort(CompareCoordinates);

            CellData[] orderedCells = new CellData[orderedCoordinates.Count];
            Dictionary<Vector3Int, int> cellIndexByCoordinate = new Dictionary<Vector3Int, int>(orderedCoordinates.Count);
            for (int i = 0; i < orderedCoordinates.Count; i++)
            {
                CellData cell = byCoordinate[orderedCoordinates[i]];
                cell.CellIndex = i;
                orderedCells[i] = cell;
                cellIndexByCoordinate.Add(cell.Coordinate, i);
            }

            for (int treeIndex = 0; treeIndex < treeInstances.Count; treeIndex++)
            {
                VegetationTreeInstanceRuntime treeInstance = treeInstances[treeIndex];
                Vector3Int coordinate = GetCellCoordinate(origin, cellSize, treeInstance.SphereCenterWorld);
                int cellIndex = cellIndexByCoordinate[coordinate];
                treeInstance.CellIndex = cellIndex;
                treeInstances[treeIndex] = treeInstance;
                orderedCells[cellIndex].TreeIndices.Add(treeIndex);
            }

            return new VegetationSpatialGrid(origin, cellSize, orderedCells);
        }

        private static int CompareCoordinates(Vector3Int left, Vector3Int right)
        {
            int compareX = left.x.CompareTo(right.x);
            if (compareX != 0)
            {
                return compareX;
            }

            int compareY = left.y.CompareTo(right.y);
            if (compareY != 0)
            {
                return compareY;
            }

            return left.z.CompareTo(right.z);
        }

        private static Bounds BuildCellBounds(Vector3 origin, Vector3 cellSize, Vector3Int coordinate)
        {
            Vector3 min = origin + Vector3.Scale(cellSize, coordinate);
            return new Bounds(min + cellSize * 0.5f, cellSize);
        }

        private static Vector3Int GetCellCoordinate(Vector3 origin, Vector3 cellSize, Vector3 worldPosition)
        {
            Vector3 relative = worldPosition - origin;
            return new Vector3Int(
                Mathf.FloorToInt(relative.x / cellSize.x),
                Mathf.FloorToInt(relative.y / cellSize.y),
                Mathf.FloorToInt(relative.z / cellSize.z));
        }
    }
}
