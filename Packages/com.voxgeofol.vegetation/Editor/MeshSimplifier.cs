#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Editor-only mesh simplifier for generated shell and impostor geometry.
    /// </summary>
    public static class MeshSimplifier
    {
        private const float MinimumBoundsSize = 0.001f;

        /// <summary>
        /// [INTEGRATION] Reduces one readable mesh toward a triangle budget for editor-generated vegetation geometry.
        /// </summary>
        public static Mesh Simplify(Mesh sourceMesh, int targetTriangleCount, float weldThreshold = 0f)
        {
            // Range: readable generated meshes only. Condition: simplification prefers deterministic vertex clustering over expensive editor-time decimation. Output: readable mesh that stays at or below target when possible.
            if (sourceMesh == null)
            {
                throw new ArgumentNullException(nameof(sourceMesh));
            }

            if (!sourceMesh.isReadable)
            {
                throw new InvalidOperationException($"{sourceMesh.name} must be readable before simplification.");
            }

            if (targetTriangleCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetTriangleCount), "Target triangle count must be greater than zero.");
            }

            Mesh bestMesh = CloneMesh(sourceMesh);
            if (GetTriangleCount(bestMesh) <= targetTriangleCount)
            {
                if (weldThreshold <= 0f)
                {
                    return bestMesh;
                }

                Mesh weldedMesh = WeldVertices(bestMesh, weldThreshold);
                UnityEngine.Object.DestroyImmediate(bestMesh);
                return weldedMesh;
            }

            int[] gridResolutions = BuildGridResolutions(sourceMesh.vertexCount);
            for (int i = 0; i < gridResolutions.Length; i++)
            {
                Mesh candidate = SimplifyByVertexClustering(sourceMesh, gridResolutions[i]);
                if (weldThreshold > 0f)
                {
                    Mesh weldedCandidate = WeldVertices(candidate, weldThreshold);
                    UnityEngine.Object.DestroyImmediate(candidate);
                    candidate = weldedCandidate;
                }

                if (GetTriangleCount(candidate) < GetTriangleCount(bestMesh))
                {
                    UnityEngine.Object.DestroyImmediate(bestMesh);
                    bestMesh = candidate;
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(candidate);
                }

                if (GetTriangleCount(bestMesh) <= targetTriangleCount)
                {
                    break;
                }
            }

            return bestMesh;
        }

        private static Mesh SimplifyByVertexClustering(Mesh sourceMesh, int gridResolution)
        {
            Vector3[] sourceVertices = sourceMesh.vertices;
            int[] sourceTriangles = sourceMesh.triangles;
            Bounds normalizedBounds = NormalizeBounds(sourceMesh.bounds);
            Vector3 boundsMin = normalizedBounds.min;
            Vector3 boundsSize = normalizedBounds.size;

            Dictionary<long, ClusterAccumulator> accumulators = new Dictionary<long, ClusterAccumulator>(sourceVertices.Length);
            for (int vertexIndex = 0; vertexIndex < sourceVertices.Length; vertexIndex++)
            {
                long clusterKey = ComputeClusterKey(sourceVertices[vertexIndex], boundsMin, boundsSize, gridResolution);
                if (accumulators.TryGetValue(clusterKey, out ClusterAccumulator accumulator))
                {
                    accumulator.Add(sourceVertices[vertexIndex]);
                    accumulators[clusterKey] = accumulator;
                }
                else
                {
                    accumulators.Add(clusterKey, new ClusterAccumulator(sourceVertices[vertexIndex]));
                }
            }

            List<Vector3> simplifiedVertices = new List<Vector3>(accumulators.Count);
            Dictionary<long, int> clusterVertexIndices = new Dictionary<long, int>(accumulators.Count);
            int[] oldToNew = new int[sourceVertices.Length];
            for (int vertexIndex = 0; vertexIndex < sourceVertices.Length; vertexIndex++)
            {
                long clusterKey = ComputeClusterKey(sourceVertices[vertexIndex], boundsMin, boundsSize, gridResolution);
                if (!clusterVertexIndices.TryGetValue(clusterKey, out int simplifiedIndex))
                {
                    simplifiedIndex = simplifiedVertices.Count;
                    simplifiedVertices.Add(accumulators[clusterKey].Average);
                    clusterVertexIndices.Add(clusterKey, simplifiedIndex);
                }

                oldToNew[vertexIndex] = simplifiedIndex;
            }

            List<int> simplifiedTriangles = new List<int>(sourceTriangles.Length);
            HashSet<long> emittedTriangles = new HashSet<long>();
            for (int triangleIndex = 0; triangleIndex < sourceTriangles.Length; triangleIndex += 3)
            {
                int a = oldToNew[sourceTriangles[triangleIndex]];
                int b = oldToNew[sourceTriangles[triangleIndex + 1]];
                int c = oldToNew[sourceTriangles[triangleIndex + 2]];
                if (a == b || b == c || c == a)
                {
                    continue;
                }

                long triangleKey = ComputeTriangleKey(a, b, c);
                if (!emittedTriangles.Add(triangleKey))
                {
                    continue;
                }

                simplifiedTriangles.Add(a);
                simplifiedTriangles.Add(b);
                simplifiedTriangles.Add(c);
            }

            Mesh simplifiedMesh = CreateMesh("VegetationSimplifiedMesh", simplifiedVertices, simplifiedTriangles);
            if (simplifiedMesh.vertexCount == 0 || GetTriangleCount(simplifiedMesh) == 0)
            {
                UnityEngine.Object.DestroyImmediate(simplifiedMesh);
                return CloneMesh(sourceMesh);
            }

            return simplifiedMesh;
        }

        private static Mesh WeldVertices(Mesh sourceMesh, float weldThreshold)
        {
            if (weldThreshold <= 0f)
            {
                return CloneMesh(sourceMesh);
            }

            Vector3[] sourceVertices = sourceMesh.vertices;
            int[] sourceTriangles = sourceMesh.triangles;
            Dictionary<long, int> weldedIndices = new Dictionary<long, int>(sourceVertices.Length);
            List<Vector3> weldedVertices = new List<Vector3>(sourceVertices.Length);
            int[] oldToNew = new int[sourceVertices.Length];

            float inverseThreshold = 1f / weldThreshold;
            for (int vertexIndex = 0; vertexIndex < sourceVertices.Length; vertexIndex++)
            {
                Vector3 vertex = sourceVertices[vertexIndex];
                int quantizedX = Mathf.RoundToInt(vertex.x * inverseThreshold);
                int quantizedY = Mathf.RoundToInt(vertex.y * inverseThreshold);
                int quantizedZ = Mathf.RoundToInt(vertex.z * inverseThreshold);
                long weldKey = ((long)(quantizedX & 0x1FFFFF) << 42) |
                               ((long)(quantizedY & 0x1FFFFF) << 21) |
                               (uint)(quantizedZ & 0x1FFFFF);

                if (!weldedIndices.TryGetValue(weldKey, out int weldedIndex))
                {
                    weldedIndex = weldedVertices.Count;
                    weldedVertices.Add(vertex);
                    weldedIndices.Add(weldKey, weldedIndex);
                }

                oldToNew[vertexIndex] = weldedIndex;
            }

            List<int> weldedTriangles = new List<int>(sourceTriangles.Length);
            HashSet<long> emittedTriangles = new HashSet<long>();
            for (int triangleIndex = 0; triangleIndex < sourceTriangles.Length; triangleIndex += 3)
            {
                int a = oldToNew[sourceTriangles[triangleIndex]];
                int b = oldToNew[sourceTriangles[triangleIndex + 1]];
                int c = oldToNew[sourceTriangles[triangleIndex + 2]];
                if (a == b || b == c || c == a)
                {
                    continue;
                }

                long triangleKey = ComputeTriangleKey(a, b, c);
                if (!emittedTriangles.Add(triangleKey))
                {
                    continue;
                }

                weldedTriangles.Add(a);
                weldedTriangles.Add(b);
                weldedTriangles.Add(c);
            }

            Mesh weldedMesh = CreateMesh("VegetationWeldedMesh", weldedVertices, weldedTriangles);
            if (weldedMesh.vertexCount == 0 || GetTriangleCount(weldedMesh) == 0)
            {
                UnityEngine.Object.DestroyImmediate(weldedMesh);
                return CloneMesh(sourceMesh);
            }

            return weldedMesh;
        }

        private static Mesh CreateMesh(string meshName, List<Vector3> vertices, List<int> triangles)
        {
            CompactMeshBuffers(vertices, triangles, out List<Vector3> compactVertices, out List<int> compactTriangles);
            Mesh mesh = new Mesh
            {
                name = meshName,
                indexFormat = compactVertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };

            mesh.SetVertices(compactVertices);
            mesh.SetTriangles(compactTriangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EnsurePositiveVolume(mesh);
            return mesh;
        }

        private static void CompactMeshBuffers(
            List<Vector3> vertices,
            List<int> triangles,
            out List<Vector3> compactVertices,
            out List<int> compactTriangles)
        {
            Dictionary<int, int> remappedIndices = new Dictionary<int, int>();
            compactVertices = new List<Vector3>(vertices.Count);
            compactTriangles = new List<int>(triangles.Count);

            for (int triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
            {
                int sourceIndex = triangles[triangleIndex];
                if (!remappedIndices.TryGetValue(sourceIndex, out int compactIndex))
                {
                    compactIndex = compactVertices.Count;
                    compactVertices.Add(vertices[sourceIndex]);
                    remappedIndices.Add(sourceIndex, compactIndex);
                }

                compactTriangles.Add(compactIndex);
            }
        }

        private static void EnsurePositiveVolume(Mesh mesh)
        {
            float signedVolume = ComputeSignedVolume(mesh);
            if (signedVolume >= 0f)
            {
                return;
            }

            int[] triangles = mesh.triangles;
            for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex += 3)
            {
                int temp = triangles[triangleIndex + 1];
                triangles[triangleIndex + 1] = triangles[triangleIndex + 2];
                triangles[triangleIndex + 2] = temp;
            }

            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private static float ComputeSignedVolume(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            float signedVolume = 0f;
            for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex += 3)
            {
                Vector3 a = vertices[triangles[triangleIndex]];
                Vector3 b = vertices[triangles[triangleIndex + 1]];
                Vector3 c = vertices[triangles[triangleIndex + 2]];
                signedVolume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6f;
            }

            return signedVolume;
        }

        private static Mesh CloneMesh(Mesh sourceMesh)
        {
            Mesh clone = new Mesh
            {
                name = $"{sourceMesh.name}_Clone",
                indexFormat = sourceMesh.vertexCount > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };

            clone.vertices = sourceMesh.vertices;
            clone.triangles = sourceMesh.triangles;

            Vector3[] normals = sourceMesh.normals;
            if (normals.Length == sourceMesh.vertexCount)
            {
                clone.normals = normals;
            }
            else
            {
                clone.RecalculateNormals();
            }

            Vector2[] uv = sourceMesh.uv;
            if (uv.Length == sourceMesh.vertexCount)
            {
                clone.uv = uv;
            }

            Color[] colors = sourceMesh.colors;
            if (colors.Length == sourceMesh.vertexCount)
            {
                clone.colors = colors;
            }

            clone.RecalculateBounds();
            return clone;
        }

        private static int[] BuildGridResolutions(int vertexCount)
        {
            List<int> resolutions = new List<int>();
            int startResolution = Mathf.Clamp(Mathf.CeilToInt(Mathf.Pow(Mathf.Max(8, vertexCount), 1f / 3f)), 4, 32);
            int[] candidates = { startResolution, 24, 20, 16, 12, 10, 8, 6, 5, 4, 3, 2 };

            for (int i = 0; i < candidates.Length; i++)
            {
                int candidate = candidates[i];
                if (!resolutions.Contains(candidate))
                {
                    resolutions.Add(candidate);
                }
            }

            return resolutions.ToArray();
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

        private static long ComputeClusterKey(Vector3 vertex, Vector3 boundsMin, Vector3 boundsSize, int gridResolution)
        {
            float normalizedX = Mathf.InverseLerp(boundsMin.x, boundsMin.x + boundsSize.x, vertex.x);
            float normalizedY = Mathf.InverseLerp(boundsMin.y, boundsMin.y + boundsSize.y, vertex.y);
            float normalizedZ = Mathf.InverseLerp(boundsMin.z, boundsMin.z + boundsSize.z, vertex.z);

            int x = Mathf.Clamp(Mathf.FloorToInt(normalizedX * gridResolution), 0, gridResolution - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(normalizedY * gridResolution), 0, gridResolution - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(normalizedZ * gridResolution), 0, gridResolution - 1);

            return x + (y * 128L) + (z * 16384L);
        }

        private static long ComputeTriangleKey(int a, int b, int c)
        {
            int min = Mathf.Min(a, Mathf.Min(b, c));
            int max = Mathf.Max(a, Mathf.Max(b, c));
            int mid = (a + b + c) - min - max;
            return ((long)min << 42) | ((long)mid << 21) | (uint)max;
        }

        private static int GetTriangleCount(Mesh mesh)
        {
            return mesh.triangles.Length / 3;
        }

        private struct ClusterAccumulator
        {
            private Vector3 positionSum;
            private int sampleCount;

            public ClusterAccumulator(Vector3 initialPosition)
            {
                positionSum = initialPosition;
                sampleCount = 1;
            }

            public Vector3 Average => positionSum / sampleCount;

            public void Add(Vector3 position)
            {
                positionSum += position;
                sampleCount++;
            }
        }
    }
}
