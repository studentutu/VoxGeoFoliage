#nullable enable

using System;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Authoring-time branch transform and bounds data precomputed for page compilation.
    /// </summary>
    [Serializable]
    public struct FoliageBranchPlacementTemplate
    {
        [SerializeField] private int blueprintIndex;
        [SerializeField] private int prototypeIndex;
        [SerializeField] private Matrix4x4 localToTree;
        [SerializeField] private Matrix4x4 treeToLocal;
        [SerializeField] private Bounds localBounds;
        [SerializeField] private float localSphereRadius;
        [SerializeField] private int packedLeafTint;

        public FoliageBranchPlacementTemplate(
            int blueprintIndex,
            int prototypeIndex,
            Matrix4x4 localToTree,
            Matrix4x4 treeToLocal,
            Bounds localBounds,
            float localSphereRadius,
            uint packedLeafTint)
        {
            this.blueprintIndex = blueprintIndex;
            this.prototypeIndex = prototypeIndex;
            this.localToTree = localToTree;
            this.treeToLocal = treeToLocal;
            this.localBounds = localBounds;
            this.localSphereRadius = Mathf.Max(0f, localSphereRadius);
            this.packedLeafTint = unchecked((int)packedLeafTint);
        }

        public int BlueprintIndex => blueprintIndex;

        public int PrototypeIndex => prototypeIndex;

        public Matrix4x4 LocalToTree => localToTree;

        public Matrix4x4 TreeToLocal => treeToLocal;

        public Bounds LocalBounds => localBounds;

        public float LocalSphereRadius => localSphereRadius;

        public uint PackedLeafTint => unchecked((uint)packedLeafTint);
    }
}
