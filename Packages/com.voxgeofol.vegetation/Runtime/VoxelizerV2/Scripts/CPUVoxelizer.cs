#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelSystem
{
    /// <summary>
    /// CPU mesh voxelizer based on triangle-vs-AABB overlap and a front/back fill pass.
    /// </summary>
    public class CPUVoxelizer
    {
        public class Triangle
        {
            public Vector3 a;
            public Vector3 b;
            public Vector3 c;
            public Bounds bounds;
            public bool frontFacing;

            public Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 dir)
            {
                this.a = a;
                this.b = b;
                this.c = c;

                Vector3 cross = Vector3.Cross(b - a, c - a);
                frontFacing = Vector3.Dot(cross, dir) <= 0f;

                Vector3 min = Vector3.Min(Vector3.Min(a, b), c);
                Vector3 max = Vector3.Max(Vector3.Max(a, b), c);
                bounds.SetMinMax(min, max);
            }

            public Vector2 GetUV(Vector3 p, Vector2 uva, Vector2 uvb, Vector2 uvc)
            {
                Barycentric(p, out float u, out float v, out float w);
                return uva * u + uvb * v + uvc * w;
            }

            public void Barycentric(Vector3 p, out float u, out float v, out float w)
            {
                Vector3 v0 = b - a;
                Vector3 v1 = c - a;
                Vector3 v2 = p - a;
                float d00 = Vector3.Dot(v0, v0);
                float d01 = Vector3.Dot(v0, v1);
                float d11 = Vector3.Dot(v1, v1);
                float d20 = Vector3.Dot(v2, v0);
                float d21 = Vector3.Dot(v2, v1);
                float denom = 1f / (d00 * d11 - d01 * d01);
                v = (d11 * d20 - d01 * d21) * denom;
                w = (d00 * d21 - d01 * d20) * denom;
                u = 1.0f - v - w;
            }
        }

        /// <summary>
        /// [INTEGRATION] Voxelizes one mesh into an indexed CPU volume with padded bounds and uniform cell size.
        /// </summary>
        public static CpuVoxelVolume VoxelizeToVolume(Mesh mesh, int resolution)
        {
            // Range: mesh must be readable and resolution must be >= 2. Condition: triangle/AABB overlap marks the shell first, then one front/back sweep fills the volume. Output: indexed occupancy grid that can be reused by production surface builders or legacy callers.
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            if (!mesh.isReadable)
            {
                throw new InvalidOperationException($"{mesh.name} must be readable before CPU voxelization.");
            }

            if (resolution < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Resolution must be 2 or greater.");
            }

            mesh.RecalculateBounds();
            Bounds bounds = mesh.bounds;
            float maxLength = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (maxLength <= Mathf.Epsilon)
            {
                throw new InvalidOperationException($"{mesh.name} has invalid bounds {bounds}.");
            }

            float unit = maxLength / resolution;
            float halfUnit = unit * 0.5f;
            Vector3 halfUnitOffset = Vector3.one * halfUnit;
            Vector3 scanOrigin = bounds.min - halfUnitOffset;
            Vector3 paddedMin = scanOrigin - halfUnitOffset;
            Vector3 end = bounds.max + halfUnitOffset;
            Vector3 size = end - scanOrigin;

            int width = Mathf.CeilToInt(size.x / unit);
            int height = Mathf.CeilToInt(size.y / unit);
            int depth = Mathf.CeilToInt(size.z / unit);
            Voxel_t[,,] volume = new Voxel_t[width, height, depth];

            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            Vector2 uv00 = Vector2.zero;
            int[] indices = mesh.triangles;
            Vector3 direction = Vector3.forward;
            Vector3 voxelSize = Vector3.one * unit;

            for (int i = 0; i < indices.Length; i += 3)
            {
                Triangle tri = new Triangle(
                    vertices[indices[i]],
                    vertices[indices[i + 1]],
                    vertices[indices[i + 2]],
                    direction);

                Vector2 uva;
                Vector2 uvb;
                Vector2 uvc;
                if (uvs.Length > 0)
                {
                    uva = uvs[indices[i]];
                    uvb = uvs[indices[i + 1]];
                    uvc = uvs[indices[i + 2]];
                }
                else
                {
                    uva = uv00;
                    uvb = uv00;
                    uvc = uv00;
                }

                Vector3 min = tri.bounds.min - scanOrigin;
                Vector3 max = tri.bounds.max - scanOrigin;
                int iminX = Mathf.Clamp(Mathf.RoundToInt(min.x / unit), 0, width - 1);
                int iminY = Mathf.Clamp(Mathf.RoundToInt(min.y / unit), 0, height - 1);
                int iminZ = Mathf.Clamp(Mathf.RoundToInt(min.z / unit), 0, depth - 1);
                int imaxX = Mathf.Clamp(Mathf.RoundToInt(max.x / unit), 0, width - 1);
                int imaxY = Mathf.Clamp(Mathf.RoundToInt(max.y / unit), 0, height - 1);
                int imaxZ = Mathf.Clamp(Mathf.RoundToInt(max.z / unit), 0, depth - 1);

                uint front = tri.frontFacing ? 1u : 0u;
                for (int x = iminX; x <= imaxX; x++)
                {
                    for (int y = iminY; y <= imaxY; y++)
                    {
                        for (int z = iminZ; z <= imaxZ; z++)
                        {
                            Bounds aabb = new Bounds(GetCellCenter(paddedMin, unit, x, y, z), voxelSize);
                            if (!Intersects(tri, aabb))
                            {
                                continue;
                            }

                            Voxel_t voxel = volume[x, y, z];
                            voxel.position = aabb.center;
                            voxel.uv = tri.GetUV(voxel.position, uva, uvb, uvc);
                            voxel.front = (voxel.fill & 1u) == 0u ? front : voxel.front & front;
                            voxel.fill |= 1u;
                            volume[x, y, z] = voxel;
                        }
                    }
                }
            }

            FillVolumeInterior(volume, paddedMin, unit, width, height, depth);
            return new CpuVoxelVolume(paddedMin, unit, volume);
        }

        /// <summary>
        /// [INTEGRATION] Legacy list-based voxelization entry point used by demos and compatibility callers.
        /// </summary>
        public static void Voxelize(Mesh mesh, int resolution, out List<Voxel_t> voxels, out float unit)
        {
            CpuVoxelVolume volume = VoxelizeToVolume(mesh, resolution);
            voxels = volume.CollectFilledVoxels();
            unit = volume.UnitLength;
        }

        public static bool Intersects(Triangle tri, Bounds aabb)
        {
            float p0;
            float p1;
            float p2;
            float r;

            Vector3 center = aabb.center;
            Vector3 extents = aabb.max - center;

            Vector3 v0 = tri.a - center;
            Vector3 v1 = tri.b - center;
            Vector3 v2 = tri.c - center;

            Vector3 f0 = v1 - v0;
            Vector3 f1 = v2 - v1;
            Vector3 f2 = v0 - v2;

            Vector3 a00 = new Vector3(0f, -f0.z, f0.y);
            Vector3 a01 = new Vector3(0f, -f1.z, f1.y);
            Vector3 a02 = new Vector3(0f, -f2.z, f2.y);
            Vector3 a10 = new Vector3(f0.z, 0f, -f0.x);
            Vector3 a11 = new Vector3(f1.z, 0f, -f1.x);
            Vector3 a12 = new Vector3(f2.z, 0f, -f2.x);
            Vector3 a20 = new Vector3(-f0.y, f0.x, 0f);
            Vector3 a21 = new Vector3(-f1.y, f1.x, 0f);
            Vector3 a22 = new Vector3(-f2.y, f2.x, 0f);

            p0 = Vector3.Dot(v0, a00);
            p1 = Vector3.Dot(v1, a00);
            p2 = Vector3.Dot(v2, a00);
            r = extents.y * Mathf.Abs(f0.z) + extents.z * Mathf.Abs(f0.y);
            if (Mathf.Max(-Mathf.Max(p0, p1, p2), Mathf.Min(p0, p1, p2)) > r)
            {
                return false;
            }

            p0 = Vector3.Dot(v0, a01);
            p1 = Vector3.Dot(v1, a01);
            p2 = Vector3.Dot(v2, a01);
            r = extents.y * Mathf.Abs(f1.z) + extents.z * Mathf.Abs(f1.y);
            if (Mathf.Max(-Mathf.Max(p0, p1, p2), Mathf.Min(p0, p1, p2)) > r)
            {
                return false;
            }

            p0 = Vector3.Dot(v0, a02);
            p1 = Vector3.Dot(v1, a02);
            p2 = Vector3.Dot(v2, a02);
            r = extents.y * Mathf.Abs(f2.z) + extents.z * Mathf.Abs(f2.y);
            if (Mathf.Max(-Mathf.Max(p0, p1, p2), Mathf.Min(p0, p1, p2)) > r)
            {
                return false;
            }

            p0 = Vector3.Dot(v0, a10);
            p1 = Vector3.Dot(v1, a10);
            p2 = Vector3.Dot(v2, a10);
            r = extents.x * Mathf.Abs(f0.z) + extents.z * Mathf.Abs(f0.x);
            if (Mathf.Max(-Mathf.Max(p0, p1, p2), Mathf.Min(p0, p1, p2)) > r)
            {
                return false;
            }

            p0 = Vector3.Dot(v0, a11);
            p1 = Vector3.Dot(v1, a11);
            p2 = Vector3.Dot(v2, a11);
            r = extents.x * Mathf.Abs(f1.z) + extents.z * Mathf.Abs(f1.x);
            if (Mathf.Max(-Mathf.Max(p0, p1, p2), Mathf.Min(p0, p1, p2)) > r)
            {
                return false;
            }

            p0 = Vector3.Dot(v0, a12);
            p1 = Vector3.Dot(v1, a12);
            p2 = Vector3.Dot(v2, a12);
            r = extents.x * Mathf.Abs(f2.z) + extents.z * Mathf.Abs(f2.x);
            if (Mathf.Max(-Mathf.Max(p0, p1, p2), Mathf.Min(p0, p1, p2)) > r)
            {
                return false;
            }

            p0 = Vector3.Dot(v0, a20);
            p1 = Vector3.Dot(v1, a20);
            p2 = Vector3.Dot(v2, a20);
            r = extents.x * Mathf.Abs(f0.y) + extents.y * Mathf.Abs(f0.x);
            if (Mathf.Max(-Mathf.Max(p0, p1, p2), Mathf.Min(p0, p1, p2)) > r)
            {
                return false;
            }

            p0 = Vector3.Dot(v0, a21);
            p1 = Vector3.Dot(v1, a21);
            p2 = Vector3.Dot(v2, a21);
            r = extents.x * Mathf.Abs(f1.y) + extents.y * Mathf.Abs(f1.x);
            if (Mathf.Max(-Mathf.Max(p0, p1, p2), Mathf.Min(p0, p1, p2)) > r)
            {
                return false;
            }

            p0 = Vector3.Dot(v0, a22);
            p1 = Vector3.Dot(v1, a22);
            p2 = Vector3.Dot(v2, a22);
            r = extents.x * Mathf.Abs(f2.y) + extents.y * Mathf.Abs(f2.x);
            if (Mathf.Max(-Mathf.Max(p0, p1, p2), Mathf.Min(p0, p1, p2)) > r)
            {
                return false;
            }

            if (Mathf.Max(v0.x, v1.x, v2.x) < -extents.x || Mathf.Min(v0.x, v1.x, v2.x) > extents.x)
            {
                return false;
            }

            if (Mathf.Max(v0.y, v1.y, v2.y) < -extents.y || Mathf.Min(v0.y, v1.y, v2.y) > extents.y)
            {
                return false;
            }

            if (Mathf.Max(v0.z, v1.z, v2.z) < -extents.z || Mathf.Min(v0.z, v1.z, v2.z) > extents.z)
            {
                return false;
            }

            Vector3 normal = Vector3.Cross(f1, f0).normalized;
            Plane plane = new Plane(normal, Vector3.Dot(normal, tri.a));
            return Intersects(plane, aabb);
        }

        public static bool Intersects(Plane pl, Bounds aabb)
        {
            Vector3 center = aabb.center;
            Vector3 extents = aabb.max - center;

            float r = extents.x * Mathf.Abs(pl.normal.x) + extents.y * Mathf.Abs(pl.normal.y) + extents.z * Mathf.Abs(pl.normal.z);
            float s = Vector3.Dot(pl.normal, center) - pl.distance;

            return Mathf.Abs(s) <= r;
        }

        private static void FillVolumeInterior(Voxel_t[,,] volume, Vector3 paddedMin, float unit, int width, int height, int depth)
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int z = 0; z < depth; z++)
                    {
                        if (volume[x, y, z].IsEmpty())
                        {
                            continue;
                        }

                        int ifront = z;
                        Vector2 uv = Vector2.zero;
                        for (; ifront < depth; ifront++)
                        {
                            if (!volume[x, y, ifront].IsFrontFace())
                            {
                                break;
                            }

                            uv = volume[x, y, ifront].uv;
                        }

                        if (ifront >= depth)
                        {
                            break;
                        }

                        int iback = ifront;
                        for (; iback < depth && volume[x, y, iback].IsEmpty(); iback++)
                        {
                        }

                        if (iback >= depth)
                        {
                            break;
                        }

                        if (volume[x, y, iback].IsBackFace())
                        {
                            for (; iback < depth && volume[x, y, iback].IsBackFace(); iback++)
                            {
                            }
                        }

                        for (int z2 = ifront; z2 < iback; z2++)
                        {
                            Voxel_t voxel = volume[x, y, z2];
                            voxel.position = GetCellCenter(paddedMin, unit, x, y, z2);
                            voxel.uv = uv;
                            voxel.fill = 1u;
                            volume[x, y, z2] = voxel;
                        }

                        z = iback;
                    }
                }
            }
        }

        private static Vector3 GetCellCenter(Vector3 paddedMin, float unit, int x, int y, int z)
        {
            return paddedMin + Vector3.Scale(Vector3.one * unit, new Vector3(x + 0.5f, y + 0.5f, z + 0.5f));
        }
    }
}
