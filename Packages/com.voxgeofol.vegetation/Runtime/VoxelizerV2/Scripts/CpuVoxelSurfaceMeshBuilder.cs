#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelSystem
{
    /// <summary>
    /// Builds surface-only meshes from filled CPU voxel volumes.
    /// </summary>
    public static class CpuVoxelSurfaceMeshBuilder
    {
        /// <summary>
        /// [INTEGRATION] Converts a filled CPU voxel volume into a surface-only mesh with culled interior faces.
        /// </summary>
        public static Mesh BuildSurfaceMesh(CpuVoxelVolume volume, string meshName = "CpuVoxelSurface")
        {
            return BuildSurfaceMesh(volume, null, meshName, CpuVoxelSurfaceBuildOptions.Reduced);
        }

        /// <summary>
        /// [INTEGRATION] Converts the owned subset of a filled CPU voxel volume into a surface-only mesh.
        /// </summary>
        public static Mesh BuildSurfaceMesh(CpuVoxelVolume volume, Bounds? ownedBounds, string meshName = "CpuVoxelSurface")
        {
            return BuildSurfaceMesh(volume, ownedBounds, meshName, CpuVoxelSurfaceBuildOptions.Reduced);
        }

        /// <summary>
        /// [INTEGRATION] Converts the owned subset of a filled CPU voxel volume into a surface-only mesh.
        /// </summary>
        public static Mesh BuildSurfaceMesh(
            CpuVoxelVolume volume,
            Bounds? ownedBounds,
            string meshName,
            CpuVoxelSurfaceBuildOptions options)
        {
            // Range: volume must be non-null and already contain the final filled occupancy. Condition: only owned boundary voxels touching empty or out-of-range neighbors are emitted. Output: one mesh covering the exposed voxel surface for the requested ownership region.
            if (volume == null)
            {
                throw new ArgumentNullException(nameof(volume));
            }

            return options.ReduceCoplanarFaces
                ? BuildReducedSurfaceMesh(volume, ownedBounds, meshName)
                : BuildRawSurfaceMesh(volume, ownedBounds, meshName);
        }

        private static Mesh BuildRawSurfaceMesh(CpuVoxelVolume volume, Bounds? ownedBounds, string meshName)
        {
            Vector3 scale = Vector3.one * volume.UnitLength;
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            for (int x = 0; x < volume.Width; x++)
            {
                for (int y = 0; y < volume.Height; y++)
                {
                    for (int z = 0; z < volume.Depth; z++)
                    {
                        if (!volume.IsFilled(x, y, z) || !IsVoxelOwnedByBounds(volume, x, y, z, ownedBounds))
                        {
                            continue;
                        }

                        Vector3 position = volume.GetCellMin(x, y, z);
                        if (ShouldEmitFace(volume, x + 1, y, z))
                        {
                            AddRightQuad(vertices, triangles, scale, position);
                        }

                        if (ShouldEmitFace(volume, x - 1, y, z))
                        {
                            AddLeftQuad(vertices, triangles, scale, position);
                        }

                        if (ShouldEmitFace(volume, x, y + 1, z))
                        {
                            AddTopQuad(vertices, triangles, scale, position);
                        }

                        if (ShouldEmitFace(volume, x, y - 1, z))
                        {
                            AddBottomQuad(vertices, triangles, scale, position);
                        }

                        if (ShouldEmitFace(volume, x, y, z + 1))
                        {
                            AddFrontQuad(vertices, triangles, scale, position);
                        }

                        if (ShouldEmitFace(volume, x, y, z - 1))
                        {
                            AddBackQuad(vertices, triangles, scale, position);
                        }
                    }
                }
            }

            Mesh mesh = new Mesh
            {
                name = meshName,
                indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            if (triangles.Count > 0)
            {
                mesh.RecalculateNormals();
            }

            return mesh;
        }

        private static Mesh BuildReducedSurfaceMesh(CpuVoxelVolume volume, Bounds? ownedBounds, string meshName)
        {
            float unit = volume.UnitLength;
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            Dictionary<Vector3, int> vertexLookup = new Dictionary<Vector3, int>();

            EmitReducedFacesAlongX(volume, ownedBounds, true, unit, vertices, triangles, vertexLookup);
            EmitReducedFacesAlongX(volume, ownedBounds, false, unit, vertices, triangles, vertexLookup);
            EmitReducedFacesAlongY(volume, ownedBounds, true, unit, vertices, triangles, vertexLookup);
            EmitReducedFacesAlongY(volume, ownedBounds, false, unit, vertices, triangles, vertexLookup);
            EmitReducedFacesAlongZ(volume, ownedBounds, true, unit, vertices, triangles, vertexLookup);
            EmitReducedFacesAlongZ(volume, ownedBounds, false, unit, vertices, triangles, vertexLookup);

            Mesh mesh = new Mesh
            {
                name = meshName,
                indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            if (triangles.Count > 0)
            {
                mesh.RecalculateNormals();
            }

            return mesh;
        }

        private static bool ShouldEmitFace(CpuVoxelVolume volume, int x, int y, int z)
        {
            return !volume.ContainsIndex(x, y, z) || !volume.IsFilled(x, y, z);
        }

        private static bool IsVoxelOwnedByBounds(CpuVoxelVolume volume, int x, int y, int z, Bounds? ownedBounds)
        {
            if (!ownedBounds.HasValue)
            {
                return true;
            }

            Vector3 cellCenter = volume.GetCellCenter(x, y, z);
            Vector3 min = ownedBounds.Value.min;
            Vector3 max = ownedBounds.Value.max;
            return cellCenter.x >= min.x &&
                   cellCenter.x < max.x &&
                   cellCenter.y >= min.y &&
                   cellCenter.y < max.y &&
                   cellCenter.z >= min.z &&
                   cellCenter.z < max.z;
        }

        private static void EmitReducedFacesAlongX(
            CpuVoxelVolume volume,
            Bounds? ownedBounds,
            bool positive,
            float unit,
            List<Vector3> vertices,
            List<int> triangles,
            Dictionary<Vector3, int> vertexLookup)
        {
            int maskWidth = volume.Depth;
            int maskHeight = volume.Height;
            bool[] mask = new bool[maskWidth * maskHeight];

            for (int x = 0; x < volume.Width; x++)
            {
                FillMask(
                    mask,
                    maskWidth,
                    maskHeight,
                    (u, v) =>
                    {
                        int z = u;
                        int y = v;
                        if (!volume.IsFilled(x, y, z) || !IsVoxelOwnedByBounds(volume, x, y, z, ownedBounds))
                        {
                            return false;
                        }

                        return positive
                            ? ShouldEmitFace(volume, x + 1, y, z)
                            : ShouldEmitFace(volume, x - 1, y, z);
                    });

                GreedyConsumeMask(
                    mask,
                    maskWidth,
                    maskHeight,
                    (u, v, width, height) =>
                    {
                        Vector3 min = volume.GetCellMin(x, v, u);
                        float planeX = positive ? min.x + unit : min.x;
                        Vector3 p00 = new Vector3(planeX, min.y, min.z);
                        Vector3 p01 = new Vector3(planeX, min.y + height * unit, min.z);
                        Vector3 p10 = new Vector3(planeX, min.y, min.z + width * unit);
                        Vector3 p11 = new Vector3(planeX, min.y + height * unit, min.z + width * unit);
                        AddReducedQuad(vertices, triangles, vertexLookup, p00, p01, p10, p11, positive ? Vector3.right : Vector3.left);
                    });
            }
        }

        private static void EmitReducedFacesAlongY(
            CpuVoxelVolume volume,
            Bounds? ownedBounds,
            bool positive,
            float unit,
            List<Vector3> vertices,
            List<int> triangles,
            Dictionary<Vector3, int> vertexLookup)
        {
            int maskWidth = volume.Width;
            int maskHeight = volume.Depth;
            bool[] mask = new bool[maskWidth * maskHeight];

            for (int y = 0; y < volume.Height; y++)
            {
                FillMask(
                    mask,
                    maskWidth,
                    maskHeight,
                    (u, v) =>
                    {
                        int x = u;
                        int z = v;
                        if (!volume.IsFilled(x, y, z) || !IsVoxelOwnedByBounds(volume, x, y, z, ownedBounds))
                        {
                            return false;
                        }

                        return positive
                            ? ShouldEmitFace(volume, x, y + 1, z)
                            : ShouldEmitFace(volume, x, y - 1, z);
                    });

                GreedyConsumeMask(
                    mask,
                    maskWidth,
                    maskHeight,
                    (u, v, width, height) =>
                    {
                        Vector3 min = volume.GetCellMin(u, y, v);
                        float planeY = positive ? min.y + unit : min.y;
                        Vector3 p00 = new Vector3(min.x, planeY, min.z);
                        Vector3 p01 = new Vector3(min.x, planeY, min.z + height * unit);
                        Vector3 p10 = new Vector3(min.x + width * unit, planeY, min.z);
                        Vector3 p11 = new Vector3(min.x + width * unit, planeY, min.z + height * unit);
                        AddReducedQuad(vertices, triangles, vertexLookup, p00, p01, p10, p11, positive ? Vector3.up : Vector3.down);
                    });
            }
        }

        private static void EmitReducedFacesAlongZ(
            CpuVoxelVolume volume,
            Bounds? ownedBounds,
            bool positive,
            float unit,
            List<Vector3> vertices,
            List<int> triangles,
            Dictionary<Vector3, int> vertexLookup)
        {
            int maskWidth = volume.Width;
            int maskHeight = volume.Height;
            bool[] mask = new bool[maskWidth * maskHeight];

            for (int z = 0; z < volume.Depth; z++)
            {
                FillMask(
                    mask,
                    maskWidth,
                    maskHeight,
                    (u, v) =>
                    {
                        int x = u;
                        int y = v;
                        if (!volume.IsFilled(x, y, z) || !IsVoxelOwnedByBounds(volume, x, y, z, ownedBounds))
                        {
                            return false;
                        }

                        return positive
                            ? ShouldEmitFace(volume, x, y, z + 1)
                            : ShouldEmitFace(volume, x, y, z - 1);
                    });

                GreedyConsumeMask(
                    mask,
                    maskWidth,
                    maskHeight,
                    (u, v, width, height) =>
                    {
                        Vector3 min = volume.GetCellMin(u, v, z);
                        float planeZ = positive ? min.z + unit : min.z;
                        Vector3 p00 = new Vector3(min.x, min.y, planeZ);
                        Vector3 p01 = new Vector3(min.x + width * unit, min.y, planeZ);
                        Vector3 p10 = new Vector3(min.x, min.y + height * unit, planeZ);
                        Vector3 p11 = new Vector3(min.x + width * unit, min.y + height * unit, planeZ);
                        AddReducedQuad(vertices, triangles, vertexLookup, p00, p01, p10, p11, positive ? Vector3.forward : Vector3.back);
                    });
            }
        }

        private static void FillMask(bool[] mask, int width, int height, Func<int, int, bool> shouldEmit)
        {
            for (int v = 0; v < height; v++)
            {
                for (int u = 0; u < width; u++)
                {
                    mask[v * width + u] = shouldEmit(u, v);
                }
            }
        }

        private static void GreedyConsumeMask(bool[] mask, int width, int height, Action<int, int, int, int> emitRectangle)
        {
            for (int v = 0; v < height; v++)
            {
                for (int u = 0; u < width; u++)
                {
                    int maskIndex = v * width + u;
                    if (!mask[maskIndex])
                    {
                        continue;
                    }

                    int rectWidth = 1;
                    while (u + rectWidth < width && mask[v * width + u + rectWidth])
                    {
                        rectWidth++;
                    }

                    int rectHeight = 1;
                    bool canGrow = true;
                    while (v + rectHeight < height && canGrow)
                    {
                        for (int testU = 0; testU < rectWidth; testU++)
                        {
                            if (!mask[(v + rectHeight) * width + u + testU])
                            {
                                canGrow = false;
                                break;
                            }
                        }

                        if (canGrow)
                        {
                            rectHeight++;
                        }
                    }

                    for (int clearV = 0; clearV < rectHeight; clearV++)
                    {
                        for (int clearU = 0; clearU < rectWidth; clearU++)
                        {
                            mask[(v + clearV) * width + u + clearU] = false;
                        }
                    }

                    emitRectangle(u, v, rectWidth, rectHeight);
                }
            }
        }

        private static void AddReducedQuad(
            List<Vector3> vertices,
            List<int> triangles,
            Dictionary<Vector3, int> vertexLookup,
            Vector3 p00,
            Vector3 p01,
            Vector3 p10,
            Vector3 p11,
            Vector3 outwardNormal)
        {
            if (Vector3.Dot(Vector3.Cross(p01 - p00, p10 - p00), outwardNormal) < 0f)
            {
                Vector3 temp = p01;
                p01 = p10;
                p10 = temp;
            }

            int i0 = GetOrAddVertex(vertices, vertexLookup, p00);
            int i1 = GetOrAddVertex(vertices, vertexLookup, p01);
            int i2 = GetOrAddVertex(vertices, vertexLookup, p10);
            int i3 = GetOrAddVertex(vertices, vertexLookup, p11);

            triangles.Add(i0);
            triangles.Add(i1);
            triangles.Add(i2);
            triangles.Add(i2);
            triangles.Add(i1);
            triangles.Add(i3);
        }

        private static int GetOrAddVertex(
            List<Vector3> vertices,
            Dictionary<Vector3, int> vertexLookup,
            Vector3 vertex)
        {
            if (vertexLookup.TryGetValue(vertex, out int existingIndex))
            {
                return existingIndex;
            }

            int newIndex = vertices.Count;
            vertices.Add(vertex);
            vertexLookup.Add(vertex, newIndex);
            return newIndex;
        }

        private static void AddRightQuad(List<Vector3> verts, List<int> indices, Vector3 scale, Vector3 pos)
        {
            int count = verts.Count;

            verts.Add(pos + new Vector3(scale.x, 0f, scale.z));
            verts.Add(pos + new Vector3(scale.x, scale.y, 0f));
            verts.Add(pos + new Vector3(scale.x, 0f, 0f));

            verts.Add(pos + new Vector3(scale.x, 0f, scale.z));
            verts.Add(pos + new Vector3(scale.x, scale.y, scale.z));
            verts.Add(pos + new Vector3(scale.x, scale.y, 0f));

            indices.Add(count + 2);
            indices.Add(count + 1);
            indices.Add(count + 0);
            indices.Add(count + 5);
            indices.Add(count + 4);
            indices.Add(count + 3);
        }

        private static void AddLeftQuad(List<Vector3> verts, List<int> indices, Vector3 scale, Vector3 pos)
        {
            int count = verts.Count;

            verts.Add(pos + new Vector3(0f, 0f, scale.z));
            verts.Add(pos + new Vector3(0f, scale.y, 0f));
            verts.Add(pos + new Vector3(0f, 0f, 0f));

            verts.Add(pos + new Vector3(0f, 0f, scale.z));
            verts.Add(pos + new Vector3(0f, scale.y, scale.z));
            verts.Add(pos + new Vector3(0f, scale.y, 0f));

            indices.Add(count + 0);
            indices.Add(count + 1);
            indices.Add(count + 2);
            indices.Add(count + 3);
            indices.Add(count + 4);
            indices.Add(count + 5);
        }

        private static void AddTopQuad(List<Vector3> verts, List<int> indices, Vector3 scale, Vector3 pos)
        {
            int count = verts.Count;

            verts.Add(pos + new Vector3(0f, scale.y, scale.z));
            verts.Add(pos + new Vector3(scale.x, scale.y, 0f));
            verts.Add(pos + new Vector3(0f, scale.y, 0f));

            verts.Add(pos + new Vector3(0f, scale.y, scale.z));
            verts.Add(pos + new Vector3(scale.x, scale.y, scale.z));
            verts.Add(pos + new Vector3(scale.x, scale.y, 0f));

            indices.Add(count + 0);
            indices.Add(count + 1);
            indices.Add(count + 2);
            indices.Add(count + 3);
            indices.Add(count + 4);
            indices.Add(count + 5);
        }

        private static void AddBottomQuad(List<Vector3> verts, List<int> indices, Vector3 scale, Vector3 pos)
        {
            int count = verts.Count;

            verts.Add(pos + new Vector3(0f, 0f, scale.z));
            verts.Add(pos + new Vector3(scale.x, 0f, 0f));
            verts.Add(pos + new Vector3(0f, 0f, 0f));

            verts.Add(pos + new Vector3(0f, 0f, scale.z));
            verts.Add(pos + new Vector3(scale.x, 0f, scale.z));
            verts.Add(pos + new Vector3(scale.x, 0f, 0f));

            indices.Add(count + 2);
            indices.Add(count + 1);
            indices.Add(count + 0);
            indices.Add(count + 5);
            indices.Add(count + 4);
            indices.Add(count + 3);
        }

        private static void AddFrontQuad(List<Vector3> verts, List<int> indices, Vector3 scale, Vector3 pos)
        {
            int count = verts.Count;

            verts.Add(pos + new Vector3(0f, scale.y, scale.z));
            verts.Add(pos + new Vector3(scale.x, 0f, scale.z));
            verts.Add(pos + new Vector3(0f, 0f, scale.z));

            verts.Add(pos + new Vector3(0f, scale.y, scale.z));
            verts.Add(pos + new Vector3(scale.x, scale.y, scale.z));
            verts.Add(pos + new Vector3(scale.x, 0f, scale.z));

            indices.Add(count + 2);
            indices.Add(count + 1);
            indices.Add(count + 0);
            indices.Add(count + 5);
            indices.Add(count + 4);
            indices.Add(count + 3);
        }

        private static void AddBackQuad(List<Vector3> verts, List<int> indices, Vector3 scale, Vector3 pos)
        {
            int count = verts.Count;

            verts.Add(pos + new Vector3(0f, scale.y, 0f));
            verts.Add(pos + new Vector3(scale.x, 0f, 0f));
            verts.Add(pos + new Vector3(0f, 0f, 0f));

            verts.Add(pos + new Vector3(0f, scale.y, 0f));
            verts.Add(pos + new Vector3(scale.x, scale.y, 0f));
            verts.Add(pos + new Vector3(scale.x, 0f, 0f));

            indices.Add(count + 0);
            indices.Add(count + 1);
            indices.Add(count + 2);
            indices.Add(count + 3);
            indices.Add(count + 4);
            indices.Add(count + 5);
        }
    }
}
