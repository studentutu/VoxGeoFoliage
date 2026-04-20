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
        private const int MinimumTreeL3TriangleBudget = 512;
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

        /// <summary>
        /// [INTEGRATION] Called from editor tooling to populate the mandatory whole-tree TreeL3 floor mesh on one tree blueprint.
        /// </summary>
        public static void BakeTreeL3Mesh(TreeBlueprintSO blueprint, ImpostorBakeSettings? settings = null)
        {
            // Range: requires a readable trunk mesh plus readable source branch meshes on every placed branch prototype. Condition: merged source geometry stays in tree local space and simplifies to a floor mesh materially heavier than the far impostor while remaining below the source tree cost. Output: treeL3Mesh is assigned on the blueprint asset.
            if (blueprint == null)
            {
                throw new ArgumentNullException(nameof(blueprint));
            }

            Mesh combinedTreeMesh = CreateCombinedTreeSpaceMesh(blueprint);
            ImpostorBakeSettings activeSettings = settings ?? blueprint.ImposterSettings;
            int sourceTriangleCount = GeneratedMeshSimplificationUtility.GetTriangleCount(combinedTreeMesh);
            int treeL3TriangleBudget = Mathf.Min(
                Mathf.Max(ImpostorTriangleBudget + 1, Mathf.Max(MinimumTreeL3TriangleBudget, sourceTriangleCount / 6)),
                Mathf.Max(1, sourceTriangleCount - 1));

            GeneratedMeshSimplificationUtility.GeneratedMeshCandidate treeL3Candidate =
                GeneratedMeshSimplificationUtility.SelectBestVoxelMeshCandidate(
                    combinedTreeMesh,
                    Mathf.Max(2, activeSettings.VoxelResolution),
                    2,
                    ImpostorResolutionFallbackStep,
                    treeL3TriangleBudget,
                    activeSettings.SkipReduction,
                    activeSettings.SkipSimplifyFallback,
                    $"{blueprint.name}_TreeL3Surface",
                    blueprint.TreeBounds);
            if (treeL3Candidate.Mesh.triangles.Length == 0)
            {
                GeneratedMeshSimplificationUtility.DestroyTemporaryMesh(treeL3Candidate.Mesh);
                UnityEngine.Object.DestroyImmediate(combinedTreeMesh);
                throw new InvalidOperationException($"{blueprint.name} did not produce a valid treeL3 surface mesh.");
            }

            if (treeL3Candidate.TriangleCount >= sourceTriangleCount)
            {
                string fallbackState = activeSettings.SkipSimplifyFallback ? "skipped" : "exhausted";
                Debug.LogError(
                    $"{blueprint.name} treeL3Mesh triangle count {treeL3Candidate.TriangleCount} must stay below source tree triangle count {sourceTriangleCount}. Final source: {treeL3Candidate.SourceDescription}. Simplify fallback {fallbackState}.");
            }

            UnityEngine.Object.DestroyImmediate(combinedTreeMesh);

            SerializedObject serializedBlueprint = new SerializedObject(blueprint);
            serializedBlueprint.FindProperty("treeL3Mesh").objectReferenceValue =
                GeneratedMeshAssetUtility.PersistGeneratedMesh(
                    blueprint,
                    $"{blueprint.name}_TreeL3",
                    treeL3Candidate.Mesh,
                    blueprint.GeneratedImpostorMeshesRelativeFolder);
            serializedBlueprint.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(blueprint);

            GeneratedMeshSimplificationUtility.DestroyTemporaryMesh(treeL3Candidate.Mesh);
        }

        /// <summary>
        /// [INTEGRATION] Called from editor tooling to populate the near-shadow L0 whole-tree proxy mesh on one tree blueprint.
        /// </summary>
        public static void BakeShadowProxyMeshL0(TreeBlueprintSO blueprint, ShadowProxyBakeSettings? settings = null)
        {
            BakeShadowProxyMesh(
                blueprint,
                settings,
                "shadowProxyMeshL0",
                "ShadowProxyL0",
                settings?.VoxelResolutionL0 ?? blueprint.ShadowProxySettings.VoxelResolutionL0,
                blueprint.ShadowProxyMeshL1);
        }

        /// <summary>
        /// [INTEGRATION] Called from editor tooling to populate the near-shadow L1 whole-tree proxy mesh on one tree blueprint.
        /// </summary>
        public static void BakeShadowProxyMeshL1(TreeBlueprintSO blueprint, ShadowProxyBakeSettings? settings = null)
        {
            BakeShadowProxyMesh(
                blueprint,
                settings,
                "shadowProxyMeshL1",
                "ShadowProxyL1",
                settings?.VoxelResolutionL1 ?? blueprint.ShadowProxySettings.VoxelResolutionL1,
                blueprint.TreeL3Mesh);
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

        private static void BakeShadowProxyMesh(
            TreeBlueprintSO blueprint,
            ShadowProxyBakeSettings? settings,
            string serializedPropertyName,
            string meshSuffix,
            int voxelResolution,
            Mesh? lowerDetailReferenceMesh)
        {
            // Range: requires a readable trunk mesh plus readable source branch meshes on every placed branch prototype. Condition: merged source geometry stays in tree local space and simplifies into a tree-level shadow proxy that remains lighter than the assembled source tree. Output: the target shadow proxy mesh field is assigned on the blueprint asset.
            if (blueprint == null)
            {
                throw new ArgumentNullException(nameof(blueprint));
            }

            Mesh treeL3Mesh = blueprint.TreeL3Mesh ?? throw new InvalidOperationException($"{blueprint.name} is missing treeL3Mesh.");
            if (!treeL3Mesh.isReadable)
            {
                throw new InvalidOperationException($"{blueprint.name} treeL3Mesh must be readable before shadow proxy baking.");
            }

            Mesh combinedTreeMesh = CreateCombinedTreeSpaceMesh(blueprint);
            ShadowProxyBakeSettings activeSettings = settings ?? blueprint.ShadowProxySettings;
            int sourceTriangleCount = GeneratedMeshSimplificationUtility.GetTriangleCount(combinedTreeMesh);
            int lowerDetailTriangleCount = GeneratedMeshSimplificationUtility.GetTriangleCount(lowerDetailReferenceMesh ?? treeL3Mesh);
            int triangleBudget = Mathf.Max(1, sourceTriangleCount - 1);

            GeneratedMeshSimplificationUtility.GeneratedMeshCandidate shadowProxyCandidate =
                GeneratedMeshSimplificationUtility.SelectBestVoxelMeshCandidate(
                    combinedTreeMesh,
                    Mathf.Max(2, voxelResolution),
                    2,
                    ImpostorResolutionFallbackStep,
                    triangleBudget,
                    activeSettings.SkipReduction,
                    activeSettings.SkipSimplifyFallback,
                    $"{blueprint.name}_{meshSuffix}Surface",
                    blueprint.TreeBounds);
            if (shadowProxyCandidate.Mesh.triangles.Length == 0)
            {
                GeneratedMeshSimplificationUtility.DestroyTemporaryMesh(shadowProxyCandidate.Mesh);
                UnityEngine.Object.DestroyImmediate(combinedTreeMesh);
                throw new InvalidOperationException($"{blueprint.name} did not produce a valid {meshSuffix} surface mesh.");
            }

            if (shadowProxyCandidate.TriangleCount >= sourceTriangleCount)
            {
                string fallbackState = activeSettings.SkipSimplifyFallback ? "skipped" : "exhausted";
                Debug.LogError(
                    $"{blueprint.name} {serializedPropertyName} triangle count {shadowProxyCandidate.TriangleCount} must stay below source tree triangle count {sourceTriangleCount}. Final source: {shadowProxyCandidate.SourceDescription}. Simplify fallback {fallbackState}.");
            }

            if (shadowProxyCandidate.TriangleCount <= lowerDetailTriangleCount)
            {
                Debug.LogError(
                    $"{blueprint.name} {serializedPropertyName} triangle count {shadowProxyCandidate.TriangleCount} should stay above the lower-detail reference triangle count {lowerDetailTriangleCount}.");
            }

            UnityEngine.Object.DestroyImmediate(combinedTreeMesh);

            SerializedObject serializedBlueprint = new SerializedObject(blueprint);
            serializedBlueprint.FindProperty(serializedPropertyName).objectReferenceValue =
                GeneratedMeshAssetUtility.PersistGeneratedMesh(
                    blueprint,
                    $"{blueprint.name}_{meshSuffix}",
                    shadowProxyCandidate.Mesh,
                    blueprint.GeneratedImpostorMeshesRelativeFolder);
            serializedBlueprint.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(blueprint);

            GeneratedMeshSimplificationUtility.DestroyTemporaryMesh(shadowProxyCandidate.Mesh);
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
