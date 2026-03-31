#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MeshVoxelizerProject
{
    /// <summary>
    /// Builds an  shell hierarchy by splitting MeshVoxelizer surface voxels into octants.
    /// </summary>
    /// TODO: Move to Editor and replace with VoxelSystem.CpuMeshBuildUtility and  CPUVoxelizer!
    public static class MeshVoxelizerHierarchyBuilder
    {
        public static bool Generating = false;

        /// <summary>
        /// [INTEGRATION] Builds one coarse MeshVoxelizer surface mesh without creating hierarchy nodes.
        /// </summary>
        public static Mesh BuildSurfaceMesh(Mesh sourceMesh, int voxelResolution)
        {
            Generating = true;
            try
            {
                // Range: sourceMesh must be readable and voxelResolution must be >= 2. Condition: the entire mesh bounds are voxelized once at the requested resolution. Output: one surface mesh covering the full source bounds.
                if (sourceMesh == null)
                {
                    throw new ArgumentNullException(nameof(sourceMesh));
                }

                if (!sourceMesh.isReadable)
                {
                    throw new InvalidOperationException(
                        $"{sourceMesh.name} must be readable before MeshVoxelizer surface generation.");
                }

                ValidateResolution(voxelResolution, nameof(voxelResolution));
                Bounds sourceBounds = sourceMesh.bounds;
                if (sourceBounds.size.x <= Mathf.Epsilon ||
                    sourceBounds.size.y <= Mathf.Epsilon ||
                    sourceBounds.size.z <= Mathf.Epsilon)
                {
                    throw new InvalidOperationException($"{sourceMesh.name} has invalid bounds {sourceBounds}.");
                }

                VoxelLevelData level = CreateVoxelLevel(sourceMesh, voxelResolution);
                return BuildNodeMesh(level, sourceBounds, $"{sourceMesh.name}_Surface_{voxelResolution}");
            }
            catch (Exception e)
            {
                Generating = false;
                throw;
            }
            finally
            {
                Generating = false;
            }
        }

        /// <summary>
        /// [INTEGRATION] Builds a preorder hierarchy where each node owns MeshVoxelizer meshes for L0/L1/L2.
        /// </summary>
        public static MeshVoxelizerHierarchyNode[] BuildHierarchy(
            Mesh sourceMesh,
            int voxelResolutionL0 = 16,
            int voxelResolutionL1 = 12,
            int voxelResolutionL2 = 8,
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
                    throw new InvalidOperationException(
                        $"{sourceMesh.name} must be readable before MeshVoxelizer hierarchy generation.");
                }

                ValidateResolution(voxelResolutionL0, nameof(voxelResolutionL0));
                ValidateResolution(voxelResolutionL1, nameof(voxelResolutionL1));
                ValidateResolution(voxelResolutionL2, nameof(voxelResolutionL2));
                if (maxDepth < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(maxDepth), maxDepth,
                        "maxDepth must be zero or greater.");
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
            catch (Exception e)
            {
                Generating = false;
                throw;
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
            Box3 bounds = new Box3(sourceMesh.bounds.min, sourceMesh.bounds.max);
            var width = Mathf.CeilToInt(bounds.Width);
            var height = Mathf.CeilToInt(bounds.Height);
            MeshVoxelizer voxelizer = new MeshVoxelizer(width, height, resolution);
            voxelizer.Voxelize(sourceMesh.vertices, sourceMesh.triangles, bounds);
            return new VoxelLevelData(sourceMesh.bounds, voxelizer.Voxels, resolution);
        }

        private static int AddNodeRecursive(
            List<NodeBuildRecord> nodeRecords,
            string meshName,
            VoxelLevelData l0,
            VoxelLevelData l1,
            VoxelLevelData l2,
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
                BuildNodeMesh(l0, nodeBounds, $"{meshName}_Node{nodeIndex:D3}_D{depth}_L0_{l0.Resolution}"),
                BuildNodeMesh(l1, nodeBounds, $"{meshName}_Node{nodeIndex:D3}_D{depth}_L1_{l1.Resolution}"),
                BuildNodeMesh(l2, nodeBounds, $"{meshName}_Node{nodeIndex:D3}_D{depth}_L2_{l2.Resolution}"));
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
            for (int z = 0; z < level.Resolution; z++)
            {
                for (int y = 0; y < level.Resolution; y++)
                {
                    for (int x = 0; x < level.Resolution; x++)
                    {
                        if (!level.IsOccupied(x, y, z))
                        {
                            continue;
                        }

                        if (!IsVoxelOwnedByBounds(level, x, y, z, nodeBounds))
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

        private static Mesh BuildNodeMesh(VoxelLevelData level, Bounds nodeBounds, string meshName)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            for (int z = 0; z < level.Resolution; z++)
            {
                for (int y = 0; y < level.Resolution; y++)
                {
                    for (int x = 0; x < level.Resolution; x++)
                    {
                        if (!level.IsOccupied(x, y, z) || !IsVoxelOwnedByBounds(level, x, y, z, nodeBounds))
                        {
                            continue;
                        }

                        Vector3 position = level.Min + Vector3.Scale(level.CellSize, new Vector3(x, y, z));

                        if (ShouldEmitFace(level, x + 1, y, z))
                        {
                            AddRightQuad(vertices, triangles, level.CellSize, position);
                        }

                        if (ShouldEmitFace(level, x - 1, y, z))
                        {
                            AddLeftQuad(vertices, triangles, level.CellSize, position);
                        }

                        if (ShouldEmitFace(level, x, y + 1, z))
                        {
                            AddTopQuad(vertices, triangles, level.CellSize, position);
                        }

                        if (ShouldEmitFace(level, x, y - 1, z))
                        {
                            AddBottomQuad(vertices, triangles, level.CellSize, position);
                        }

                        if (ShouldEmitFace(level, x, y, z + 1))
                        {
                            AddFrontQuad(vertices, triangles, level.CellSize, position);
                        }

                        if (ShouldEmitFace(level, x, y, z - 1))
                        {
                            AddBackQuad(vertices, triangles, level.CellSize, position);
                        }
                    }
                }
            }

            Mesh mesh = new Mesh
            {
                name = meshName,
                indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            if (triangles.Count > 0)
            {
                mesh.RecalculateNormals();
            }

            return mesh;
        }

        private static bool ShouldEmitFace(VoxelLevelData level, int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= level.Resolution || y >= level.Resolution || z >= level.Resolution)
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

        private static void AddRightQuad(List<Vector3> verts, List<int> indices, Vector3 scale, Vector3 pos)
        {
            int count = verts.Count;

            verts.Add(pos + new Vector3(scale.x, 0f, scale.z));
            verts.Add(pos + new Vector3(scale.x, scale.y, 0f));
            verts.Add(pos + new Vector3(scale.x, 0f, 0f));

            verts.Add(pos + new Vector3(scale.x, 0f, scale.z));
            verts.Add(pos + new Vector3(scale.x, scale.y, scale.z));
            verts.Add(pos + new Vector3(scale.x, scale.y, 0f));

            indices.Add(count + 2);
            indices.Add(count + 1);
            indices.Add(count + 0);
            indices.Add(count + 5);
            indices.Add(count + 4);
            indices.Add(count + 3);
        }

        private static void AddLeftQuad(List<Vector3> verts, List<int> indices, Vector3 scale, Vector3 pos)
        {
            int count = verts.Count;

            verts.Add(pos + new Vector3(0f, 0f, scale.z));
            verts.Add(pos + new Vector3(0f, scale.y, 0f));
            verts.Add(pos + new Vector3(0f, 0f, 0f));

            verts.Add(pos + new Vector3(0f, 0f, scale.z));
            verts.Add(pos + new Vector3(0f, scale.y, scale.z));
            verts.Add(pos + new Vector3(0f, scale.y, 0f));

            indices.Add(count + 0);
            indices.Add(count + 1);
            indices.Add(count + 2);
            indices.Add(count + 3);
            indices.Add(count + 4);
            indices.Add(count + 5);
        }

        private static void AddTopQuad(List<Vector3> verts, List<int> indices, Vector3 scale, Vector3 pos)
        {
            int count = verts.Count;

            verts.Add(pos + new Vector3(0f, scale.y, scale.z));
            verts.Add(pos + new Vector3(scale.x, scale.y, 0f));
            verts.Add(pos + new Vector3(0f, scale.y, 0f));

            verts.Add(pos + new Vector3(0f, scale.y, scale.z));
            verts.Add(pos + new Vector3(scale.x, scale.y, scale.z));
            verts.Add(pos + new Vector3(scale.x, scale.y, 0f));

            indices.Add(count + 0);
            indices.Add(count + 1);
            indices.Add(count + 2);
            indices.Add(count + 3);
            indices.Add(count + 4);
            indices.Add(count + 5);
        }

        private static void AddBottomQuad(List<Vector3> verts, List<int> indices, Vector3 scale, Vector3 pos)
        {
            int count = verts.Count;

            verts.Add(pos + new Vector3(0f, 0f, scale.z));
            verts.Add(pos + new Vector3(scale.x, 0f, 0f));
            verts.Add(pos + new Vector3(0f, 0f, 0f));

            verts.Add(pos + new Vector3(0f, 0f, scale.z));
            verts.Add(pos + new Vector3(scale.x, 0f, scale.z));
            verts.Add(pos + new Vector3(scale.x, 0f, 0f));

            indices.Add(count + 2);
            indices.Add(count + 1);
            indices.Add(count + 0);
            indices.Add(count + 5);
            indices.Add(count + 4);
            indices.Add(count + 3);
        }

        private static void AddFrontQuad(List<Vector3> verts, List<int> indices, Vector3 scale, Vector3 pos)
        {
            int count = verts.Count;

            verts.Add(pos + new Vector3(0f, scale.y, scale.z));
            verts.Add(pos + new Vector3(scale.x, 0f, scale.z));
            verts.Add(pos + new Vector3(0f, 0f, scale.z));

            verts.Add(pos + new Vector3(0f, scale.y, scale.z));
            verts.Add(pos + new Vector3(scale.x, scale.y, scale.z));
            verts.Add(pos + new Vector3(scale.x, 0f, scale.z));

            indices.Add(count + 2);
            indices.Add(count + 1);
            indices.Add(count + 0);
            indices.Add(count + 5);
            indices.Add(count + 4);
            indices.Add(count + 3);
        }

        private static void AddBackQuad(List<Vector3> verts, List<int> indices, Vector3 scale, Vector3 pos)
        {
            int count = verts.Count;

            verts.Add(pos + new Vector3(0f, scale.y, 0f));
            verts.Add(pos + new Vector3(scale.x, 0f, 0f));
            verts.Add(pos + new Vector3(0f, 0f, 0f));

            verts.Add(pos + new Vector3(0f, scale.y, 0f));
            verts.Add(pos + new Vector3(scale.x, scale.y, 0f));
            verts.Add(pos + new Vector3(scale.x, 0f, 0f));

            indices.Add(count + 0);
            indices.Add(count + 1);
            indices.Add(count + 2);
            indices.Add(count + 3);
            indices.Add(count + 4);
            indices.Add(count + 5);
        }

        private sealed class NodeBuildRecord
        {
            public NodeBuildRecord(Bounds localBounds, int depth, int parentIndex, Mesh shellL0Mesh, Mesh shellL1Mesh,
                Mesh shellL2Mesh)
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
            public VoxelLevelData(Bounds sourceBounds, int[,,] voxels, int resolution)
            {
                SourceBounds = sourceBounds;
                Voxels = voxels;
                Resolution = resolution;
                Min = sourceBounds.min;
                CellSize = new Vector3(
                    sourceBounds.size.x / resolution,
                    sourceBounds.size.y / resolution,
                    sourceBounds.size.z / resolution);
            }

            public Bounds SourceBounds { get; }

            public int[,,] Voxels { get; }

            public int Resolution { get; }

            public Vector3 Min { get; }

            public Vector3 CellSize { get; }

            public bool IsOccupied(int x, int y, int z)
            {
                return Voxels[x, y, z] == 1;
            }

            public Vector3 GetCellCenter(int x, int y, int z)
            {
                return Min + Vector3.Scale(CellSize, new Vector3(x + 0.5f, y + 0.5f, z + 0.5f));
            }
        }
    }
}