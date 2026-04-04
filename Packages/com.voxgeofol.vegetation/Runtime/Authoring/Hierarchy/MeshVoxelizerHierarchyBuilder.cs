#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelSystem;

namespace MeshVoxelizerProject
{
    /// <summary>
    /// Builds a shell hierarchy by splitting CPU voxel surface ownership into octants.
    /// </summary>
    public static class MeshVoxelizerHierarchyBuilder
    {
        public static bool Generating = false;

        /// <summary>
        /// [INTEGRATION] Builds a preorder hierarchy where each node owns CPU-voxel meshes for L0/L1/L2.
        /// </summary>
        public static MeshVoxelizerHierarchyNode[] BuildHierarchy(
            Mesh sourceMesh,
            int voxelResolutionL0,
            int voxelResolutionL1,
            int voxelResolutionL2,
            int maxDepth = 2,
            int minimumSurfaceVoxelCountToSplit = 4)
        {
            return BuildHierarchy(
                sourceMesh,
                voxelResolutionL0,
                voxelResolutionL1,
                voxelResolutionL2,
                CpuVoxelSurfaceBuildOptions.Reduced,
                CpuVoxelSurfaceBuildOptions.Reduced,
                CpuVoxelSurfaceBuildOptions.Reduced,
                maxDepth,
                minimumSurfaceVoxelCountToSplit);
        }

        /// <summary>
        /// [INTEGRATION] Builds a preorder hierarchy where each node owns CPU-voxel meshes for L0/L1/L2.
        /// </summary>
        public static MeshVoxelizerHierarchyNode[] BuildHierarchy(
            Mesh sourceMesh,
            int voxelResolutionL0,
            int voxelResolutionL1,
            int voxelResolutionL2,
            CpuVoxelSurfaceBuildOptions l0BuildOptions,
            CpuVoxelSurfaceBuildOptions l1BuildOptions,
            CpuVoxelSurfaceBuildOptions l2BuildOptions,
            int maxDepth = 2,
            int minimumSurfaceVoxelCountToSplit = 4)
        {
            Generating = true;
            try
            {
                // Range: sourceMesh must be readable and the resolutions must be >= 2. Condition: L0 drives node subdivision and L1/L2 reuse the same node bounds. Output: preorder node array with one L0/L1/L2 mesh triplet per node.
                if (sourceMesh == null)
                {
                    throw new ArgumentNullException(nameof(sourceMesh));
                }

                if (!sourceMesh.isReadable)
                {
                    throw new InvalidOperationException($"{sourceMesh.name} must be readable before hierarchy generation.");
                }

                ValidateResolution(voxelResolutionL0, nameof(voxelResolutionL0));
                ValidateResolution(voxelResolutionL1, nameof(voxelResolutionL1));
                ValidateResolution(voxelResolutionL2, nameof(voxelResolutionL2));
                if (maxDepth < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(maxDepth), maxDepth, "maxDepth must be zero or greater.");
                }

                Bounds sourceBounds = sourceMesh.bounds;
                if (sourceBounds.size.x <= Mathf.Epsilon ||
                    sourceBounds.size.y <= Mathf.Epsilon ||
                    sourceBounds.size.z <= Mathf.Epsilon)
                {
                    throw new InvalidOperationException($"{sourceMesh.name} has invalid bounds {sourceBounds}.");
                }

                VoxelLevelData l0 = CreateVoxelLevel(sourceMesh, voxelResolutionL0);
                VoxelLevelData l1 = CreateVoxelLevel(sourceMesh, voxelResolutionL1);
                VoxelLevelData l2 = CreateVoxelLevel(sourceMesh, voxelResolutionL2);

                List<NodeBuildRecord> nodeRecords = new List<NodeBuildRecord>();
                AddNodeRecursive(
                    nodeRecords,
                    sourceMesh.name,
                    l0,
                    l1,
                    l2,
                    l0BuildOptions,
                    l1BuildOptions,
                    l2BuildOptions,
                    sourceBounds,
                    -1,
                    0,
                    maxDepth,
                    Mathf.Max(1, minimumSurfaceVoxelCountToSplit));

                if (nodeRecords.Count == 0)
                {
                    throw new InvalidOperationException($"{sourceMesh.name} produced no surface hierarchy nodes.");
                }

                return NormalizeHierarchy(nodeRecords);
            }
            finally
            {
                Generating = false;
            }
        }

