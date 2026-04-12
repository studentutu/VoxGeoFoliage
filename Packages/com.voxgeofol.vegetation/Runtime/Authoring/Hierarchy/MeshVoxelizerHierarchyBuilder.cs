#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelSystem;

namespace MeshVoxelizerProject
{
    /// <summary>
    /// Builds canonical L0 ownership hierarchy data and compact L1/L2 hierarchies from that owned occupancy.
    /// </summary>
    public static class MeshVoxelizerHierarchyBuilder
    {
        public static bool Generating = false;

        /// <summary>
        /// [INTEGRATION] Builds the canonical L0 hierarchy for compatibility/demo callers.
        /// </summary>
        public static MeshVoxelizerHierarchyNode[] BuildHierarchy(
            Mesh sourceMesh,
            int voxelResolutionL0,
            int voxelResolutionL1,
            int voxelResolutionL2,
            int maxDepth = 2,
            int minimumSurfaceVoxelCountToSplit = 4)
        {
            BuildHierarchies(
                sourceMesh,
                voxelResolutionL0,
                voxelResolutionL1,
                voxelResolutionL2,
                CpuVoxelSurfaceBuildOptions.Reduced,
                CpuVoxelSurfaceBuildOptions.Reduced,
                CpuVoxelSurfaceBuildOptions.Reduced,
                out MeshVoxelizerHierarchyNode[] hierarchyL0,
                out _,
                out _,
                maxDepth,
                minimumSurfaceVoxelCountToSplit);
            return hierarchyL0;
        }

        /// <summary>
        /// [INTEGRATION] Builds canonical L0 plus compact L1/L2 hierarchies from owned L0 occupancy.
        /// </summary>
        public static void BuildHierarchies(
            Mesh sourceMesh,
            int voxelResolutionL0,
            int voxelResolutionL1,
            int voxelResolutionL2,
            int maxDepth,
            int minimumSurfaceVoxelCountToSplit,
            out MeshVoxelizerHierarchyNode[] hierarchyL0,
            out MeshVoxelizerHierarchyNode[] hierarchyL1,
            out MeshVoxelizerHierarchyNode[] hierarchyL2)
        {
            BuildHierarchies(
                sourceMesh,
                voxelResolutionL0,
                voxelResolutionL1,
                voxelResolutionL2,
                CpuVoxelSurfaceBuildOptions.Reduced,
                CpuVoxelSurfaceBuildOptions.Reduced,
                CpuVoxelSurfaceBuildOptions.Reduced,
                out hierarchyL0,
                out hierarchyL1,
                out hierarchyL2,
                maxDepth,
                minimumSurfaceVoxelCountToSplit);
        }

        /// <summary>
        /// [INTEGRATION] Builds canonical L0 plus compact L1/L2 hierarchies from owned L0 occupancy.
        /// </summary>
        public static void BuildHierarchies(
            Mesh sourceMesh,
            int voxelResolutionL0,
            int voxelResolutionL1,
            int voxelResolutionL2,
            CpuVoxelSurfaceBuildOptions l0BuildOptions,
            CpuVoxelSurfaceBuildOptions l1BuildOptions,
            CpuVoxelSurfaceBuildOptions l2BuildOptions,
            out MeshVoxelizerHierarchyNode[] hierarchyL0,
            out MeshVoxelizerHierarchyNode[] hierarchyL1,
            out MeshVoxelizerHierarchyNode[] hierarchyL2,
            int maxDepth = 2,
            int minimumSurfaceVoxelCountToSplit = 4)
        {
            Generating = true;
            try
            {
                // Range: sourceMesh must be readable and the resolutions must be >= 2. Condition: L0 owns subdivision and bounds authority, while L1/L2 are rebuilt from that owned occupancy into their own compact hierarchies. Output: three persisted hierarchy arrays with tier-specific bounds and meshes.
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

                sourceMesh.RecalculateBounds();
                Bounds sourceBounds = sourceMesh.bounds;
                if (sourceBounds.size.x <= Mathf.Epsilon ||
                    sourceBounds.size.y <= Mathf.Epsilon ||
                    sourceBounds.size.z <= Mathf.Epsilon)
                {
                    throw new InvalidOperationException($"{sourceMesh.name} has invalid bounds {sourceBounds}.");
                }

                VoxelLevelData l0 = CreateVoxelLevel(sourceMesh, voxelResolutionL0);
                HierarchyTreeNode canonicalRoot = BuildCanonicalNodeRecursive(
                    sourceMesh.name,
                    l0,
                    sourceBounds,
                    l0BuildOptions,
                    0,
                    maxDepth,
                    Mathf.Max(1, minimumSurfaceVoxelCountToSplit)) ??
                    throw new InvalidOperationException($"{sourceMesh.name} produced no canonical shell hierarchy.");

                HierarchyTreeNode compactL1Root = BuildCompactNodeRecursive(
                    sourceMesh.name,
                    canonicalRoot,
                    l0,
                    voxelResolutionL1,
                    l1BuildOptions,
                    1);
                HierarchyTreeNode compactL2Root = BuildCompactNodeRecursive(
                    sourceMesh.name,
                    canonicalRoot,
                    l0,
                    voxelResolutionL2,
                    l2BuildOptions,
                    2);

                hierarchyL0 = FlattenHierarchy(canonicalRoot, 0);
                hierarchyL1 = FlattenHierarchy(compactL1Root, 1);
                hierarchyL2 = FlattenHierarchy(compactL2Root, 2);
            }
            finally
            {
                Generating = false;
            }
        }

