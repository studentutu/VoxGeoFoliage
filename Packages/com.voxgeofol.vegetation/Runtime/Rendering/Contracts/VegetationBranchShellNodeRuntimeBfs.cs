#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Branch-local shell-node hierarchy metadata authored in BFS order.
    /// Current shipped limitation: the GPU path uploads child-link data but does not yet use it for real frontier traversal or subtree skip.
    /// </summary>
    public struct VegetationBranchShellNodeRuntimeBfs
    {
        public Vector3 LocalCenter;
        public Vector3 LocalExtents;
        public int FirstChildIndex;
        public uint ChildMask;
        public int ShellDrawSlot;
    }
}
