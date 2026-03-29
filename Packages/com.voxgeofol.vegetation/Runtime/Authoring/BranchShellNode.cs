#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Authoring
{
    /// <summary>
    /// Immutable authoring record for one hierarchical canopy shell node.
    /// </summary>
    [System.Serializable]
    public sealed class BranchShellNode
    {
        [SerializeField] private Bounds localBounds = new Bounds(Vector3.zero, Vector3.one);
        [SerializeField] private int depth;
        [SerializeField] private int firstChildIndex = -1;
        [SerializeField] private byte childMask;
        [SerializeField] private Mesh? shellL0Mesh;
        [SerializeField] private Mesh? shellL1Mesh;
        [SerializeField] private Mesh? shellL2Mesh;

        public BranchShellNode()
        {
        }

        public BranchShellNode(
            Bounds localBounds,
            int depth,
            int firstChildIndex,
            byte childMask,
            Mesh? shellL0Mesh,
            Mesh? shellL1Mesh,
            Mesh? shellL2Mesh)
        {
            this.localBounds = localBounds;
            this.depth = depth;
            this.firstChildIndex = firstChildIndex;
            this.childMask = childMask;
            this.shellL0Mesh = shellL0Mesh;
            this.shellL1Mesh = shellL1Mesh;
            this.shellL2Mesh = shellL2Mesh;
        }

        public Bounds LocalBounds => localBounds;

        public int Depth => depth;

        public int FirstChildIndex => firstChildIndex;

        public byte ChildMask => childMask;

        public Mesh? ShellL0Mesh => shellL0Mesh;

        public Mesh? ShellL1Mesh => shellL1Mesh;

        public Mesh? ShellL2Mesh => shellL2Mesh;
    }
}