        private static HierarchyTreeNode? BuildCanonicalNodeRecursive(
            string meshName,
            VoxelLevelData l0,
            Bounds ownershipBounds,
            CpuVoxelSurfaceBuildOptions buildOptions,
            int depth,
            int maxDepth,
            int minimumSurfaceVoxelCountToSplit)
        {
            int surfaceVoxelCount = CountSurfaceVoxels(l0, ownershipBounds);
            if (surfaceVoxelCount == 0)
            {
                return null;
            }

            Mesh shellMesh = BuildOwnedNodeMesh(
                l0,
                ownershipBounds,
                ownershipBounds,
                $"{meshName}_L0_D{depth}_{ownershipBounds.center.x:F3}_{ownershipBounds.center.y:F3}_{ownershipBounds.center.z:F3}",
                buildOptions);
            HierarchyTreeNode node = new HierarchyTreeNode(
                ownershipBounds,
                ownershipBounds,
                depth,
                shellMesh);

            if (depth >= maxDepth || surfaceVoxelCount < minimumSurfaceVoxelCountToSplit)
            {
                node.LeafTriangleCount = GetTriangleCount(shellMesh);
                return node;
            }

            Bounds[] childBounds = SplitIntoOctants(ownershipBounds);
            int occupiedChildCount = 0;
            int[] childSurfaceCounts = new int[childBounds.Length];
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
                node.LeafTriangleCount = GetTriangleCount(shellMesh);
                return node;
            }

            int leafTriangleCount = 0;
            for (int octant = 0; octant < childBounds.Length; octant++)
            {
                if (childSurfaceCounts[octant] == 0)
                {
                    continue;
                }

                HierarchyTreeNode? childNode = BuildCanonicalNodeRecursive(
                    meshName,
                    l0,
                    childBounds[octant],
                    buildOptions,
                    depth + 1,
                    maxDepth,
                    minimumSurfaceVoxelCountToSplit);
                if (childNode == null)
                {
                    continue;
                }

                node.Children.Add(new HierarchyTreeChild(octant, childNode));
                leafTriangleCount += childNode.LeafTriangleCount;
            }

            node.LeafTriangleCount = node.Children.Count == 0
                ? GetTriangleCount(shellMesh)
                : leafTriangleCount;
            return node;
        }

        private static HierarchyTreeNode BuildCompactNodeRecursive(
            string meshName,
            HierarchyTreeNode canonicalNode,
            VoxelLevelData canonicalL0,
            int targetResolution,
            CpuVoxelSurfaceBuildOptions buildOptions,
            int shellLevel)
        {
            Mesh compactMesh = BuildCompactNodeMesh(
                meshName,
                canonicalNode,
                canonicalL0,
                targetResolution,
                buildOptions,
                shellLevel);
            HierarchyTreeNode compactNode = new HierarchyTreeNode(
                canonicalNode.OwnershipBounds,
                canonicalNode.OwnershipBounds,
                canonicalNode.Depth,
                compactMesh);

            if (canonicalNode.Children.Count == 0)
            {
                compactNode.LeafTriangleCount = GetTriangleCount(compactMesh);
                return compactNode;
            }

            List<HierarchyTreeChild> compactChildren = new List<HierarchyTreeChild>(canonicalNode.Children.Count);
            int compactChildLeafTriangles = 0;
            for (int i = 0; i < canonicalNode.Children.Count; i++)
            {
                HierarchyTreeChild canonicalChild = canonicalNode.Children[i];
                HierarchyTreeNode compactChild = BuildCompactNodeRecursive(
                    meshName,
                    canonicalChild.Node,
                    canonicalL0,
                    targetResolution,
                    buildOptions,
                    shellLevel);
                compactChildren.Add(new HierarchyTreeChild(canonicalChild.Octant, compactChild));
                compactChildLeafTriangles += compactChild.LeafTriangleCount;
            }

            int compactTriangles = GetTriangleCount(compactMesh);
            // Compact tiers intentionally collapse to one node when the resampled shell is cheaper than keeping the child frontier.
            if (compactTriangles > 0 && compactTriangles <= compactChildLeafTriangles)
            {
                compactNode.LeafTriangleCount = compactTriangles;
                return compactNode;
            }

            compactNode.Children.AddRange(compactChildren);
            compactNode.LeafTriangleCount = compactChildLeafTriangles;
            return compactNode;
        }

