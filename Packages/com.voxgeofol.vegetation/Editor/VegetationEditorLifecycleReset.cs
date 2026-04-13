#nullable enable

using System;
using UnityEditor;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Rendering;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// [INTEGRATION] Editor-only lifecycle teardown for package-owned vegetation runtime state.
    /// </summary>
    [InitializeOnLoad]
    public static class VegetationEditorLifecycleReset
    {
        static VegetationEditorLifecycleReset()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting -= OnEditorQuitting;
            EditorApplication.quitting += OnEditorQuitting;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingPlayMode)
            {
                return;
            }

            ResetVegetationRuntimeState();
        }

        private static void OnBeforeAssemblyReload()
        {
            ResetVegetationRuntimeState();
        }

        private static void OnEditorQuitting()
        {
            ResetVegetationRuntimeState();
        }

        /// <summary>
        /// [INTEGRATION] Releases scene-owned runtime owners and clears static runtime registries before editor lifecycle transitions.
        /// </summary>
        private static void ResetVegetationRuntimeState()
        {
            try
            {
                VegetationRuntimeContainer[] containers = Resources.FindObjectsOfTypeAll<VegetationRuntimeContainer>();
                for (int i = 0; i < containers.Length; i++)
                {
                    VegetationRuntimeContainer container = containers[i];
                    if (container == null || EditorUtility.IsPersistent(container))
                    {
                        continue;
                    }

                    container.ResetRuntimeState();
                }

                VegetationActiveAuthoringContainerRuntimes.Reset();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
