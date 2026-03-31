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
            // Range: volume must be non-null and already contain the final filled occupancy. Condition: only boundary faces touching empty or out-of-range neighbors are emitted. Output: one mesh covering the exposed voxel surface.
            if (volume == null)
            {
                throw new ArgumentNullException(nameof(volume));
            }

            Vector3 scale = Vector3.one * volume.UnitLength;
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            for (int x = 0; x < volume.Width; x++)
            {
                for (int y = 0; y < volume.Height; y++)
                {
                    for (int z = 0; z < volume.Depth; z++)
                    {
                        if (!volume.IsFilled(x, y, z))
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

        private static bool ShouldEmitFace(CpuVoxelVolume volume, int x, int y, int z)
        {
            return !volume.ContainsIndex(x, y, z) || !volume.IsFilled(x, y, z);
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