        private static MeshVoxelizerHierarchyNode[] NormalizeHierarchy(List<NodeBuildRecord> nodeRecords)
        {
            List<int>[] childrenByParent = new List<int>[nodeRecords.Count];
            List<int> rootIndices = new List<int>();
            for (int i = 0; i < nodeRecords.Count; i++)
            {
                int parentIndex = nodeRecords[i].ParentIndex;
                if (parentIndex < 0)
                {
                    rootIndices.Add(i);
                    continue;
                }

                if (childrenByParent[parentIndex] == null)
                {
                    childrenByParent[parentIndex] = new List<int>();
                }

                childrenByParent[parentIndex].Add(i);
            }

            List<int> orderedIndices = new List<int>(nodeRecords.Count);
            Queue<int> frontier = new Queue<int>();
            for (int i = 0; i < rootIndices.Count; i++)
            {
                frontier.Enqueue(rootIndices[i]);
            }

            while (frontier.Count > 0)
            {
                int oldIndex = frontier.Dequeue();
                orderedIndices.Add(oldIndex);

                List<int>? children = childrenByParent[oldIndex];
                if (children == null)
                {
                    continue;
                }

                for (int i = 0; i < children.Count; i++)
                {
                    frontier.Enqueue(children[i]);
                }
            }

            int[] oldToNewIndex = new int[nodeRecords.Count];
            for (int i = 0; i < orderedIndices.Count; i++)
            {
                oldToNewIndex[orderedIndices[i]] = i;
            }

            MeshVoxelizerHierarchyNode[] hierarchyNodes = new MeshVoxelizerHierarchyNode[orderedIndices.Count];
            for (int i = 0; i < orderedIndices.Count; i++)
            {
                int oldIndex = orderedIndices[i];
                NodeBuildRecord nodeRecord = nodeRecords[oldIndex];
                List<int>? children = childrenByParent[oldIndex];
                int parentIndex = nodeRecord.ParentIndex < 0 ? -1 : oldToNewIndex[nodeRecord.ParentIndex];
                int firstChildIndex = children == null || children.Count == 0 ? -1 : oldToNewIndex[children[0]];

                hierarchyNodes[i] = new MeshVoxelizerHierarchyNode(
                    nodeRecord.LocalBounds,
                    nodeRecord.Depth,
                    parentIndex,
                    firstChildIndex,
                    nodeRecord.ChildMask,
                    nodeRecord.ShellL0Mesh,
                    nodeRecord.ShellL1Mesh,
                    nodeRecord.ShellL2Mesh);
            }

            return hierarchyNodes;
        }

        private static void ValidateResolution(int resolution, string parameterName)
        {
            if (resolution < 2)
            {
                throw new ArgumentOutOfRangeException(parameterName, resolution, "Resolution must be 2 or greater.");
            }
        }

        private static VoxelLevelData CreateVoxelLevel(Mesh sourceMesh, int resolution)
        {
            return new VoxelLevelData(CPUVoxelizer.VoxelizeToVolume(sourceMesh, resolution), resolution);
        }

