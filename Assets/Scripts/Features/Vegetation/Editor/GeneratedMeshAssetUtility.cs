#nullable enable

using System;
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
        private const string RuntimeMeshFolderPath = "Assets/Scripts/Features/Vegetation/Runtime/Meshes";

        /// <summary>
        /// [INTEGRATION] Reuses or creates a generated mesh asset under the vegetation runtime mesh folder.
        /// </summary>
        public static Mesh PersistGeneratedMesh(UnityEngine.Object ownerAsset, string meshName, Mesh generatedMesh)
        {
            // Range: owner asset can be transient or saved. Condition: generated mesh already contains final topology. Output: explicit .mesh asset under Runtime/Meshes when the owner asset is saved, otherwise the transient generated mesh instance.
            if (ownerAsset == null)
            {
                throw new ArgumentNullException(nameof(ownerAsset));
            }

            if (generatedMesh == null)
            {
                throw new ArgumentNullException(nameof(generatedMesh));
            }

            generatedMesh.name = meshName;
            string assetPath = AssetDatabase.GetAssetPath(ownerAsset);
            if (string.IsNullOrEmpty(assetPath))
            {
                return generatedMesh;
            }

            EnsureFolderPath(RuntimeMeshFolderPath);
            string meshAssetPath = BuildGeneratedMeshAssetPath(assetPath, meshName);
            Mesh? existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);
            if (existingMesh == null)
            {
                AssetDatabase.CreateAsset(generatedMesh, meshAssetPath);
                EditorUtility.SetDirty(generatedMesh);
                EditorUtility.SetDirty(ownerAsset);
                return generatedMesh;
            }

            CopyMeshData(generatedMesh, existingMesh);
            UnityEngine.Object.DestroyImmediate(generatedMesh);
            EditorUtility.SetDirty(existingMesh);
            EditorUtility.SetDirty(ownerAsset);
            return existingMesh;
        }

        private static string BuildGeneratedMeshAssetPath(string ownerAssetPath, string meshName)
        {
            string ownerGuid = AssetDatabase.AssetPathToGUID(ownerAssetPath);
            string guidSuffix = string.IsNullOrEmpty(ownerGuid)
                ? "noguid"
                : ownerGuid.Substring(0, Mathf.Min(8, ownerGuid.Length));

            string sanitizedName = SanitizeFileName($"{meshName}_{guidSuffix}");
            return $"{RuntimeMeshFolderPath}/{sanitizedName}.mesh";
        }

        private static void EnsureFolderPath(string folderPath)
        {
            string[] segments = folderPath.Split('/');
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
