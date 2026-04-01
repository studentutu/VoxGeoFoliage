#nullable enable

using UnityEngine;

namespace MeshVoxelizerProject
{
    /// <summary>
    /// One node in the compatibility canopy hierarchy API.
    /// </summary>
    [System.Serializable]
    public sealed class MeshVoxelizerHierarchyNode
    {
        public MeshVoxelizerHierarchyNode(
            Bounds localBounds,
            int depth,
            int parentIndex,
            int firstChildIndex,
            byte childMask,
            Mesh? shellL0Mesh,
            Mesh? shellL1Mesh,
            Mesh? shellL2Mesh)
        {
            LocalBounds = localBounds;
            Depth = depth;
            ParentIndex = parentIndex;
            FirstChildIndex = firstChildIndex;
            ChildMask = childMask;
            ShellL0Mesh = shellL0Mesh;
            ShellL1Mesh = shellL1Mesh;
            ShellL2Mesh = shellL2Mesh;
        }

        public Bounds LocalBounds { get; }

        public int Depth { get; }

        public int ParentIndex { get; }

        public int FirstChildIndex { get; }

        public byte ChildMask { get; }

        public Mesh? ShellL0Mesh { get; }

        public Mesh? ShellL1Mesh { get; }

        public Mesh? ShellL2Mesh { get; }
    }
}