        private static int AddNodeRecursive(
            List<NodeBuildRecord> nodeRecords,
            string meshName,
            VoxelLevelData l0,
            VoxelLevelData l1,
            VoxelLevelData l2,
            CpuVoxelSurfaceBuildOptions l0BuildOptions,
            CpuVoxelSurfaceBuildOptions l1BuildOptions,
            CpuVoxelSurfaceBuildOptions l2BuildOptions,
            Bounds nodeBounds,
            int parentIndex,
            int depth,
            int maxDepth,
            int minimumSurfaceVoxelCountToSplit)
        {
            int surfaceVoxelCount = CountSurfaceVoxels(l0, nodeBounds);
            if (surfaceVoxelCount == 0)
            {
                return -1;
            }

            int nodeIndex = nodeRecords.Count;
            NodeBuildRecord nodeRecord = new NodeBuildRecord(
                nodeBounds,
                depth,
                parentIndex,
                    BuildNodeMesh(
                        l0,
                        nodeBounds,
                        $"{meshName}_Node{nodeIndex:D3}_D{depth}_L0_{l0.Resolution}",
                        l0BuildOptions),
                    BuildNodeMesh(
                        l1,
                        nodeBounds,
                        $"{meshName}_Node{nodeIndex:D3}_D{depth}_L1_{l1.Resolution}",
                        l1BuildOptions),
                    BuildNodeMesh(
                        l2,
                        nodeBounds,
                        $"{meshName}_Node{nodeIndex:D3}_D{depth}_L2_{l2.Resolution}",
                        l2BuildOptions));
            nodeRecords.Add(nodeRecord);

            if (depth >= maxDepth || surfaceVoxelCount < minimumSurfaceVoxelCountToSplit)
            {
                return nodeIndex;
            }

            Bounds[] childBounds = SplitIntoOctants(nodeBounds);
            int[] childSurfaceCounts = new int[8];
            int occupiedChildCount = 0;
            for (int octant = 0; octant < childBounds.Length; octant++)
            {
                childSurfaceCounts[octant] = CountSurfaceVoxels(l0, childBounds[octant]);
                if (childSurfaceCounts[octant] > 0)
                {
                    occupiedChildCount++;
                }
            }

            if (occupiedChildCount < 2)
            {
                return nodeIndex;
            }

            int firstChildIndex = -1;
            byte childMask = 0;
            for (int octant = 0; octant < childBounds.Length; octant++)
            {
                if (childSurfaceCounts[octant] == 0)
                {
                    continue;
                }

                int childIndex = AddNodeRecursive(
                    nodeRecords,
                    meshName,
                    l0,
                    l1,
                    l2,
                    l0BuildOptions,
                    l1BuildOptions,
                    l2BuildOptions,
                    childBounds[octant],
                    nodeIndex,
                    depth + 1,
                    maxDepth,
                    minimumSurfaceVoxelCountToSplit);
                if (childIndex < 0)
                {
                    continue;
                }

                if (firstChildIndex < 0)
                {
                    firstChildIndex = childIndex;
                }

                childMask |= (byte)(1 << octant);
            }

            if (firstChildIndex >= 0)
            {
                nodeRecord.FirstChildIndex = firstChildIndex;
                nodeRecord.ChildMask = childMask;
            }

            return nodeIndex;
        }

        private static int CountSurfaceVoxels(VoxelLevelData level, Bounds nodeBounds)
        {
            int count = 0;
            for (int z = 0; z < level.Depth; z++)
            {
                for (int y = 0; y < level.Height; y++)
                {
                    for (int x = 0; x < level.Width; x++)
                    {
                        if (!level.IsOccupied(x, y, z) || !IsVoxelOwnedByBounds(level, x, y, z, nodeBounds))
                        {
                            continue;
                        }

                        if (IsSurfaceVoxel(level, x, y, z))
                        {
                            count++;
                        }
                    }
                }
            }

            return count;
        }

        private static Mesh BuildNodeMesh(
            VoxelLevelData level,
            Bounds nodeBounds,
            string meshName,
            CpuVoxelSurfaceBuildOptions buildOptions)
        {
            return CpuVoxelSurfaceMeshBuilder.BuildSurfaceMesh(level.Volume, nodeBounds, meshName, buildOptions);
        }

