#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using MeshVoxelizerProject;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VoxGeoFol.Features.Vegetation.Authoring;
using VoxGeoFol.Features.Vegetation.Rendering;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Custom inspector for vegetation tree authoring scene bindings.
    /// </summary>
    [CustomEditor(typeof(VegetationTreeAuthoring))]
    public sealed class VegetationTreeAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (MeshVoxelizerHierarchyBuilder.Generating)
                return;

            VegetationTreeAuthoring authoring = (VegetationTreeAuthoring)target;
            VegetationTreeAuthoringEditorPanel.Draw(serializedObject, authoring);
        }
    }

    /// <summary>
    /// Custom inspector for runtime vegetation container bindings.
    /// </summary>
    [CustomEditor(typeof(VegetationRuntimeContainer))]
    public sealed class VegetationRuntimeContainerEditor : UnityEditor.Editor
    {
        private const string OutputRoot = "Assets/VoxGeoFol.Generated/Vegetation/CompiledPages";

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();

            GUILayout.Space(6f);
            bool fillRegisteredAuthorings = GUILayout.Button("Fill Registered Authorings");
            bool compilePageAssets = GUILayout.Button("Fill Registered Authorings + Compile Pages");
            serializedObject.ApplyModifiedProperties();

            VegetationRuntimeContainer container = (VegetationRuntimeContainer)target;
            if (fillRegisteredAuthorings)
            {
                VegetationTreeAuthoringEditorUtility.FillRuntimeContainerAuthorings(container);
                GUIUtility.ExitGUI();
            }

            if (compilePageAssets)
            {
                VegetationTreeAuthoringEditorUtility.FillRuntimeContainerAuthorings(container);
                CompileContainerToDefaultAssets(container);
                GUIUtility.ExitGUI();
            }
        }

        [MenuItem("CONTEXT/VegetationRuntimeContainer/Fill Registered Authorings")]
        private static void FillRegisteredAuthorings(MenuCommand command)
        {
            VegetationTreeAuthoringEditorUtility.FillRuntimeContainerAuthorings((VegetationRuntimeContainer)command.context);
        }

        /// <summary>
        /// [INTEGRATION] Batchmode entry point for regenerating compiled page assets for loaded scene containers.
        /// </summary>
        public static void CompileOpenSceneContainersToDefaultAssetsFromCommandLine()
        {
            // Range: loaded scene containers, optionally after opening VOXGEOFOL_COMPILE_SCENE. Condition: fails the batch run when no container can be compiled. Output: generated page assets and assigned container references.
            string scenePath = Environment.GetEnvironmentVariable("VOXGEOFOL_COMPILE_SCENE");
            bool openedScene = !string.IsNullOrWhiteSpace(scenePath);
            if (openedScene)
            {
                EditorSceneManager.OpenScene(scenePath);
            }

            int compiledCount = 0;
            List<string> failures = new List<string>();
            VegetationRuntimeContainer[] containers = UnityEngine.Object.FindObjectsByType<VegetationRuntimeContainer>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < containers.Length; i++)
            {
                VegetationRuntimeContainer container = containers[i];
                Scene scene = container.gameObject.scene;
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                VegetationTreeAuthoringEditorUtility.FillRuntimeContainerAuthorings(container);
                if (CompileContainerToDefaultAssets(container))
                {
                    compiledCount++;
                }
                else
                {
                    failures.Add(container.name);
                }
            }

            if (openedScene)
            {
                EditorSceneManager.SaveOpenScenes();
            }

            AssetDatabase.SaveAssets();
            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Failed to compile vegetation containers: {string.Join(", ", failures)}");
            }

            if (compiledCount == 0)
            {
                throw new InvalidOperationException("No loaded scene VegetationRuntimeContainer was compiled.");
            }
        }

        /// <summary>
        /// [INTEGRATION] Compiles one classic-scene container into generated page assets and assigns them back to the container.
        /// </summary>
        private static bool CompileContainerToDefaultAssets(VegetationRuntimeContainer container)
        {
            // Range: one inspector-owned compiled provider. Condition: writes generated assets under the default project folder. Output: true when the container now references a generated assembly and page list.
            FoliageCompilerSettings defaults = FoliageCompilerSettings.Default;
            FoliageCompilerSettings settings = new FoliageCompilerSettings(
                container.CellSize,
                defaults.MaxTreesPerPage,
                defaults.MaxTreesPerCell,
                defaults.MaxPacketsPerPage,
                defaults.MaxAssetGroups,
                defaults.MaxNearDetailBytesPerPage);
            string outputFolder = $"{OutputRoot}/{container.name}";
            FoliageCompilationResult result = FoliageCompiledAssetCompiler.CompileContainerToAssets(
                container,
                outputFolder,
                settings);
            if (!result.Succeeded)
            {
                Debug.LogError(
                    $"Foliage compile failed for '{container.name}': {string.Join("; ", result.FailureMessages)}");
                return false;
            }

            AssignCompiledAssets(container, outputFolder);
            Debug.Log(
                $"Foliage compiled '{container.name}': pages={result.BuildReport.PageCount}, " +
                $"trees={result.BuildReport.TreeCount}, packets={result.BuildReport.PacketCount}, " +
                $"assetGroups={result.BuildReport.AssetGroupCount}.");
            return true;
        }

        private static void AssignCompiledAssets(VegetationRuntimeContainer container, string outputFolder)
        {
            string safeContainerName = MakeSafeAssetName(container.name);
            string assemblyPath = $"{outputFolder}/{safeContainerName}_FoliageAssembly.asset";
            FoliageAssemblyAsset assembly = AssetDatabase.LoadAssetAtPath<FoliageAssemblyAsset>(assemblyPath);
            string[] pageGuids = AssetDatabase.FindAssets("t:FoliagePageAsset", new[] { outputFolder });
            List<FoliagePageAsset> pages = new List<FoliagePageAsset>(pageGuids.Length);
            Array.Sort(pageGuids, StringComparer.Ordinal);
            for (int i = 0; i < pageGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(pageGuids[i]);
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (!fileName.StartsWith($"{safeContainerName}_Page_", StringComparison.Ordinal))
                {
                    continue;
                }

                FoliagePageAsset page = AssetDatabase.LoadAssetAtPath<FoliagePageAsset>(path);
                if (page != null)
                {
                    pages.Add(page);
                }
            }

            SerializedObject serializedContainer = new SerializedObject(container);
            serializedContainer.FindProperty("compiledAssembly").objectReferenceValue = assembly;
            SerializedProperty pagesProperty = serializedContainer.FindProperty("compiledPages");
            pagesProperty.arraySize = pages.Count;
            for (int i = 0; i < pages.Count; i++)
            {
                pagesProperty.GetArrayElementAtIndex(i).objectReferenceValue = pages[i];
            }

            serializedContainer.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(container);
        }

        private static string MakeSafeAssetName(string name)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            char[] buffer = name.ToCharArray();
            for (int i = 0; i < buffer.Length; i++)
            {
                for (int invalidIndex = 0; invalidIndex < invalidChars.Length; invalidIndex++)
                {
                    if (buffer[i] == invalidChars[invalidIndex])
                    {
                        buffer[i] = '_';
                        break;
                    }
                }
            }

            string safeName = new string(buffer).Trim();
            return safeName.Length > 0 ? safeName : "VegetationContainer";
        }
    }
}
