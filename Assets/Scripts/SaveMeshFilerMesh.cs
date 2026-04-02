using System;
using System.IO;
using JetBrains.Annotations;
using UnityEngine;
using Random = UnityEngine.Random;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DefaultNamespace
{
    /// <summary>
    ///     Test save utility 
    /// </summary>
    public class SaveMeshFilerMesh : MonoBehaviour
    {
        [SerializeField] private MeshFilter _meshFilter;
        [SerializeField] private string fullrelativeFolder = "Assets/GenerateMeshHere";

        public bool Resave = false;

        private void OnValidate()
        {
            if (Resave)
            {
                Resave = false;
                SaveMeshToFolder();
            }
        }

        private void SaveMeshToFolder()
        {
            var mesh = _meshFilter.sharedMesh;
#if UNITY_EDITOR
            SaveMeshAsCopyTo(mesh.name, mesh, fullrelativeFolder);

#endif
        }

        /// <summary>
        /// [INTEGRATION] Simple save mesh utility.
        /// </summary>
        public static Mesh SaveMeshAsCopyTo(string meshName, Mesh generatedMesh, string relativeFolderToSave)
        {
#if UNITY_EDITOR
            if (generatedMesh == null)
            {
                throw new ArgumentNullException(nameof(generatedMesh));
            }

            generatedMesh.name = meshName;

            string meshFolderPath = ResolveExplicitFolderPath(relativeFolderToSave);
            EnsureFolderPath(meshFolderPath);
            string sanitizedName = SanitizeFileName($"{meshName}");
            var random = Random.Range(0, 2024);
            string meshAssetPath = $"{meshFolderPath}/{sanitizedName}_{random}.mesh";
            Mesh? existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);
            if (existingMesh == null)
            {
                AssetDatabase.CreateAsset(generatedMesh, meshAssetPath);
                EditorUtility.SetDirty(generatedMesh);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                return generatedMesh;
            }
#endif
            throw new InvalidOperationException($"Mesh {meshAssetPath} already exists! Remove it and try again.");
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

        private static void EnsureFolderPath(string folderPath)
        {
#if UNITY_EDITOR  
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
#endif
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            return assetPath.Replace('\\', '/').TrimEnd('/');
        }
    }
}