        private static bool ShouldEmitFace(VoxelLevelData level, int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= level.Width || y >= level.Height || z >= level.Depth)
            {
                return true;
            }

            return !level.IsOccupied(x, y, z);
        }

        private static bool IsSurfaceVoxel(VoxelLevelData level, int x, int y, int z)
        {
            return ShouldEmitFace(level, x + 1, y, z) ||
                   ShouldEmitFace(level, x - 1, y, z) ||
                   ShouldEmitFace(level, x, y + 1, z) ||
                   ShouldEmitFace(level, x, y - 1, z) ||
                   ShouldEmitFace(level, x, y, z + 1) ||
                   ShouldEmitFace(level, x, y, z - 1);
        }

        private static bool IsVoxelOwnedByBounds(VoxelLevelData level, int x, int y, int z, Bounds nodeBounds)
        {
            Vector3 cellCenter = level.GetCellCenter(x, y, z);
            Vector3 min = nodeBounds.min;
            Vector3 max = nodeBounds.max;
            return cellCenter.x >= min.x &&
                   cellCenter.x < max.x &&
                   cellCenter.y >= min.y &&
                   cellCenter.y < max.y &&
                   cellCenter.z >= min.z &&
                   cellCenter.z < max.z;
        }

        private static Bounds[] SplitIntoOctants(Bounds parentBounds)
        {
            Bounds[] childBounds = new Bounds[8];
            Vector3 parentMin = parentBounds.min;
            Vector3 parentMax = parentBounds.max;
            Vector3 center = parentBounds.center;

            for (int octant = 0; octant < childBounds.Length; octant++)
            {
                Vector3 childMin = new Vector3(
                    (octant & 1) == 0 ? parentMin.x : center.x,
                    (octant & 2) == 0 ? parentMin.y : center.y,
                    (octant & 4) == 0 ? parentMin.z : center.z);
                Vector3 childMax = new Vector3(
                    (octant & 1) == 0 ? center.x : parentMax.x,
                    (octant & 2) == 0 ? center.y : parentMax.y,
                    (octant & 4) == 0 ? center.z : parentMax.z);
                childBounds[octant] = new Bounds((childMin + childMax) * 0.5f, childMax - childMin);
            }

            return childBounds;
        }

        private sealed class NodeBuildRecord
        {
            public NodeBuildRecord(Bounds localBounds, int depth, int parentIndex, Mesh shellL0Mesh, Mesh shellL1Mesh, Mesh shellL2Mesh)
            {
                LocalBounds = localBounds;
                Depth = depth;
                ParentIndex = parentIndex;
                ShellL0Mesh = shellL0Mesh;
                ShellL1Mesh = shellL1Mesh;
                ShellL2Mesh = shellL2Mesh;
            }

            public Bounds LocalBounds { get; }

            public int Depth { get; }

            public int ParentIndex { get; }

            public int FirstChildIndex { get; set; } = -1;

            public byte ChildMask { get; set; }

            public Mesh ShellL0Mesh { get; }

            public Mesh ShellL1Mesh { get; }

            public Mesh ShellL2Mesh { get; }
        }

        private readonly struct VoxelLevelData
        {
            public VoxelLevelData(CpuVoxelVolume volume, int resolution)
            {
                Volume = volume ?? throw new ArgumentNullException(nameof(volume));
                Resolution = resolution;
            }

            public CpuVoxelVolume Volume { get; }

            public int Resolution { get; }

            public int Width => Volume.Width;

            public int Height => Volume.Height;

            public int Depth => Volume.Depth;

            public bool IsOccupied(int x, int y, int z)
            {
                return Volume.IsFilled(x, y, z);
            }

            public Vector3 GetCellCenter(int x, int y, int z)
            {
                return Volume.GetCellCenter(x, y, z);
            }
        }
    }
}
