#nullable enable

using System;
using UnityEditor;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Editor-only trunk simplifier for assembled tree blueprints.
    /// </summary>
    public static class TrunkL3MeshGenerator
    {
        private const int TrunkResolutionFallbackStep = 2;

        /// <summary>
        /// [INTEGRATION] Called from editor tooling to populate the simplified L3 trunk mesh on one tree blueprint.
        /// </summary>
        public static void BakeTrunkL3Mesh(TreeBlueprintSO blueprint, ImpostorBakeSettings? settings = null)
        {
            // Range: requires a readable source trunk mesh. Condition: every simplification candidate is clipped back to the original trunkMesh bounds so lower voxel resolutions never expand beyond the source envelope. Output: trunkL3Mesh is assigned on the blueprint asset.
            if (blueprint == null)
            {
                throw new ArgumentNullException(nameof(blueprint));
            }

            Mesh trunkMesh = blueprint.TrunkMesh ?? throw new InvalidOperationException($"{blueprint.name} is missing trunkMesh.");
            if (!trunkMesh.isReadable)
            {
                throw new InvalidOperationException($"{blueprint.name} trunkMesh must be readable before trunkL3Mesh baking.");
            }

            int sourceTriangleCount = GeneratedMeshSimplificationUtility.GetTriangleCount(trunkMesh);
            if (sourceTriangleCount <= 0)
            {
                throw new InvalidOperationException($"{blueprint.name} trunkMesh must contain triangles before trunkL3Mesh baking.");
            }

            ImpostorBakeSettings activeSettings = settings ?? blueprint.ImposterSettings;
            GeneratedMeshSimplificationUtility.GeneratedMeshCandidate trunkCandidate =
                GeneratedMeshSimplificationUtility.SelectBestVoxelMeshCandidate(
                    trunkMesh,
                    Mathf.Max(2, activeSettings.VoxelResolution),
                    2,
                    TrunkResolutionFallbackStep,
                    Mathf.Max(0, sourceTriangleCount - 1),
                    activeSettings.SkipReduction,
                    activeSettings.SkipSimplifyFallback,
                    $"{blueprint.name}_TrunkL3Surface",
                    trunkMesh.bounds);
            if (trunkCandidate.Mesh.triangles.Length == 0)
            {
                GeneratedMeshSimplificationUtility.DestroyTemporaryMesh(trunkCandidate.Mesh);
                throw new InvalidOperationException($"{blueprint.name} did not produce a valid trunkL3 surface mesh.");
            }

            if (trunkCandidate.TriangleCount >= sourceTriangleCount)
            {
                string fallbackState = activeSettings.SkipSimplifyFallback ? "skipped" : "exhausted";
                Debug.LogError(
                    $"{blueprint.name} trunkL3Mesh triangle count {trunkCandidate.TriangleCount} must stay below source trunk triangle count {sourceTriangleCount}. Final source: {trunkCandidate.SourceDescription}. Simplify fallback {fallbackState}.");
            }

            SerializedObject serializedBlueprint = new SerializedObject(blueprint);
            serializedBlueprint.FindProperty("trunkL3Mesh").objectReferenceValue =
                GeneratedMeshAssetUtility.PersistGeneratedMesh(
                    blueprint,
                    $"{blueprint.name}_TrunkL3",
                    trunkCandidate.Mesh,
                    blueprint.GeneratedImpostorMeshesRelativeFolder);
            serializedBlueprint.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(blueprint);

            GeneratedMeshSimplificationUtility.DestroyTemporaryMesh(trunkCandidate.Mesh);
        }
    }
}
