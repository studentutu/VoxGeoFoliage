#nullable enable

using System;
using UnityEditor;
using UnityEngine;
using VoxelSystem;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Shared editor-side helpers for generated vegetation mesh simplification and fallback extraction.
    /// </summary>
    internal static class GeneratedMeshSimplificationUtility
    {
        private static readonly int[] MeshLodFallbackLimits = { 2, 4, 6 };

        internal sealed class GeneratedMeshCandidate
        {
            public GeneratedMeshCandidate(Mesh mesh, int triangleCount, string sourceDescription)
            {
                Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
                TriangleCount = triangleCount;
                SourceDescription = sourceDescription ?? throw new ArgumentNullException(nameof(sourceDescription));
            }

            public Mesh Mesh { get; }

            public int TriangleCount { get; }

            public string SourceDescription { get; }
        }

        public static Mesh BuildVoxelSurfaceMesh(
            CpuVoxelVolume volume,
            Bounds? ownedBounds,
            string meshName,
            bool skipReduction)
        {
            CpuVoxelSurfaceBuildOptions buildOptions = skipReduction
                ? CpuVoxelSurfaceBuildOptions.Raw
                : CpuVoxelSurfaceBuildOptions.Reduced;
            return CpuVoxelSurfaceMeshBuilder.BuildSurfaceMesh(volume, ownedBounds, meshName, buildOptions);
        }

        public static Mesh BuildVoxelSurfaceMesh(
            Mesh sourceMesh,
            int resolution,
            string meshName,
            bool skipReduction)
        {
            CpuVoxelVolume volume = CPUVoxelizer.VoxelizeToVolume(sourceMesh, resolution);
            return BuildVoxelSurfaceMesh(volume, null, meshName, skipReduction);
        }

        public static GeneratedMeshCandidate SelectBestVoxelMeshCandidate(
            Mesh sourceMesh,
            int initialResolution,
            int minimumResolution,
            int resolutionStep,
            int triangleBudget,
            bool skipReduction,
            bool skipSimplifyFallback,
            string meshName)
        {
            if (sourceMesh == null)
            {
                throw new ArgumentNullException(nameof(sourceMesh));
            }

            GeneratedMeshCandidate bestCandidate =
                BuildVoxelMeshCandidate(sourceMesh, initialResolution, meshName, skipReduction);
            if (bestCandidate.TriangleCount <= triangleBudget || skipSimplifyFallback)
            {
                return bestCandidate;
            }

            int currentResolution = initialResolution;
            for (int retry = 0; retry < 2; retry++)
            {
                int nextResolution = Mathf.Max(minimumResolution, currentResolution - resolutionStep);
                if (nextResolution >= currentResolution)
                {
                    break;
                }

                GeneratedMeshCandidate retryCandidate =
                    BuildVoxelMeshCandidate(sourceMesh, nextResolution, meshName, skipReduction);
                bestCandidate = SelectBetterCandidate(bestCandidate, retryCandidate, triangleBudget);
                if (bestCandidate.TriangleCount <= triangleBudget)
                {
                    return bestCandidate;
                }

                currentResolution = nextResolution;
            }

            for (int i = 0; i < MeshLodFallbackLimits.Length; i++)
            {
                int lodLimit = MeshLodFallbackLimits[i];
                Mesh? lodMesh = CreateMeshLodFallbackMesh(bestCandidate.Mesh, $"{meshName}_LOD{lodLimit}", lodLimit);
                if (lodMesh == null)
                {
                    continue;
                }

                GeneratedMeshCandidate lodCandidate = new GeneratedMeshCandidate(
                    lodMesh,
                    GetTriangleCount(lodMesh),
                    $"MeshLodUtility LOD{lodLimit}");
                bestCandidate = SelectBetterCandidate(bestCandidate, lodCandidate, triangleBudget);
                if (bestCandidate.TriangleCount <= triangleBudget)
                {
                    return bestCandidate;
                }
            }

            return bestCandidate;
        }

        public static Mesh? CreateMeshLodFallbackMesh(Mesh sourceMesh, string meshName, int meshLodLimit)
        {
            if (sourceMesh == null)
            {
                throw new ArgumentNullException(nameof(sourceMesh));
            }

            if (!CanGenerateMeshLod(sourceMesh))
            {
                return null;
            }

            Mesh lodMesh = CreateMeshLodInputMesh(sourceMesh, $"{meshName}_MeshLod{meshLodLimit}");
            try
            {
                MeshLodUtility.GenerateMeshLods(
                    lodMesh,
                    MeshLodUtility.LodGenerationFlags.DiscardOddLevels,
                    meshLodLimit);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Skipping MeshLodUtility fallback for {sourceMesh.name}: {exception.Message}");
                UnityEngine.Object.DestroyImmediate(lodMesh);
                return null;
            }

            int selectedSubMesh = GetLastNonEmptySubMeshIndex(lodMesh);
            if (selectedSubMesh < 0)
            {
                UnityEngine.Object.DestroyImmediate(lodMesh);
                return null;
            }

            Mesh extractedMesh = ExtractSubMesh(lodMesh, selectedSubMesh, meshName);
            UnityEngine.Object.DestroyImmediate(lodMesh);
            return extractedMesh;
        }

        public static int GetTriangleCount(Mesh? mesh)
        {
            if (mesh == null || mesh.subMeshCount <= 0)
            {
                return 0;
            }

            int triangleCount = 0;
            for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
            {
                triangleCount = checked(triangleCount + (int)(mesh.GetIndexCount(subMeshIndex) / 3L));
            }

            return triangleCount;
        }

        public static bool CanGenerateMeshLod(Mesh? mesh)
        {
            if (mesh == null || !mesh.isReadable || mesh.vertexCount <= 0 || mesh.subMeshCount <= 0)
            {
                return false;
            }

            for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
            {
                if (mesh.GetIndexCount(subMeshIndex) > 0)
                {
                    return true;
                }
            }

            return false;
        }

        public static void DestroyTemporaryMesh(Mesh? mesh)
        {
            if (mesh != null && !EditorUtility.IsPersistent(mesh))
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static GeneratedMeshCandidate BuildVoxelMeshCandidate(
            Mesh sourceMesh,
            int resolution,
            string meshName,
            bool skipReduction)
        {
            Mesh mesh = BuildVoxelSurfaceMesh(sourceMesh, resolution, $"{meshName}_{resolution}", skipReduction);
            return new GeneratedMeshCandidate(mesh, GetTriangleCount(mesh), $"voxel resolution {resolution}");
        }

        private static GeneratedMeshCandidate SelectBetterCandidate(
            GeneratedMeshCandidate bestCandidate,
            GeneratedMeshCandidate candidate,
            int triangleBudget)
        {
            bool candidatePasses = candidate.TriangleCount <= triangleBudget;
            bool bestPasses = bestCandidate.TriangleCount <= triangleBudget;
            bool replaceBest = false;

            if (candidatePasses && !bestPasses)
            {
                replaceBest = true;
            }
            else if (candidatePasses == bestPasses && candidate.TriangleCount < bestCandidate.TriangleCount)
            {
                replaceBest = true;
            }

            if (!replaceBest)
            {
                DestroyTemporaryMesh(candidate.Mesh);
                return bestCandidate;
            }

            DestroyTemporaryMesh(bestCandidate.Mesh);
            return candidate;
        }

        private static int GetLastNonEmptySubMeshIndex(Mesh mesh)
        {
            for (int subMeshIndex = mesh.subMeshCount - 1; subMeshIndex >= 0; subMeshIndex--)
            {
                if (mesh.GetIndexCount(subMeshIndex) > 0)
                {
                    return subMeshIndex;
                }
            }

            return -1;
        }

        private static Mesh CreateMeshLodInputMesh(Mesh sourceMesh, string meshName)
        {
            Mesh lodInputMesh = new Mesh
            {
                name = meshName,
                indexFormat = sourceMesh.indexFormat
            };

            CopyMeshAttributes(sourceMesh, lodInputMesh);
            lodInputMesh.subMeshCount = sourceMesh.subMeshCount;
            for (int subMeshIndex = 0; subMeshIndex < sourceMesh.subMeshCount; subMeshIndex++)
            {
                lodInputMesh.SetTriangles(sourceMesh.GetTriangles(subMeshIndex), subMeshIndex, true);
            }

            lodInputMesh.RecalculateBounds();
            if (CanGenerateMeshLod(lodInputMesh) && lodInputMesh.normals.Length != lodInputMesh.vertexCount)
            {
                lodInputMesh.RecalculateNormals();
            }

            return lodInputMesh;
        }

        private static Mesh ExtractSubMesh(Mesh sourceMesh, int subMeshIndex, string meshName)
        {
            Mesh extractedMesh = new Mesh
            {
                name = meshName,
                indexFormat = sourceMesh.indexFormat
            };

            CopyMeshAttributes(sourceMesh, extractedMesh);
            extractedMesh.SetTriangles(sourceMesh.GetTriangles(subMeshIndex), 0, true);
            extractedMesh.RecalculateBounds();
            if (extractedMesh.GetIndexCount(0) > 0 && extractedMesh.normals.Length != extractedMesh.vertexCount)
            {
                extractedMesh.RecalculateNormals();
            }

            return extractedMesh;
        }

        private static void CopyMeshAttributes(Mesh sourceMesh, Mesh targetMesh)
        {
            targetMesh.vertices = sourceMesh.vertices;

            Vector3[] normals = sourceMesh.normals;
            if (normals.Length == sourceMesh.vertexCount)
            {
                targetMesh.normals = normals;
            }

            Vector4[] tangents = sourceMesh.tangents;
            if (tangents.Length == sourceMesh.vertexCount)
            {
                targetMesh.tangents = tangents;
            }

            Vector2[] uv = sourceMesh.uv;
            if (uv.Length == sourceMesh.vertexCount)
            {
                targetMesh.uv = uv;
            }

            Vector2[] uv2 = sourceMesh.uv2;
            if (uv2.Length == sourceMesh.vertexCount)
            {
                targetMesh.uv2 = uv2;
            }

            Vector2[] uv3 = sourceMesh.uv3;
            if (uv3.Length == sourceMesh.vertexCount)
            {
                targetMesh.uv3 = uv3;
            }

            Vector2[] uv4 = sourceMesh.uv4;
            if (uv4.Length == sourceMesh.vertexCount)
            {
                targetMesh.uv4 = uv4;
            }

            Color[] colors = sourceMesh.colors;
            if (colors.Length == sourceMesh.vertexCount)
            {
                targetMesh.colors = colors;
            }

            Color32[] colors32 = sourceMesh.colors32;
            if (colors32.Length == sourceMesh.vertexCount)
            {
                targetMesh.colors32 = colors32;
            }
        }
    }
}
