#nullable enable

using System;
using System.Collections.Generic;
using MeshVoxelizerProject;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Editor-only impostor baker for assembled tree blueprints.
    /// </summary>
    public static class ImpostorMeshGenerator
    {
        /// <summary>
        /// [INTEGRATION] Called from editor tooling to populate the far-LOD impostor mesh on one tree blueprint.
        /// </summary>
        public static void BakeImpostorMesh(TreeBlueprintSO blueprint, ImpostorBakeSettings? settings = null)
        {
            // Range: requires a readable trunk mesh plus readable shellL2 canopy and simplified L2 wood meshes on every placed branch prototype. Condition: merged source geometry stays in tree local space. Output: impostorMesh is assigned on the blueprint asset.
            if (blueprint == null)
            {
                throw new ArgumentNullException(nameof(blueprint));
            }

            Mesh combinedTreeMesh = CreateCombinedTreeSpaceMesh(blueprint);
            ImpostorBakeSettings activeSettings = settings ?? new ImpostorBakeSettings();
            int targetTriangles = Mathf.Max(1, activeSettings.TargetTriangles);
            int voxelResolution = Mathf.Clamp(Mathf.CeilToInt(Mathf.Pow(targetTriangles * 1.5f, 1f / 3f)) * 2, 8, 20);
            MeshVoxelizerHierarchyNode[] hierarchyNodes = MeshVoxelizerHierarchyBuilder.BuildHierarchy(
                combinedTreeMesh,
                voxelResolution,
                voxelResolution,
                voxelResolution,
                0,
                1);
            Mesh? surfaceMesh = hierarchyNodes.Length == 0 ? null : hierarchyNodes[0].ShellL0Mesh;
            if (surfaceMesh == null || surfaceMesh.triangles.Length == 0)
            {
                UnityEngine.Object.DestroyImmediate(combinedTreeMesh);
                throw new InvalidOperationException($"{blueprint.name} did not produce a valid impostor surface mesh.");
            }

            Mesh simplifiedMesh = surfaceMesh; // no simplification for now.
            DestroyUnusedHierarchyMeshes(hierarchyNodes, simplifiedMesh);
            UnityEngine.Object.DestroyImmediate(combinedTreeMesh);

            SerializedObject serializedBlueprint = new SerializedObject(blueprint);
            serializedBlueprint.FindProperty("impostorMesh").objectReferenceValue =
                GeneratedMeshAssetUtility.PersistGeneratedMesh(blueprint, $"{blueprint.name}_Impostor", simplifiedMesh);
            serializedBlueprint.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(blueprint);
        }

        private static void DestroyUnusedHierarchyMeshes(MeshVoxelizerHierarchyNode[] hierarchyNodes, Mesh retainedMesh)
        {
            for (int i = 0; i < hierarchyNodes.Length; i++)
            {
                DestroyIfDifferent(hierarchyNodes[i].ShellL0Mesh, retainedMesh);
                DestroyIfDifferent(hierarchyNodes[i].ShellL1Mesh, retainedMesh);
                DestroyIfDifferent(hierarchyNodes[i].ShellL2Mesh, retainedMesh);
            }
        }

        private static void DestroyIfDifferent(Mesh? mesh, Mesh retainedMesh)
        {
            if (mesh != null && !ReferenceEquals(mesh, retainedMesh))
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static Mesh CreateCombinedTreeSpaceMesh(TreeBlueprintSO blueprint)
        {
            Mesh trunkMesh = blueprint.TrunkMesh ?? throw new InvalidOperationException($"{blueprint.name} is missing trunkMesh.");
            if (!trunkMesh.isReadable)
            {
                throw new InvalidOperationException($"{blueprint.name} trunkMesh must be readable before impostor baking.");
            }

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            AppendMesh(vertices, triangles, trunkMesh, Matrix4x4.identity);

            BranchPlacement[] placements = blueprint.Branches;
            if (placements == null || placements.Length == 0)
            {
                throw new InvalidOperationException($"{blueprint.name} does not contain any branch placements.");
            }

            for (int i = 0; i < placements.Length; i++)
            {
                BranchPlacement placement = placements[i] ?? throw new InvalidOperationException($"{blueprint.name} branch placement {i} is missing.");
                BranchPrototypeSO prototype = placement.Prototype ?? throw new InvalidOperationException($"{blueprint.name} branch placement {i} is missing prototype.");
                Mesh shellL2WoodMesh = prototype.ShellL2WoodMesh ?? throw new InvalidOperationException($"{prototype.name} is missing shellL2WoodMesh.");
                if (!shellL2WoodMesh.isReadable)
                {
                    throw new InvalidOperationException($"{prototype.name} shellL2WoodMesh must be readable before impostor baking.");
                }

                List<BranchShellNode> leafNodes = BranchShellNodeUtility.CollectLeafNodes(prototype.ShellNodes);
                if (leafNodes.Count == 0)
                {
                    throw new InvalidOperationException($"{prototype.name} is missing shellNodes for impostor baking.");
                }

                Matrix4x4 branchMatrix = Matrix4x4.TRS(
                    placement.LocalPosition,
                    placement.LocalRotation,
                    Vector3.one * placement.Scale);

                AppendMesh(vertices, triangles, shellL2WoodMesh, branchMatrix);
                for (int leafIndex = 0; leafIndex < leafNodes.Count; leafIndex++)
                {
                    Mesh shellL2Mesh = leafNodes[leafIndex].ShellL2Mesh ??
                                       throw new InvalidOperationException($"{prototype.name} leaf node {leafIndex} is missing shellL2Mesh.");
                    if (!shellL2Mesh.isReadable)
                    {
                        throw new InvalidOperationException($"{prototype.name} leaf node {leafIndex} shellL2Mesh must be readable before impostor baking.");
                    }

                    AppendMesh(vertices, triangles, shellL2Mesh, branchMatrix);
                }
            }

            Mesh combinedMesh = new Mesh
            {
                name = $"{blueprint.name}_ImpostorSource",
                indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };

            combinedMesh.SetVertices(vertices);
            combinedMesh.SetTriangles(triangles, 0, true);
            combinedMesh.RecalculateBounds();
            return combinedMesh;
        }

        private static void AppendMesh(List<Vector3> vertices, List<int> triangles, Mesh mesh, Matrix4x4 transformMatrix)
        {
            Vector3[] sourceVertices = mesh.vertices;
            int[] sourceTriangles = mesh.triangles;
            int startIndex = vertices.Count;

            for (int vertexIndex = 0; vertexIndex < sourceVertices.Length; vertexIndex++)
            {
                vertices.Add(transformMatrix.MultiplyPoint3x4(sourceVertices[vertexIndex]));
            }

            for (int triangleIndex = 0; triangleIndex < sourceTriangles.Length; triangleIndex++)
            {
                triangles.Add(startIndex + sourceTriangles[triangleIndex]);
            }
        }
    }
}
