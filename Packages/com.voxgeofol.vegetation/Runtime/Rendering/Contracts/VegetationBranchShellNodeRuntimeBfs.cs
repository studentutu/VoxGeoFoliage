#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// MVP BFS shell-node payload used by the GPU decision path.
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
