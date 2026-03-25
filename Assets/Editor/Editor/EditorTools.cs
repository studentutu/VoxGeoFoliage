using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Pipeline.Utilities;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Scripts.Editor
{
    public class EditorTools
    {
        [MenuItem("Tools/Editor/Clear Player Prefs", priority = 1, secondaryPriority = 0)]
        public static void ClearPlayerPrefs()
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
        }

        [MenuItem("Tools/Editor/Clear Editor Cache", priority = 1, secondaryPriority = 1)]
        public static void ClearEditorCache()
        {
            BuildCache.PurgeCache(prompt: false);
            UnityEngine.Caching.ClearCache();
            UnityEditor.Lightmapping.ClearDiskCache();

            // only for the active scene.
            UnityEditor.StaticOcclusionCulling.RemoveCacheFolder();

            EditorUtility.UnloadUnusedAssetsImmediate();
            GC.Collect();

            SaveAndRefresh();
        }

        [MenuItem("Tools/Editor/Clear Addressables", priority = 1, secondaryPriority = 2)]
        public static void CleanAddressables()
        {
            AddressableAssetSettings.CleanPlayerContent();
            EditorFolders.ClearAddressableAssetFolder();
        }
        
        /// <summary>
        ///  In order for sub-scenes to use the same Occlusion/LightProbes/Light settings
        ///  we need 1 main and all of them to be baked at the same tim.
        /// </summary>
        // TODO: Separate baking for NavMesh as it can be baked per Sub-scene/new AI package (do not use old).
        [MenuItem("Tools/Baking/Bake All data for Open Scenes As Cluster", priority = 3)]
        public static void BakeAllForOpenScenes()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("Cannot bake while in Play Mode.");
                return;
            }

            if (Lightmapping.isRunning)
            {
                Debug.LogError("A lighting bake is already running.");
                return;
            }

            if (StaticOcclusionCulling.isRunning)
            {
                Debug.LogError("An occlusion culling bake is already running.");
                return;
            }

            var scenePaths = new List<string>();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && !string.IsNullOrEmpty(scene.path))
                    scenePaths.Add(scene.path);
            }

            if (scenePaths.Count == 0)
            {
                Debug.LogWarning("No loaded scenes to bake.");
                return;
            }

            // Save scene object changes before the bake starts.
            EditorSceneManager.SaveOpenScenes();
            SaveAndRefresh();

            Lightmapping.BakeMultipleScenes(scenePaths.ToArray());
            StaticOcclusionCulling.Compute();

            EditorSceneManager.SaveOpenScenes();
            SaveAndRefresh();

            Debug.Log($"Baking {scenePaths.Count} open scenes as one lighting cluster.");
        }

        public static void SaveAndRefresh()
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.RefreshSettings();
            AssetDatabase.Refresh();
        }
    }
}