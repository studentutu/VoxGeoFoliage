#nullable enable

using System;
using System.Reflection;
using MeshVoxelizerProject;
using UnityEditor;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Editor-only hierarchical canopy shell baker for reusable branch prototypes.
    /// </summary>
    public static class CanopyShellGenerator
    {
        /// <summary>
        /// [INTEGRATION] Called from editor tooling to populate hierarchical shell data on one branch prototype.
        /// </summary>
        public static void BakeCanopyShells(BranchPrototypeSO prototype, ShellBakeSettings? settings = null)
        {
            // Range: requires readable foliage and wood meshes plus valid shell budgets. Condition: the hierarchy builder generates 80/16/6 shell meshes from the CPU voxel volume backend and L0 surface voxels drive the hierarchy split. Output: the prototype receives shellNodes plus refreshed simplified wood attachments.
            if (prototype == null)
            {
                throw new ArgumentNullException(nameof(prototype));
            }

            Mesh foliageMesh = prototype.FoliageMesh ??
                               throw new InvalidOperationException($"{prototype.name} is missing foliageMesh.");
            Mesh woodMesh = prototype.WoodMesh ??
                            throw new InvalidOperationException($"{prototype.name} is missing woodMesh.");
            if (!foliageMesh.isReadable)
            {
                throw new InvalidOperationException(
                    $"{prototype.name} foliageMesh must be readable before shell baking.");
            }

            if (!woodMesh.isReadable)
            {
                throw new InvalidOperationException($"{prototype.name} woodMesh must be readable before shell baking.");
            }

            ShellBakeSettings activeSettings = settings ?? prototype.ShellBakeSettings;
            MeshVoxelizerHierarchyNode[] hierarchyNodes = MeshVoxelizerHierarchyBuilder.BuildHierarchy(
                foliageMesh,
                activeSettings.VoxelResolutionL0,
                activeSettings.VoxelResolutionL1,
                activeSettings.VoxelResolutionL2,
                activeSettings.MaxOctreeDepth,
                activeSettings.MinimumSurfaceVoxelCountToSplit);
            if (hierarchyNodes.Length == 0)
            {
                throw new InvalidOperationException($"{foliageMesh.name} produced no occupied shell nodes.");
            }

            string nodeMeshPrefix = $"{prototype.name}_ShellNode";
            BranchShellNode[] persistedNodes = PersistShellNodes(prototype, hierarchyNodes, nodeMeshPrefix);
            Mesh shellL1WoodMesh = BuildWoodAttachmentMesh(woodMesh, 1);
            Mesh shellL2WoodMesh = BuildWoodAttachmentMesh(woodMesh, 2);

            SetPrivateField(
                prototype,
                "shellL1WoodMesh",
                GeneratedMeshAssetUtility.PersistGeneratedMesh(
                    prototype,
                    $"{prototype.name}_ShellL1Wood",
                    shellL1WoodMesh,
                    prototype.GeneratedCanopyShellsRelativeFolder));
            SetPrivateField(
                prototype,
                "shellL2WoodMesh",
                GeneratedMeshAssetUtility.PersistGeneratedMesh(
                    prototype,
                    $"{prototype.name}_ShellL2Wood",
                    shellL2WoodMesh,
                    prototype.GeneratedCanopyShellsRelativeFolder));
            SetPrivateField(prototype, "shellNodes", persistedNodes);

            EditorUtility.SetDirty(prototype);
        }

        private static BranchShellNode[] PersistShellNodes(BranchPrototypeSO prototype,
            MeshVoxelizerHierarchyNode[] hierarchyNodes, string nodeMeshPrefix)
        {
            BranchShellNode[] persistedNodes = new BranchShellNode[hierarchyNodes.Length];
            for (int i = 0; i < hierarchyNodes.Length; i++)
            {
                MeshVoxelizerHierarchyNode node = hierarchyNodes[i];
                string nodePath = $"D{node.Depth}_{i:D3}";
                Mesh persistedL0 = GeneratedMeshAssetUtility.PersistGeneratedMesh(
                    prototype,
                    $"{nodeMeshPrefix}_{nodePath}_L0",
                    node.ShellL0Mesh ?? throw new InvalidOperationException($"Node {i} is missing L0 shell mesh."),
                    prototype.GeneratedCanopyShellsRelativeFolder);
                Mesh persistedL1 = GeneratedMeshAssetUtility.PersistGeneratedMesh(
                    prototype,
                    $"{nodeMeshPrefix}_{nodePath}_L1",
                    node.ShellL1Mesh ?? throw new InvalidOperationException($"Node {i} is missing L1 shell mesh."),
                    prototype.GeneratedCanopyShellsRelativeFolder);
                Mesh persistedL2 = GeneratedMeshAssetUtility.PersistGeneratedMesh(
                    prototype,
                    $"{nodeMeshPrefix}_{nodePath}_L2",
                    node.ShellL2Mesh ?? throw new InvalidOperationException($"Node {i} is missing L2 shell mesh."),
                    prototype.GeneratedCanopyShellsRelativeFolder);

                persistedNodes[i] = new BranchShellNode(
                    node.LocalBounds,
                    node.Depth,
                    node.FirstChildIndex,
                    node.ChildMask,
                    persistedL0,
                    persistedL1,
                    persistedL2);
            }

            return persistedNodes;
        }

        private static Mesh BuildWoodAttachmentMesh(Mesh sourceWoodMesh, int shellLevel)
        {
            Mesh clonedMesh = UnityEngine.Object.Instantiate(sourceWoodMesh);
            clonedMesh.name = $"{sourceWoodMesh.name}_ShellL{shellLevel}Wood";
            return clonedMesh;
        }

        private static void SetPrivateField(object target, string fieldName, object? value)
        {
            FieldInfo? fieldInfo = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (fieldInfo == null)
            {
                throw new InvalidOperationException($"Field '{fieldName}' was not found on '{target.GetType().Name}'.");
            }

            fieldInfo.SetValue(target, value);
        }
    }
}