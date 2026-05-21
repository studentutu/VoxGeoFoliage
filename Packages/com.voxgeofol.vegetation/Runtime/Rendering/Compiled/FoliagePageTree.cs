#nullable enable

using System;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Compiler-emitted tree record with precomputed bounds and transforms.
    /// </summary>
    [Serializable]
    public struct FoliagePageTree
    {
        [SerializeField] private int sourceTreeIndex;
        [SerializeField] private int blueprintIndex;
        [SerializeField] private int cellIndex;
        [SerializeField] private string stableTreeId;
        [SerializeField] private string debugName;
        [SerializeField] private Matrix4x4 localToWorld;
        [SerializeField] private Matrix4x4 worldToObject;
        [SerializeField] private Bounds worldBounds;
        [SerializeField] private Vector3 sphereCenter;
        [SerializeField] private float sphereRadius;

        public FoliagePageTree(
            int sourceTreeIndex,
            int blueprintIndex,
            int cellIndex,
            string stableTreeId,
            string debugName,
            Matrix4x4 localToWorld,
            Matrix4x4 worldToObject,
            Bounds worldBounds,
            Vector3 sphereCenter,
            float sphereRadius)
        {
            this.sourceTreeIndex = sourceTreeIndex;
            this.blueprintIndex = blueprintIndex;
            this.cellIndex = cellIndex;
            this.stableTreeId = stableTreeId ?? string.Empty;
            this.debugName = debugName ?? string.Empty;
            this.localToWorld = localToWorld;
            this.worldToObject = worldToObject;
            this.worldBounds = worldBounds;
            this.sphereCenter = sphereCenter;
            this.sphereRadius = Mathf.Max(0f, sphereRadius);
        }

        public int SourceTreeIndex => sourceTreeIndex;

        public int BlueprintIndex => blueprintIndex;

        public int CellIndex => cellIndex;

        public string StableTreeId => stableTreeId;

        public string DebugName => debugName;

        public Matrix4x4 LocalToWorld => localToWorld;

        public Matrix4x4 WorldToObject => worldToObject;

        public Bounds WorldBounds => worldBounds;

        public Vector3 SphereCenter => sphereCenter;

        public float SphereRadius => sphereRadius;
    }
}
