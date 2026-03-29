#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Editor-only dense mesh voxelizer used by vegetation shell baking.
    /// </summary>
    public static class Voxelizer
    {
        private const float IntersectionEpsilon = 0.00001f;
        private static readonly Vector3 RayDirection = new Vector3(0.917321f, 0.223114f, 0.330817f).normalized;

        /// <summary>
        /// [INTEGRATION] Builds a solid voxel occupancy grid from one readable mesh.
        /// </summary>
        public static VoxelGrid VoxelizeSolid(Mesh mesh, int resolution)
        {
            // Range: readable input mesh with any triangle count. Condition: editor-only dense sampling favors determinism over speed. Output: occupied voxels approximate solid canopy mass in mesh local space.
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            if (!mesh.isReadable)
            {
                throw new InvalidOperationException($"{mesh.name} must be readable before voxelization.");
            }

            VoxelGrid voxelGrid = new VoxelGrid(mesh.bounds, resolution);
            int[] triangles = mesh.triangles;
            if (triangles.Length == 0)
            {
                return voxelGrid;
            }

            Vector3[] vertices = mesh.vertices;
            Vector3 halfCell = voxelGrid.CellSize * 0.5f;
            float surfaceDistanceThresholdSq = halfCell.sqrMagnitude * 1.1025f;

            for (int z = 0; z < voxelGrid.Resolution; z++)
            {
                for (int y = 0; y < voxelGrid.Resolution; y++)
                {
                    for (int x = 0; x < voxelGrid.Resolution; x++)
                    {
                        Vector3 samplePoint = voxelGrid.GetVoxelCenter(x, y, z);
                        bool nearSurface = false;
                        int forwardIntersections = 0;
                        Vector3 rayOrigin = samplePoint + RayDirection * 0.00037f;

                        for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex += 3)
                        {
                            Vector3 a = vertices[triangles[triangleIndex]];
                            Vector3 b = vertices[triangles[triangleIndex + 1]];
                            Vector3 c = vertices[triangles[triangleIndex + 2]];

                            if (!nearSurface &&
                                DistancePointTriangleSquared(samplePoint, a, b, c) <= surfaceDistanceThresholdSq)
                            {
                                nearSurface = true;
                            }

                            if (RayIntersectsTriangle(rayOrigin, RayDirection, a, b, c, out _))
                            {
                                forwardIntersections++;
                            }
                        }

                        if (nearSurface || (forwardIntersections & 1) == 1)
                        {
                            voxelGrid.SetOccupied(x, y, z);
                        }
                    }
                }
            }

            return voxelGrid;
        }

        /// <summary>
        /// [INTEGRATION] Emits a closed triangle mesh from occupied voxel surface faces.
        /// </summary>
        public static Mesh CreateSurfaceMesh(VoxelGrid voxelGrid)
        {
            // Range: accepts any occupancy density. Condition: only exposed axis-aligned faces emit geometry. Output: readable mesh with outward face winding for occupied voxels.
            if (voxelGrid == null)
            {
                throw new ArgumentNullException(nameof(voxelGrid));
            }

            List<Vector3> vertices = new List<Vector3>(Mathf.Max(8, voxelGrid.OccupiedCount * 24));
            List<int> triangles = new List<int>(Mathf.Max(12, voxelGrid.OccupiedCount * 36));
            List<Vector3> normals = new List<Vector3>(Mathf.Max(8, voxelGrid.OccupiedCount * 24));

            for (int z = 0; z < voxelGrid.Resolution; z++)
            {
                for (int y = 0; y < voxelGrid.Resolution; y++)
                {
                    for (int x = 0; x < voxelGrid.Resolution; x++)
                    {
                        if (!voxelGrid.IsSurfaceVoxel(x, y, z))
                        {
                            continue;
                        }

                        Vector3 min = voxelGrid.GetVoxelMin(x, y, z);
                        Vector3 max = voxelGrid.GetVoxelMax(x, y, z);

                        if (x == voxelGrid.Resolution - 1 || !voxelGrid.IsOccupied(x + 1, y, z))
                        {
                            EmitFace(vertices, triangles, normals,
                                new Vector3(max.x, min.y, min.z),
                                new Vector3(max.x, max.y, min.z),
                                new Vector3(max.x, max.y, max.z),
                                new Vector3(max.x, min.y, max.z),
                                Vector3.right);
                        }

                        if (x == 0 || !voxelGrid.IsOccupied(x - 1, y, z))
                        {
                            EmitFace(vertices, triangles, normals,
                                new Vector3(min.x, min.y, max.z),
                                new Vector3(min.x, max.y, max.z),
                                new Vector3(min.x, max.y, min.z),
                                new Vector3(min.x, min.y, min.z),
                                Vector3.left);
                        }

                        if (y == voxelGrid.Resolution - 1 || !voxelGrid.IsOccupied(x, y + 1, z))
                        {
                            EmitFace(vertices, triangles, normals,
                                new Vector3(min.x, max.y, min.z),
                                new Vector3(min.x, max.y, max.z),
                                new Vector3(max.x, max.y, max.z),
                                new Vector3(max.x, max.y, min.z),
                                Vector3.up);
                        }

                        if (y == 0 || !voxelGrid.IsOccupied(x, y - 1, z))
                        {
                            EmitFace(vertices, triangles, normals,
                                new Vector3(min.x, min.y, max.z),
                                new Vector3(min.x, min.y, min.z),
                                new Vector3(max.x, min.y, min.z),
                                new Vector3(max.x, min.y, max.z),
                                Vector3.down);
                        }

                        if (z == voxelGrid.Resolution - 1 || !voxelGrid.IsOccupied(x, y, z + 1))
                        {
                            EmitFace(vertices, triangles, normals,
                                new Vector3(max.x, min.y, max.z),
                                new Vector3(max.x, max.y, max.z),
                                new Vector3(min.x, max.y, max.z),
                                new Vector3(min.x, min.y, max.z),
                                Vector3.forward);
                        }

                        if (z == 0 || !voxelGrid.IsOccupied(x, y, z - 1))
                        {
                            EmitFace(vertices, triangles, normals,
                                new Vector3(min.x, min.y, min.z),
                                new Vector3(min.x, max.y, min.z),
                                new Vector3(max.x, max.y, min.z),
                                new Vector3(max.x, min.y, min.z),
                                Vector3.back);
                        }
                    }
                }
            }

            Mesh surfaceMesh = new Mesh
            {
                name = "VegetationVoxelSurface",
                indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };

            surfaceMesh.SetVertices(vertices);
            surfaceMesh.SetTriangles(triangles, 0, true);
            surfaceMesh.SetNormals(normals);
            surfaceMesh.RecalculateBounds();
            return surfaceMesh;
        }

        private static void EmitFace(
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector3> normals,
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            Vector3 v3,
            Vector3 normal)
        {
            int startIndex = vertices.Count;
            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);

            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);

            triangles.Add(startIndex);
            triangles.Add(startIndex + 1);
            triangles.Add(startIndex + 2);
            triangles.Add(startIndex);
            triangles.Add(startIndex + 2);
            triangles.Add(startIndex + 3);
        }

        private static bool RayIntersectsTriangle(
            Vector3 origin,
            Vector3 direction,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            out float distance)
        {
            Vector3 edgeAb = b - a;
            Vector3 edgeAc = c - a;
            Vector3 p = Vector3.Cross(direction, edgeAc);
            float determinant = Vector3.Dot(edgeAb, p);
            if (Mathf.Abs(determinant) <= IntersectionEpsilon)
            {
                distance = 0f;
                return false;
            }

            float inverseDeterminant = 1f / determinant;
            Vector3 t = origin - a;
            float barycentricU = Vector3.Dot(t, p) * inverseDeterminant;
            if (barycentricU < -IntersectionEpsilon || barycentricU > 1f + IntersectionEpsilon)
            {
                distance = 0f;
                return false;
            }

            Vector3 q = Vector3.Cross(t, edgeAb);
            float barycentricV = Vector3.Dot(direction, q) * inverseDeterminant;
            if (barycentricV < -IntersectionEpsilon || barycentricU + barycentricV > 1f + IntersectionEpsilon)
            {
                distance = 0f;
                return false;
            }

            distance = Vector3.Dot(edgeAc, q) * inverseDeterminant;
            return distance > IntersectionEpsilon;
        }

        private static float DistancePointTriangleSquared(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = point - a;

            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f)
            {
                return ap.sqrMagnitude;
            }

            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3)
            {
                return bp.sqrMagnitude;
            }

            float vc = (d1 * d4) - (d3 * d2);
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                Vector3 projection = a + (v * ab);
                return (point - projection).sqrMagnitude;
            }

            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6)
            {
                return cp.sqrMagnitude;
            }

            float vb = (d5 * d2) - (d1 * d6);
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                Vector3 projection = a + (w * ac);
                return (point - projection).sqrMagnitude;
            }

            float va = (d3 * d6) - (d5 * d4);
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                Vector3 bc = c - b;
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                Vector3 projection = b + (w * bc);
                return (point - projection).sqrMagnitude;
            }

            float inverseDenominator = 1f / (va + vb + vc);
            float barycentricV = vb * inverseDenominator;
            float barycentricW = vc * inverseDenominator;
            Vector3 closestPoint = a + (ab * barycentricV) + (ac * barycentricW);
            return (point - closestPoint).sqrMagnitude;
        }
    }
}
