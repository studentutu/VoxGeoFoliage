#nullable enable

using System;
using System.Collections.Generic;
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
        private const int ImpostorTriangleBudget = 200;
        private const int ImpostorResolutionFallbackStep = 2;

        /// <summary>
        /// [INTEGRATION] Called from editor tooling to populate the far-LOD impostor mesh on one tree blueprint.
        /// </summary>
        public static void BakeImpostorMesh(TreeBlueprintSO blueprint, ImpostorBakeSettings? settings = null)
        {
            // Range: requires a readable trunk mesh plus readable source wood/foliage meshes on every placed branch prototype. Condition: merged source geometry stays in tree local space and is voxelized at very coarse resolution through the CPU voxel volume path. Output: impostorMesh is assigned on the blueprint asset.
            if (blueprint == null)
            {
                throw new ArgumentNullException(nameof(blueprint));
            }

            Mesh combinedTreeMesh = CreateCombinedTreeSpaceMesh(blueprint);
            ImpostorBakeSettings activeSettings = settings ?? blueprint.ImposterSettings;
            GeneratedMeshSimplificationUtility.GeneratedMeshCandidate impostorCandidate =
                GeneratedMeshSimplificationUtility.SelectBestVoxelMeshCandidate(
                    combinedTreeMesh,
                    Mathf.Max(2, activeSettings.VoxelResolution),
                    2,
                    ImpostorResolutionFallbackStep,
                    ImpostorTriangleBudget,
                    activeSettings.SkipReduction,
                    activeSettings.SkipSimplifyFallback,
                    $"{blueprint.name}_ImpostorSurface");
            if (impostorCandidate.Mesh.triangles.Length == 0)
            {
                GeneratedMeshSimplificationUtility.DestroyTemporaryMesh(impostorCandidate.Mesh);
                UnityEngine.Object.DestroyImmediate(combinedTreeMesh);
                throw new InvalidOperationException($"{blueprint.name} did not produce a valid impostor surface mesh.");
            }

            if (impostorCandidate.TriangleCount > ImpostorTriangleBudget)
            {
                string fallbackState = activeSettings.SkipSimplifyFallback ? "skipped" : "exhausted";
                Debug.LogError(
                    $"{blueprint.name} impostor triangle count {impostorCandidate.TriangleCount} exceeds budget {ImpostorTriangleBudget}. Final source: {impostorCandidate.SourceDescription}. Simplify fallback {fallbackState}.");
            }

            UnityEngine.Object.DestroyImmediate(combinedTreeMesh);

            SerializedObject serializedBlueprint = new SerializedObject(blueprint);
            serializedBlueprint.FindProperty("impostorMesh").objectReferenceValue =
                GeneratedMeshAssetUtility.PersistGeneratedMesh(
                    blueprint,
                    $"{blueprint.name}_Impostor",
                    impostorCandidate.Mesh,
                    blueprint.GeneratedImpostorMeshesRelativeFolder);
            serializedBlueprint.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(blueprint);

            GeneratedMeshSimplificationUtility.DestroyTemporaryMesh(impostorCandidate.Mesh);
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
                Mesh woodMesh = prototype.WoodMesh ?? throw new InvalidOperationException($"{prototype.name} is missing woodMesh.");
                Mesh foliageMesh = prototype.FoliageMesh ?? throw new InvalidOperationException($"{prototype.name} is missing foliageMesh.");
                if (!woodMesh.isReadable)
                {
                    throw new InvalidOperationException($"{prototype.name} woodMesh must be readable before impostor baking.");
                }

                if (!foliageMesh.isReadable)
                {
                    throw new InvalidOperationException($"{prototype.name} foliageMesh must be readable before impostor baking.");
                }

                Matrix4x4 branchMatrix = Matrix4x4.TRS(
                    placement.LocalPosition,
                    placement.LocalRotation,
                    Vector3.one * placement.Scale);

                AppendMesh(vertices, triangles, woodMesh, branchMatrix);
                AppendMesh(vertices, triangles, foliageMesh, branchMatrix);
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
