#nullable enable

using System;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Dense local-space occupancy grid used by shell and impostor baking.
    /// </summary>
    public sealed class VoxelGrid
    {
        private const float MinimumBoundsSize = 0.001f;
        private readonly bool[] occupied;
        private int occupiedCount;

        public VoxelGrid(Bounds bounds, int resolution)
        {
            if (resolution <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(resolution), "Voxel resolution must be greater than zero.");
            }

            Bounds = NormalizeBounds(bounds);
            Resolution = resolution;
            CellSize = Bounds.size / resolution;
            occupied = new bool[resolution * resolution * resolution];
        }

        public Bounds Bounds { get; }

        public int Resolution { get; }

        public Vector3 CellSize { get; }

        public int OccupiedCount => occupiedCount;

        public bool IsOccupied(int x, int y, int z)
        {
            return occupied[ToIndex(x, y, z)];
        }

        public void SetOccupied(int x, int y, int z, bool value = true)
        {
            int index = ToIndex(x, y, z);
            if (occupied[index] == value)
            {
                return;
            }

            occupied[index] = value;
            occupiedCount += value ? 1 : -1;
        }

        public Vector3 GetVoxelMin(int x, int y, int z)
        {
            ValidateCoordinate(x, y, z);
            Vector3 boundsMin = Bounds.min;
            return boundsMin + Vector3.Scale(new Vector3(x, y, z), CellSize);
        }

        public Vector3 GetVoxelMax(int x, int y, int z)
        {
            return GetVoxelMin(x, y, z) + CellSize;
        }

        public Vector3 GetVoxelCenter(int x, int y, int z)
        {
            return GetVoxelMin(x, y, z) + CellSize * 0.5f;
        }

        public bool IsSurfaceVoxel(int x, int y, int z)
        {
            if (!IsOccupied(x, y, z))
            {
                return false;
            }

            return x == 0 ||
                   y == 0 ||
                   z == 0 ||
                   x == Resolution - 1 ||
                   y == Resolution - 1 ||
                   z == Resolution - 1 ||
                   !IsOccupied(x - 1, y, z) ||
                   !IsOccupied(x + 1, y, z) ||
                   !IsOccupied(x, y - 1, z) ||
                   !IsOccupied(x, y + 1, z) ||
                   !IsOccupied(x, y, z - 1) ||
                   !IsOccupied(x, y, z + 1);
        }

        public VoxelGrid Clone()
        {
            VoxelGrid clone = new VoxelGrid(Bounds, Resolution);
            Array.Copy(occupied, clone.occupied, occupied.Length);
            clone.occupiedCount = occupiedCount;
            return clone;
        }

        public VoxelGrid CreateDilated(int iterations)
        {
            if (iterations < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(iterations), "Dilation iterations must be zero or greater.");
            }

            VoxelGrid current = Clone();
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                VoxelGrid dilated = new VoxelGrid(Bounds, Resolution);
                for (int z = 0; z < Resolution; z++)
                {
                    for (int y = 0; y < Resolution; y++)
                    {
                        for (int x = 0; x < Resolution; x++)
                        {
                            if (current.IsOccupied(x, y, z) || HasOccupiedNeighbor(current, x, y, z))
                            {
                                dilated.SetOccupied(x, y, z);
                            }
                        }
                    }
                }

                current = dilated;
            }

            return current;
        }

        private static bool HasOccupiedNeighbor(VoxelGrid grid, int x, int y, int z)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0)
                        {
                            continue;
                        }

                        int sampleX = x + dx;
                        int sampleY = y + dy;
                        int sampleZ = z + dz;
                        if (sampleX < 0 ||
                            sampleY < 0 ||
                            sampleZ < 0 ||
                            sampleX >= grid.Resolution ||
                            sampleY >= grid.Resolution ||
                            sampleZ >= grid.Resolution)
                        {
                            continue;
                        }

                        if (grid.IsOccupied(sampleX, sampleY, sampleZ))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private int ToIndex(int x, int y, int z)
        {
            ValidateCoordinate(x, y, z);
            return x + (y * Resolution) + (z * Resolution * Resolution);
        }

        private void ValidateCoordinate(int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= Resolution || y >= Resolution || z >= Resolution)
            {
                throw new ArgumentOutOfRangeException($"Voxel coordinate ({x}, {y}, {z}) is outside resolution {Resolution}.");
            }
        }

        private static Bounds NormalizeBounds(Bounds bounds)
        {
            Vector3 size = bounds.size;
            if (size.x < MinimumBoundsSize)
            {
                size.x = MinimumBoundsSize;
            }

            if (size.y < MinimumBoundsSize)
            {
                size.y = MinimumBoundsSize;
            }

            if (size.z < MinimumBoundsSize)
            {
                size.z = MinimumBoundsSize;
            }

            return new Bounds(bounds.center, size);
        }
    }
}
