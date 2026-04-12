#nullable enable

using System;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Runtime-safe tree authoring snapshot shared by classic-scene and SubScene providers.
    /// </summary>
    public sealed class VegetationTreeAuthoringRuntime
    {
        private readonly VegetationTreeAuthoring? sourceAuthoring;
        private int runtimeTreeIndex = -1;

        public VegetationTreeAuthoringRuntime(
            Hash128 stableTreeIdHash,
            string debugName,
            TreeBlueprintSO blueprint,
            Matrix4x4 localToWorld,
            bool isActive,
            VegetationTreeAuthoring? sourceAuthoring = null)
        {
            if (!stableTreeIdHash.isValid)
            {
                throw new ArgumentException("Stable tree id hash must be valid.", nameof(stableTreeIdHash));
            }

            StableTreeIdHash = stableTreeIdHash;
            DebugName = debugName;
            Blueprint = blueprint ?? throw new ArgumentNullException(nameof(blueprint));
            LocalToWorld = localToWorld;
            IsActive = isActive;
            this.sourceAuthoring = sourceAuthoring;
        }

        public Hash128 StableTreeIdHash { get; }

        public string StableTreeId => StableTreeIdHash.ToString();

        public string DebugName { get; }

        public TreeBlueprintSO Blueprint { get; }

        public Matrix4x4 LocalToWorld { get; }

        public bool IsActive { get; }

        public int RuntimeTreeIndex => runtimeTreeIndex;

        /// <summary>
        /// [INTEGRATION] Assigns the runtime tree index produced by runtime registration and mirrors it to classic authoring when present.
        /// </summary>
        public void RefreshRuntimeTreeIndex(int treeIndex)
        {
            runtimeTreeIndex = treeIndex;
            sourceAuthoring?.RefreshRuntimeTreeIndex(treeIndex);
        }

        /// <summary>
        /// [INTEGRATION] Clears runtime-only indexing and mirrors the reset to classic authoring when present.
        /// </summary>
        public void ResetRuntimeTreeIndex()
        {
            runtimeTreeIndex = -1;
            sourceAuthoring?.ResetRuntimeTreeIndex();
        }
    }
}
