#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelSystem
{
    /// <summary>
    /// Indexed CPU voxel volume with padded bounds and uniform cell size.
    /// </summary>
    public sealed class CpuVoxelVolume
    {
        private readonly Voxel_t[,,] voxels;

        public CpuVoxelVolume(Vector3 paddedMin, float unitLength, Voxel_t[,,] voxels)
        {
            if (unitLength <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(unitLength), unitLength, "unitLength must be greater than zero.");
            }

            this.voxels = voxels ?? throw new ArgumentNullException(nameof(voxels));
            if (voxels.GetLength(0) <= 0 || voxels.GetLength(1) <= 0 || voxels.GetLength(2) <= 0)
            {
                throw new ArgumentException("Voxel volume dimensions must all be greater than zero.", nameof(voxels));
            }

            PaddedMin = paddedMin;
            UnitLength = unitLength;
            Width = voxels.GetLength(0);
            Height = voxels.GetLength(1);
            Depth = voxels.GetLength(2);
        }

        public Vector3 PaddedMin { get; }

        public float UnitLength { get; }

        public int Width { get; }

        public int Height { get; }

        public int Depth { get; }

        public bool ContainsIndex(int x, int y, int z)
        {
            return x >= 0 && x < Width &&
                   y >= 0 && y < Height &&
                   z >= 0 && z < Depth;
        }

        /// <summary>
        /// [INTEGRATION] Returns whether one indexed voxel cell is filled.
        /// </summary>
        public bool IsFilled(int x, int y, int z)
        {
            ValidateIndices(x, y, z);
            return !voxels[x, y, z].IsEmpty();
        }

        public Voxel_t GetVoxel(int x, int y, int z)
        {
            ValidateIndices(x, y, z);
            return voxels[x, y, z];
        }

        public Vector3 GetCellMin(int x, int y, int z)
        {
            ValidateIndices(x, y, z);
            return PaddedMin + Vector3.Scale(Vector3.one * UnitLength, new Vector3(x, y, z));
        }

        public Vector3 GetCellCenter(int x, int y, int z)
        {
            ValidateIndices(x, y, z);
            return GetCellMin(x, y, z) + Vector3.one * (UnitLength * 0.5f);
        }

        /// <summary>
        /// [INTEGRATION] Collects the currently filled voxels for legacy callers that still consume the list API.
        /// </summary>
        public List<Voxel_t> CollectFilledVoxels()
        {
            List<Voxel_t> filledVoxels = new List<Voxel_t>();
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    for (int z = 0; z < Depth; z++)
                    {
                        if (!voxels[x, y, z].IsEmpty())
                        {
                            filledVoxels.Add(voxels[x, y, z]);
                        }
                    }
                }
            }

            return filledVoxels;
        }

        private void ValidateIndices(int x, int y, int z)
        {
            if (!ContainsIndex(x, y, z))
            {
                throw new ArgumentOutOfRangeException(nameof(x), $"Voxel index ({x}, {y}, {z}) is outside the volume bounds {Width}x{Height}x{Depth}.");
            }
        }
    }
}
