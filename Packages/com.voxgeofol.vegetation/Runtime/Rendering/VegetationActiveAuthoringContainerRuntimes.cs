#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Shared discovery registry for active runtime vegetation owners.
    /// </summary>
    public static class VegetationActiveAuthoringContainerRuntimes
    {
        private static readonly List<AuthoringContainerRuntime> ActiveRuntimesInternal =
            new List<AuthoringContainerRuntime>();

        private static readonly Dictionary<string, AuthoringContainerRuntime> ActiveRuntimesByContainerId =
            new Dictionary<string, AuthoringContainerRuntime>(StringComparer.Ordinal);

        /// <summary>
        /// [INTEGRATION] Registers one runtime owner and enforces one active owner per deterministic container id.
        /// </summary>
        public static bool Register(AuthoringContainerRuntime runtime)
        {
            if (runtime == null)
            {
                return false;
            }

            if (ActiveRuntimesByContainerId.TryGetValue(runtime.ContainerId, out AuthoringContainerRuntime? existingRuntime))
            {
                if (ReferenceEquals(existingRuntime, runtime))
                {
                    return true;
                }

                int precedenceComparison = CompareProviderPrecedence(runtime.ProviderKind, existingRuntime.ProviderKind);
                if (precedenceComparison <= 0)
                {
                    Debug.LogWarning(
                        $"Vegetation runtime owner skipped containerId={runtime.ContainerId} incoming={runtime.ProviderKind} active={existingRuntime.ProviderKind} debugName={runtime.DebugName}");
                    return false;
                }

                ReplaceActiveRuntime(runtime, existingRuntime);
                return true;
            }

            ActiveRuntimesByContainerId.Add(runtime.ContainerId, runtime);
            ActiveRuntimesInternal.Add(runtime);
            return true;
        }

        /// <summary>
        /// [INTEGRATION] Unregisters one runtime owner when its provider disables or unloads.
        /// </summary>
        public static void Unregister(AuthoringContainerRuntime runtime)
        {
            if (runtime == null)
            {
                return;
            }

            if (!ActiveRuntimesByContainerId.TryGetValue(runtime.ContainerId, out AuthoringContainerRuntime? existingRuntime) ||
                !ReferenceEquals(existingRuntime, runtime))
            {
                return;
            }

            ActiveRuntimesByContainerId.Remove(runtime.ContainerId);
            ActiveRuntimesInternal.Remove(runtime);
        }

        /// <summary>
        /// [INTEGRATION] Copies all active runtime owners into the provided target list for renderer discovery.
        /// </summary>
        public static void GetActive(List<AuthoringContainerRuntime> target)
        {
            if (target == null)
            {
                return;
            }

            target.Clear();
            for (int i = 0; i < ActiveRuntimesInternal.Count; i++)
            {
                AuthoringContainerRuntime runtime = ActiveRuntimesInternal[i];
                if (runtime != null)
                {
                    target.Add(runtime);
                }
            }
        }

        private static void ReplaceActiveRuntime(AuthoringContainerRuntime incomingRuntime, AuthoringContainerRuntime existingRuntime)
        {
            int activeIndex = ActiveRuntimesInternal.IndexOf(existingRuntime);
            if (activeIndex >= 0)
            {
                ActiveRuntimesInternal[activeIndex] = incomingRuntime;
            }
            else
            {
                ActiveRuntimesInternal.Add(incomingRuntime);
            }

            ActiveRuntimesByContainerId[incomingRuntime.ContainerId] = incomingRuntime;
            existingRuntime.HandleRegistrySuperseded();
        }

        private static int CompareProviderPrecedence(
            VegetationRuntimeProviderKind incomingProviderKind,
            VegetationRuntimeProviderKind existingProviderKind)
        {
            return incomingProviderKind.CompareTo(existingProviderKind);
        }
    }
}
