#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] Classic-scene lifecycle provider that converts serialized vegetation authorings into one runtime owner.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class VegetationRuntimeContainer : MonoBehaviour
    {
        // shared instance payload is about 144 bytes per visible instance
        // so 262144 is roughly 36 MB just for the packed instance buffer
        // Don’t raise it blindly.
        // this is a chunk of data for a single container!
        private const int DefaultMaxVisibleInstanceCapacity = 262144;

        [Tooltip("World-space origin of the frozen spatial grid built during runtime registration.")]
        [SerializeField] private Vector3 gridOrigin = Vector3.zero;

        [Tooltip("World-space cell size of the frozen spatial grid. Changes require RefreshRuntimeRegistration().")]
        [SerializeField] private Vector3 cellSize = new Vector3(32f, 32f, 32f);

        [Tooltip("Explicit authoring list owned by this container. Rebuild it with Fill Registered Authorings after hierarchy ownership changes.")]
        [SerializeField]
        private List<VegetationTreeAuthoring> registeredAuthorings = new List<VegetationTreeAuthoring>();

        [Min(1)]
        [Tooltip("Hard cap for GPU-visible vegetation instances packed into the shared runtime buffer each frame. Overflow is clamped instead of reallocating scene-scale buffers. Shared instance payload is approximately 144 bytes per visible instance.")]
        [SerializeField]
        private int maxVisibleInstanceCapacity = DefaultMaxVisibleInstanceCapacity;

        private readonly List<VegetationTreeAuthoringRuntime> runtimeAuthorings = new List<VegetationTreeAuthoringRuntime>();
        private AuthoringContainerRuntime? runtimeOwner;

        public Vector3 GridOrigin => gridOrigin;

        public Vector3 CellSize => cellSize;

        public IReadOnlyList<VegetationTreeAuthoring> RegisteredAuthorings => registeredAuthorings;

        public int MaxVisibleInstanceCapacity => Mathf.Max(1, maxVisibleInstanceCapacity);

        public int RenderLayer => gameObject.layer;

        public Hash128 ContainerIdHash => VegetationRuntimeIdentityUtility.BuildContainerIdHash(gameObject);

        public string ContainerId => ContainerIdHash.ToString();

        public VegetationRuntimeRegistry? Registry => runtimeOwner?.Registry;

        public VegetationIndirectRenderer? IndirectRenderer => runtimeOwner?.IndirectRenderer;

        public bool HasPreparedFrame => runtimeOwner != null && runtimeOwner.HasPreparedFrame;

        public AuthoringContainerRuntime? RuntimeOwner => runtimeOwner;

        /// <summary>
        /// [INTEGRATION] Converts serialized authorings into runtime-safe tree records shared by classic-scene and SubScene providers.
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
                                                $"{authoring.name} is missing blueprint and cannot enter runtime registration.");

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
        /// [INTEGRATION] Rebuilds the classic-scene runtime owner from the configured serialized authorings list.
        /// </summary>
        public void RefreshRuntimeRegistration()
        {
            runtimeAuthorings.Clear();
            BuildRuntimeTreeAuthorings(runtimeAuthorings);
            ReplaceRuntimeOwner(runtimeAuthorings.ToArray());
        }

        /// <summary>
        /// [INTEGRATION] Clears runtime-only registration state and releases the classic-scene runtime owner.
        /// </summary>
        public void ResetRuntimeState()
        {
            DisposeRuntimeOwner();
        }

        /// <summary>
        /// [INTEGRATION] Forwards classic-scene camera preparation to the shared runtime owner.
        /// </summary>
        public bool PrepareFrameForCamera(Camera camera, ComputeShader? classifyShader, bool diagnosticsEnabled)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            EnsureRuntimeOwner();
            return runtimeOwner != null && runtimeOwner.PrepareFrameForCamera(camera, classifyShader, diagnosticsEnabled);
        }

        private void OnEnable()
        {
            RefreshRuntimeRegistration();
        }

        private void OnDisable()
        {
            DisposeRuntimeOwner();
        }

        private void EnsureRuntimeOwner()
        {
            if (runtimeOwner != null)
            {
                return;
            }

            RefreshRuntimeRegistration();
        }

        private bool IsOwnedByContainer(Transform authoringTransform)
        {
            return authoringTransform == transform || authoringTransform.IsChildOf(transform);
        }

        private void ReplaceRuntimeOwner(VegetationTreeAuthoringRuntime[] authoringSnapshot)
        {
            DisposeRuntimeOwner();
            AuthoringContainerRuntime newRuntimeOwner = new AuthoringContainerRuntime(
                ContainerId,
                VegetationRuntimeProviderKind.ClassicScene,
                name,
                this,
                RenderLayer,
                gridOrigin,
                cellSize,
                MaxVisibleInstanceCapacity,
                authoringSnapshot);
            runtimeOwner = newRuntimeOwner;
            newRuntimeOwner.Activate();
            newRuntimeOwner.RefreshRuntimeRegistration();
        }

        private void DisposeRuntimeOwner()
        {
            runtimeOwner?.Dispose();
            runtimeOwner = null;
        }
    }
}
