#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] Classic-scene compiled-page provider for the global vegetation render world.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class VegetationRuntimeContainer : MonoBehaviour
    {
        [Tooltip("World-space origin used by the editor compiler when assigning generated foliage pages to cells.")]
        [SerializeField] private Vector3 gridOrigin = Vector3.zero;

        [Tooltip("World-space cell size used by the editor compiler when assigning generated foliage pages to cells.")]
        [SerializeField] private Vector3 cellSize = new Vector3(32f, 32f, 32f);

        [Tooltip("Explicit authoring list owned by this container. Rebuild it with Fill Registered Authorings after hierarchy ownership changes.")]
        [SerializeField]
        private List<VegetationTreeAuthoring> registeredAuthorings = new List<VegetationTreeAuthoring>();

        [Tooltip("Compiled foliage assembly generated from this container. Runtime rendering consumes this asset, not live tree authorings.")]
        [SerializeField] private FoliageAssemblyAsset? compiledAssembly;

        [Tooltip("Compiled foliage pages generated from this container. Runtime rendering consumes these pages, not live tree authorings.")]
        [SerializeField] private List<FoliagePageAsset> compiledPages = new List<FoliagePageAsset>();

        public Vector3 GridOrigin => gridOrigin;

        public Vector3 CellSize => cellSize;

        public IReadOnlyList<VegetationTreeAuthoring> RegisteredAuthorings => registeredAuthorings;

        public FoliageAssemblyAsset? CompiledAssembly => compiledAssembly;

        public IReadOnlyList<FoliagePageAsset> CompiledPages => compiledPages;

        public Hash128 ContainerIdHash => VegetationRuntimeIdentityUtility.BuildContainerIdHash(gameObject);

        public string ContainerId => ContainerIdHash.ToString();

        /// <summary>
        /// [INTEGRATION] Converts serialized authorings into compiler-safe tree records. Runtime rendering does not consume these records.
        /// </summary>
        public void BuildRuntimeTreeAuthorings(List<VegetationTreeAuthoringRuntime> target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            target.Clear();
            Hash128 containerIdHash = ContainerIdHash;
            for (int i = 0; i < registeredAuthorings.Count; i++)
            {
                VegetationTreeAuthoring? authoring = registeredAuthorings[i];
                if (authoring == null)
                {
                    throw new InvalidOperationException(
                        $"VegetationRuntimeContainer '{name}' contains a null authoring entry at index {i}. Refill the serialized authorings list.");
                }

                if (!IsOwnedByContainer(authoring.transform))
                {
                    throw new InvalidOperationException(
                        $"VegetationRuntimeContainer '{name}' references authoring '{authoring.name}' outside its own hierarchy.");
                }

                for (int existingIndex = 0; existingIndex < i; existingIndex++)
                {
                    if (ReferenceEquals(registeredAuthorings[existingIndex], authoring))
                    {
                        throw new InvalidOperationException(
                            $"VegetationRuntimeContainer '{name}' contains duplicate authoring '{authoring.name}' in the serialized list.");
                    }
                }

                TreeBlueprintSO blueprint = authoring.Blueprint ??
                                            throw new InvalidOperationException(
                                                $"{authoring.name} is missing blueprint and cannot enter compiled foliage output.");

                target.Add(new VegetationTreeAuthoringRuntime(
                    VegetationRuntimeIdentityUtility.BuildTreeIdHash(containerIdHash, i),
                    authoring.name,
                    blueprint,
                    authoring.transform.localToWorldMatrix,
                    authoring.gameObject.activeInHierarchy,
                    authoring));
            }
        }

        /// <summary>
        /// [INTEGRATION] Registers this container's compiled page assets with the global render world.
        /// </summary>
        public void RefreshRuntimeRegistration()
        {
            if (compiledAssembly == null || compiledPages.Count == 0)
            {
                VegetationRenderWorld.Shared.UnregisterProvider(ContainerId);
                Debug.LogWarning(
                    $"VegetationRuntimeContainer '{name}' has no compiled foliage pages. Run the compiled page command before Play Mode.");
                return;
            }

            VegetationRenderWorld.Shared.RegisterProvider(ContainerId, name, compiledAssembly, compiledPages);
        }

        /// <summary>
        /// [INTEGRATION] Clears this container's compiled page provider registration.
        /// </summary>
        public void ResetRuntimeState()
        {
            VegetationRenderWorld.Shared.UnregisterProvider(ContainerId);
        }

        private void OnEnable()
        {
            RefreshRuntimeRegistration();
        }

        private void OnDisable()
        {
            ResetRuntimeState();
        }

        private bool IsOwnedByContainer(Transform authoringTransform)
        {
            return authoringTransform == transform || authoringTransform.IsChildOf(transform);
        }
    }
}
