#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Persists generated vegetation meshes as standalone native mesh assets when the owning asset is saved on disk.
    /// </summary>
    public static class GeneratedMeshAssetUtility
    {
        private const string GeneratedMeshFolderName = "GeneratedMeshes";
        private const string FallbackGeneratedMeshFolderPath = "Assets/VoxGeoFol.Generated/Vegetation/Meshes";

        /// <summary>
        /// [INTEGRATION] Reuses or creates a generated mesh asset under a writable project folder.
        /// </summary>
        public static Mesh PersistGeneratedMesh(UnityEngine.Object ownerAsset, string meshName, Mesh generatedMesh, string? relativeFolderToSave = null)
        {
            // Range: owner asset can be transient or saved. Condition: generated mesh already contains the final authored shape, but this utility may still strip degenerate faces and rebuild stable hard normals before persistence. Output: explicit .mesh asset in the requested Assets folder when provided, otherwise beside the owner asset or under a writable fallback folder.
            if (ownerAsset == null)
            {
                throw new ArgumentNullException(nameof(ownerAsset));
            }

            if (generatedMesh == null)
            {
                throw new ArgumentNullException(nameof(generatedMesh));
            }

            generatedMesh.name = meshName;
            RefreshMeshForPersistence(generatedMesh);
            string assetPath = AssetDatabase.GetAssetPath(ownerAsset);
            if (string.IsNullOrEmpty(assetPath))
            {
                return generatedMesh;
            }

            string meshFolderPath = ResolveWritableMeshFolderPath(assetPath, relativeFolderToSave);
            EnsureFolderPath(meshFolderPath);
            string meshAssetPath = BuildGeneratedMeshAssetPath(assetPath, meshFolderPath, meshName);
            Mesh? existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);
            if (existingMesh == null)
            {
                AssetDatabase.CreateAsset(generatedMesh, meshAssetPath);
                EditorUtility.SetDirty(generatedMesh);
                EditorUtility.SetDirty(ownerAsset);
                return generatedMesh;
            }

            if (ReferenceEquals(existingMesh, generatedMesh))
            {
                EditorUtility.SetDirty(existingMesh);
                EditorUtility.SetDirty(ownerAsset);
                return existingMesh;
            }

            CopyMeshData(generatedMesh, existingMesh);
            if (!EditorUtility.IsPersistent(generatedMesh))
            {
                UnityEngine.Object.DestroyImmediate(generatedMesh);
            }

            EditorUtility.SetDirty(existingMesh);
            EditorUtility.SetDirty(ownerAsset);
            return existingMesh;
        }
        
        /// <summary>
        /// [INTEGRATION] Simple save mesh utility.
        /// </summary>
        public static Mesh SaveMeshAsCopyTo(string meshName, Mesh generatedMesh, string relativeFolderToSave)
        {
            if (generatedMesh == null)
            {
                throw new ArgumentNullException(nameof(generatedMesh));
            }

            generatedMesh.name = meshName;
            RefreshMeshForPersistence(generatedMesh);

            string meshFolderPath = ResolveExplicitFolderPath(relativeFolderToSave);
            EnsureFolderPath(meshFolderPath);
            string sanitizedName = SanitizeFileName($"{meshName}");
            string meshAssetPath = $"{meshFolderPath}/{sanitizedName}.mesh";
            Mesh? existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);
            if (existingMesh == null)
            {
                AssetDatabase.CreateAsset(generatedMesh, meshAssetPath);
                EditorUtility.SetDirty(generatedMesh);
                return generatedMesh;
            }

            CopyMeshData(generatedMesh, existingMesh);
            EditorUtility.SetDirty(existingMesh);
            return existingMesh;
        }

        private static string ResolveWritableMeshFolderPath(string ownerAssetPath, string? relativeFolderToSave)
        {
            string explicitFolderPath = ResolveExplicitFolderPath(relativeFolderToSave);
            if (!string.IsNullOrEmpty(explicitFolderPath))
            {
                return explicitFolderPath;
            }

            string normalizedOwnerAssetPath = NormalizeAssetPath(ownerAssetPath);
            if (normalizedOwnerAssetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                string ownerFolderPath = NormalizeAssetPath(Path.GetDirectoryName(normalizedOwnerAssetPath) ?? string.Empty);
                if (!string.IsNullOrEmpty(ownerFolderPath))
                {
                    return $"{ownerFolderPath}/{GeneratedMeshFolderName}";
                }
            }

            return FallbackGeneratedMeshFolderPath;
        }

        private static string ResolveExplicitFolderPath(string? relativeFolderToSave)
        {
            if (string.IsNullOrWhiteSpace(relativeFolderToSave))
            {
                return string.Empty;
            }

            string explicitFolder = relativeFolderToSave!;
            string cleaned = NormalizeAssetPath(explicitFolder.Trim());
            if (string.IsNullOrEmpty(cleaned))
            {
                return string.Empty;
            }

            if (string.Equals(cleaned, "Assets", StringComparison.OrdinalIgnoreCase) ||
                cleaned.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return cleaned;
            }

            return $"Assets/{cleaned.TrimStart('/')}";
        }

        private static string BuildGeneratedMeshAssetPath(string ownerAssetPath, string meshFolderPath, string meshName)
        {
            string ownerGuid = AssetDatabase.AssetPathToGUID(ownerAssetPath);
            string guidSuffix = string.IsNullOrEmpty(ownerGuid)
                ? "noguid"
                : ownerGuid.Substring(0, Mathf.Min(8, ownerGuid.Length));

            string sanitizedName = SanitizeFileName($"{meshName}_{guidSuffix}");
            return $"{meshFolderPath}/{sanitizedName}.mesh";
        }

        private static void EnsureFolderPath(string folderPath)
        {
            string normalizedFolderPath = NormalizeAssetPath(folderPath);
            string[] segments = normalizedFolderPath.Split('/');
            string currentPath = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string nextPath = $"{currentPath}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, segments[i]);
                }

                currentPath = nextPath;
            }
        }

        private static string SanitizeFileName(string fileName)
        {
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            char[] sanitizedCharacters = fileName.ToCharArray();
            for (int i = 0; i < sanitizedCharacters.Length; i++)
            {
                if (Array.IndexOf(invalidCharacters, sanitizedCharacters[i]) >= 0)
                {
                    sanitizedCharacters[i] = '_';
                }
            }

            return new string(sanitizedCharacters);
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            return assetPath.Replace('\\', '/').TrimEnd('/');
        }

        private static void RefreshMeshForPersistence(Mesh mesh)
        {
            Vector3[] sourceVertices = mesh.vertices;
            int[] sourceTriangles = mesh.triangles;
            if (sourceVertices.Length == 0 || sourceTriangles.Length == 0)
            {
                mesh.normals = Array.Empty<Vector3>();
                mesh.RecalculateBounds();
                return;
            }

            Vector2[] sourceUv = mesh.uv;
            Color[] sourceColors = mesh.colors;
            Color32[] sourceColors32 = mesh.colors32;
            bool hasUv = sourceUv.Length == sourceVertices.Length;
            bool hasColors = sourceColors.Length == sourceVertices.Length;
            bool hasColors32 = sourceColors32.Length == sourceVertices.Length;

            List<Vector3> repairedVertices = new List<Vector3>(sourceTriangles.Length);
            List<int> repairedTriangles = new List<int>(sourceTriangles.Length);
            List<Vector3> repairedNormals = new List<Vector3>(sourceTriangles.Length);
            List<Vector2>? repairedUv = hasUv ? new List<Vector2>(sourceTriangles.Length) : null;
            List<Color>? repairedColors = hasColors ? new List<Color>(sourceTriangles.Length) : null;
            List<Color32>? repairedColors32 = hasColors32 ? new List<Color32>(sourceTriangles.Length) : null;

            for (int triangleIndex = 0; triangleIndex < sourceTriangles.Length; triangleIndex += 3)
            {
                int indexA = sourceTriangles[triangleIndex];
                int indexB = sourceTriangles[triangleIndex + 1];
                int indexC = sourceTriangles[triangleIndex + 2];
                if ((uint)indexA >= sourceVertices.Length ||
                    (uint)indexB >= sourceVertices.Length ||
                    (uint)indexC >= sourceVertices.Length)
                {
                    continue;
                }

                Vector3 a = sourceVertices[indexA];
                Vector3 b = sourceVertices[indexB];
                Vector3 c = sourceVertices[indexC];
                Vector3 faceCross = Vector3.Cross(b - a, c - a);
                float faceMagnitude = faceCross.magnitude;
                if (faceMagnitude <= 0.000001f)
                {
                    continue;
                }

                Vector3 faceNormal = faceCross / faceMagnitude;
                int repairedIndex = repairedVertices.Count;
                repairedVertices.Add(a);
                repairedVertices.Add(b);
                repairedVertices.Add(c);
                repairedTriangles.Add(repairedIndex);
                repairedTriangles.Add(repairedIndex + 1);
                repairedTriangles.Add(repairedIndex + 2);
                repairedNormals.Add(faceNormal);
                repairedNormals.Add(faceNormal);
                repairedNormals.Add(faceNormal);

                if (repairedUv != null)
                {
                    repairedUv.Add(sourceUv[indexA]);
                    repairedUv.Add(sourceUv[indexB]);
                    repairedUv.Add(sourceUv[indexC]);
                }

                if (repairedColors != null)
                {
                    repairedColors.Add(sourceColors[indexA]);
                    repairedColors.Add(sourceColors[indexB]);
                    repairedColors.Add(sourceColors[indexC]);
                }

                if (repairedColors32 != null)
                {
                    repairedColors32.Add(sourceColors32[indexA]);
                    repairedColors32.Add(sourceColors32[indexB]);
                    repairedColors32.Add(sourceColors32[indexC]);
                }
            }

            mesh.Clear();
            mesh.indexFormat = repairedVertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(repairedVertices);
            mesh.SetTriangles(repairedTriangles, 0, true);
            mesh.SetNormals(repairedNormals);

            if (repairedUv != null)
            {
                mesh.SetUVs(0, repairedUv);
            }

            if (repairedColors != null)
            {
                mesh.SetColors(repairedColors);
            }
            else if (repairedColors32 != null)
            {
                mesh.SetColors(repairedColors32);
            }

            if (repairedTriangles.Count == 0)
            {
                mesh.bounds = new Bounds(Vector3.zero, Vector3.zero);
                return;
            }

            mesh.RecalculateBounds();
        }

        private static void CopyMeshData(Mesh sourceMesh, Mesh destinationMesh)
        {
            destinationMesh.Clear();
            destinationMesh.name = sourceMesh.name;
            destinationMesh.indexFormat = sourceMesh.vertexCount > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            destinationMesh.vertices = sourceMesh.vertices;
            destinationMesh.triangles = sourceMesh.triangles;

            Vector3[] normals = sourceMesh.normals;
            if (normals.Length == sourceMesh.vertexCount)
            {
                destinationMesh.normals = normals;
            }
            else
            {
                destinationMesh.RecalculateNormals();
            }

            Vector2[] uv = sourceMesh.uv;
            if (uv.Length == sourceMesh.vertexCount)
            {
                destinationMesh.uv = uv;
            }

            Color[] colors = sourceMesh.colors;
            if (colors.Length == sourceMesh.vertexCount)
            {
                destinationMesh.colors = colors;
            }

            destinationMesh.RecalculateBounds();
        }
    }
}