        private static Mesh BuildCompactNodeMesh(
            string meshName,
            HierarchyTreeNode canonicalNode,
            VoxelLevelData canonicalL0,
            int targetResolution,
            CpuVoxelSurfaceBuildOptions buildOptions,
            int shellLevel)
        {
            CpuVoxelVolume resampledVolume = CreateResampledVolume(canonicalL0, canonicalNode.OwnershipBounds, targetResolution);
            return CpuVoxelSurfaceMeshBuilder.BuildSurfaceMesh(
                resampledVolume,
                null,
                canonicalNode.LocalBounds,
                $"{meshName}_L{shellLevel}_D{canonicalNode.Depth}_{targetResolution}",
                buildOptions);
        }

        private static Mesh BuildOwnedNodeMesh(
            VoxelLevelData level,
            Bounds ownershipBounds,
            Bounds clipBounds,
            string meshName,
            CpuVoxelSurfaceBuildOptions buildOptions)
        {
            return CpuVoxelSurfaceMeshBuilder.BuildSurfaceMesh(
                level.Volume,
                ownershipBounds,
                clipBounds,
                meshName,
                buildOptions);
        }

        private static MeshVoxelizerHierarchyNode[] FlattenHierarchy(HierarchyTreeNode root, int shellLevel)
        {
            // Range: root must be non-null. Condition: MVP flattening uses BFS so immediate children occupy one contiguous block; this favors simple hybrid decode before the later DFS/subtree-span upgrade. Output: one flattened hierarchy array for the selected authored shell tier.
            List<HierarchyTreeNode> orderedNodes = new List<HierarchyTreeNode>();
            List<int> parentIndices = new List<int>();
            Dictionary<HierarchyTreeNode, int> nodeIndices = new Dictionary<HierarchyTreeNode, int>();
            Queue<HierarchyQueueRecord> frontier = new Queue<HierarchyQueueRecord>();
            frontier.Enqueue(new HierarchyQueueRecord(root, -1));

            while (frontier.Count > 0)
            {
                HierarchyQueueRecord record = frontier.Dequeue();
                int currentIndex = orderedNodes.Count;
                orderedNodes.Add(record.Node);
                parentIndices.Add(record.ParentIndex);
                nodeIndices.Add(record.Node, currentIndex);

                for (int i = 0; i < record.Node.Children.Count; i++)
                {
                    frontier.Enqueue(new HierarchyQueueRecord(record.Node.Children[i].Node, currentIndex));
                }
            }

            MeshVoxelizerHierarchyNode[] hierarchy = new MeshVoxelizerHierarchyNode[orderedNodes.Count];
            for (int i = 0; i < orderedNodes.Count; i++)
            {
                HierarchyTreeNode node = orderedNodes[i];
                int firstChildIndex = node.Children.Count == 0 ? -1 : nodeIndices[node.Children[0].Node];
                byte childMask = 0;
                for (int childIndex = 0; childIndex < node.Children.Count; childIndex++)
                {
                    childMask |= (byte)(1 << node.Children[childIndex].Octant);
                }

                Mesh? shellL0Mesh = null;
                Mesh? shellL1Mesh = null;
                Mesh? shellL2Mesh = null;
                switch (shellLevel)
                {
                    case 0:
                        shellL0Mesh = node.ShellMesh;
                        break;
                    case 1:
                        shellL1Mesh = node.ShellMesh;
                        break;
                    case 2:
                        shellL2Mesh = node.ShellMesh;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(shellLevel), shellLevel, "Shell level must be 0, 1, or 2.");
                }

                hierarchy[i] = new MeshVoxelizerHierarchyNode(
                    node.LocalBounds,
                    node.Depth,
                    parentIndices[i],
                    firstChildIndex,
                    childMask,
                    shellL0Mesh,
                    shellL1Mesh,
                    shellL2Mesh);
            }

            return hierarchy;
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

        private static CpuVoxelVolume CreateResampledVolume(VoxelLevelData sourceLevel, Bounds ownershipBounds, int resolution)
        {
            float maxLength = Mathf.Max(ownershipBounds.size.x, Mathf.Max(ownershipBounds.size.y, ownershipBounds.size.z));
            if (maxLength <= Mathf.Epsilon)
            {
                throw new InvalidOperationException($"Cannot resample zero-sized ownership bounds {ownershipBounds}.");
            }

            float unit = maxLength / resolution;
            int width = Mathf.Max(1, Mathf.CeilToInt(ownershipBounds.size.x / unit));
            int height = Mathf.Max(1, Mathf.CeilToInt(ownershipBounds.size.y / unit));
            int depth = Mathf.Max(1, Mathf.CeilToInt(ownershipBounds.size.z / unit));
            Voxel_t[,,] coarseVoxels = new Voxel_t[width, height, depth];
            Vector3 coarseMin = ownershipBounds.min;
            float fineUnit = sourceLevel.Volume.UnitLength;

            for (int z = 0; z < sourceLevel.Depth; z++)
            {
                for (int y = 0; y < sourceLevel.Height; y++)
                {
                    for (int x = 0; x < sourceLevel.Width; x++)
                    {
                        if (!sourceLevel.IsOccupied(x, y, z) || !IsVoxelOwnedByBounds(sourceLevel, x, y, z, ownershipBounds))
                        {
                            continue;
                        }

                        Bounds fineBounds = new Bounds(sourceLevel.GetCellCenter(x, y, z), Vector3.one * fineUnit);
                        if (!TryIntersectBounds(fineBounds, ownershipBounds, out Bounds clippedFineBounds))
                        {
                            continue;
                        }

                        Vector3 clippedMin = clippedFineBounds.min;
                        Vector3 clippedMax = clippedFineBounds.max;
                        int minX = Mathf.Clamp(Mathf.FloorToInt((clippedMin.x - coarseMin.x) / unit), 0, width - 1);
                        int minY = Mathf.Clamp(Mathf.FloorToInt((clippedMin.y - coarseMin.y) / unit), 0, height - 1);
                        int minZ = Mathf.Clamp(Mathf.FloorToInt((clippedMin.z - coarseMin.z) / unit), 0, depth - 1);
                        int maxX = Mathf.Clamp(Mathf.CeilToInt((clippedMax.x - coarseMin.x) / unit) - 1, 0, width - 1);
                        int maxY = Mathf.Clamp(Mathf.CeilToInt((clippedMax.y - coarseMin.y) / unit) - 1, 0, height - 1);
                        int maxZ = Mathf.Clamp(Mathf.CeilToInt((clippedMax.z - coarseMin.z) / unit) - 1, 0, depth - 1);

                        for (int coarseZ = minZ; coarseZ <= maxZ; coarseZ++)
                        {
                            for (int coarseY = minY; coarseY <= maxY; coarseY++)
                            {
                                for (int coarseX = minX; coarseX <= maxX; coarseX++)
                                {
                                    Voxel_t coarseVoxel = coarseVoxels[coarseX, coarseY, coarseZ];
                                    coarseVoxel.fill = 1u;
                                    coarseVoxels[coarseX, coarseY, coarseZ] = coarseVoxel;
                                }
                            }
                        }
                    }
                }
            }

            return new CpuVoxelVolume(coarseMin, unit, coarseVoxels);
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

        private static int GetTriangleCount(Mesh? mesh)
        {
            return mesh == null ? 0 : checked((int)(mesh.GetIndexCount(0) / 3L));
        }

        private static bool TryIntersectBounds(Bounds a, Bounds b, out Bounds intersection)
        {
            Vector3 min = Vector3.Max(a.min, b.min);
            Vector3 max = Vector3.Min(a.max, b.max);
            if (max.x <= min.x || max.y <= min.y || max.z <= min.z)
            {
                intersection = default;
                return false;
            }

            intersection = new Bounds((min + max) * 0.5f, max - min);
            return true;
        }

        private sealed class HierarchyTreeNode
        {
            public HierarchyTreeNode(Bounds ownershipBounds, Bounds localBounds, int depth, Mesh shellMesh)
            {
                OwnershipBounds = ownershipBounds;
                LocalBounds = localBounds;
                Depth = depth;
                ShellMesh = shellMesh ?? throw new ArgumentNullException(nameof(shellMesh));
            }

            public Bounds OwnershipBounds { get; }

            public Bounds LocalBounds { get; }

            public int Depth { get; }

            public Mesh ShellMesh { get; }

            public List<HierarchyTreeChild> Children { get; } = new List<HierarchyTreeChild>();

            public int LeafTriangleCount { get; set; }
        }

        private sealed class HierarchyTreeChild
        {
            public HierarchyTreeChild(int octant, HierarchyTreeNode node)
            {
                Octant = octant;
                Node = node ?? throw new ArgumentNullException(nameof(node));
            }

            public int Octant { get; }

            public HierarchyTreeNode Node { get; }
        }

        private readonly struct HierarchyQueueRecord
        {
            public HierarchyQueueRecord(HierarchyTreeNode node, int parentIndex)
            {
                Node = node;
                ParentIndex = parentIndex;
            }

            public HierarchyTreeNode Node { get; }

            public int ParentIndex { get; }
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
