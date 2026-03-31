#nullable enable

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Editor-only authoring operations for vegetation tree scene bindings.
    /// </summary>
    public static class VegetationTreeAuthoringEditorUtility
    {
        /// <summary>
        /// [INTEGRATION] Bakes shell L0/L1/L2 for every unique branch prototype referenced by one authoring component.
        /// </summary>
        public static void BakeCanopyShells(VegetationTreeAuthoring authoring)
        {
            // Range: requires a valid blueprint with readable source branch meshes. Condition: each unique prototype is baked once through the editor-only shell pipeline. Output: referenced prototypes receive refreshed shell and simplified wood meshes.
            if (authoring == null)
            {
                throw new ArgumentNullException(nameof(authoring));
            }

            TreeBlueprintSO blueprint = GetRequiredBlueprint(authoring);
            BranchPlacement[] placements = GetRequiredPlacements(blueprint);
            HashSet<BranchPrototypeSO> bakedPrototypes = new HashSet<BranchPrototypeSO>();

            for (int i = 0; i < placements.Length; i++)
            {
                BranchPrototypeSO prototype = placements[i].Prototype ??
                                              throw new InvalidOperationException($"branches[{i}] is missing prototype.");
                if (bakedPrototypes.Add(prototype))
                {
                    CanopyShellGenerator.BakeCanopyShells(prototype);
                }
            }

            SaveAuthoringChanges(authoring, blueprint);
        }

        /// <summary>
        /// [INTEGRATION] Bakes the far impostor mesh for one tree blueprint referenced by the authoring component.
        /// </summary>
        public static void BakeImpostor(VegetationTreeAuthoring authoring)
        {
            // Range: requires a valid blueprint with trunk mesh and readable source branch meshes on every referenced branch prototype. Condition: uses the editor-only coarse MeshVoxelizer impostor bake pipeline. Output: the blueprint receives a refreshed impostor mesh asset.
            if (authoring == null)
            {
                throw new ArgumentNullException(nameof(authoring));
            }

            TreeBlueprintSO blueprint = GetRequiredBlueprint(authoring);
            GetRequiredPlacements(blueprint);
            ImpostorMeshGenerator.BakeImpostorMesh(blueprint, blueprint.ImposterSettings);
            SaveAuthoringChanges(authoring, blueprint);
        }

        /// <summary>
        /// [INTEGRATION] Refreshes shells first and then refreshes the far impostor mesh.
        /// </summary>
        public static void BakeCanopyShellsAndImpostor(VegetationTreeAuthoring authoring)
        {
            // Range: requires the same authoring data as the shell and impostor bake steps. Condition: shells are refreshed first for convenience, then the impostor is rebuilt directly from the original tree meshes. Output: referenced prototypes and the blueprint are updated in one command.
            BakeCanopyShells(authoring);
            BakeImpostor(authoring);
        }

        /// <summary>
        /// [INTEGRATION] Aggregates authoring validation for the scene binding, blueprint, and every unique branch prototype it references.
        /// </summary>
        public static VegetationValidationResult ValidateForEditor(VegetationTreeAuthoring authoring)
        {
            // Range: accepts one scene binding. Condition: merges the root authoring validation with every unique branch prototype validation. Output: editor-facing validation issues ready for inspector/window display.
            if (authoring == null)
            {
                throw new ArgumentNullException(nameof(authoring));
            }

            VegetationValidationResult result = VegetationAuthoringValidator.ValidateTreeAuthoring(authoring);
            TreeBlueprintSO? blueprint = authoring.Blueprint;
            if (blueprint == null)
            {
                return result;
            }

            BranchPlacement[] placements = blueprint.Branches;
            HashSet<BranchPrototypeSO> validatedPrototypes = new HashSet<BranchPrototypeSO>();
            for (int i = 0; i < placements.Length; i++)
            {
                BranchPrototypeSO? prototype = placements[i]?.Prototype;
                if (prototype != null && validatedPrototypes.Add(prototype))
                {
                    result.Merge(prototype.Validate());
                }
            }

            return result;
        }

        /// <summary>
        /// [INTEGRATION] Builds the custom editor summary for one vegetation authoring component.
        /// </summary>
        public static VegetationAuthoringSummary BuildSummary(VegetationTreeAuthoring authoring)
        {
            // Range: requires one authoring component with an assigned blueprint. Condition: triangle totals are computed from the currently referenced meshes only. Output: compact per-tier counts and the blueprint bounds for editor UI display.
            if (authoring == null)
            {
                throw new ArgumentNullException(nameof(authoring));
            }

            TreeBlueprintSO blueprint = GetRequiredBlueprint(authoring);
            BranchPlacement[] placements = blueprint.Branches;

            int r0Triangles = GetTriangleCount(blueprint.TrunkMesh);
            int r1Triangles = GetTriangleCount(blueprint.TrunkMesh);
            int r2Triangles = GetTriangleCount(blueprint.TrunkMesh);
            int r3Triangles = GetTriangleCount(blueprint.ImpostorMesh);
            int shellL0OnlyTriangles = 0;
            int shellL1OnlyTriangles = 0;
            int shellL2OnlyTriangles = 0;

            for (int i = 0; i < placements.Length; i++)
            {
                BranchPrototypeSO prototype = placements[i].Prototype ??
                                              throw new InvalidOperationException($"branches[{i}] is missing prototype.");

                r0Triangles += GetTriangleCount(prototype.WoodMesh);
                r0Triangles += GetTriangleCount(prototype.FoliageMesh);
                r0Triangles += BranchShellNodeUtility.GetTriangleCountForLeafFrontier(prototype.ShellNodes, 0);

                r1Triangles += GetTriangleCount(prototype.ShellL1WoodMesh);
                r1Triangles += BranchShellNodeUtility.GetTriangleCountForLeafFrontier(prototype.ShellNodes, 1);

                r2Triangles += GetTriangleCount(prototype.ShellL2WoodMesh);
                r2Triangles += BranchShellNodeUtility.GetTriangleCountForLeafFrontier(prototype.ShellNodes, 2);

                shellL0OnlyTriangles += GetTriangleCount(prototype.WoodMesh);
                shellL0OnlyTriangles += BranchShellNodeUtility.GetTriangleCountForLeafFrontier(prototype.ShellNodes, 0);

                shellL1OnlyTriangles += GetTriangleCount(prototype.ShellL1WoodMesh);
                shellL1OnlyTriangles += BranchShellNodeUtility.GetTriangleCountForLeafFrontier(prototype.ShellNodes, 1);

                shellL2OnlyTriangles += GetTriangleCount(prototype.ShellL2WoodMesh);
                shellL2OnlyTriangles += BranchShellNodeUtility.GetTriangleCountForLeafFrontier(prototype.ShellNodes, 2);
            }

            return new VegetationAuthoringSummary(
                placements.Length,
                blueprint.TreeBounds,
                r0Triangles,
                r1Triangles,
                r2Triangles,
                r3Triangles,
                shellL0OnlyTriangles,
                shellL1OnlyTriangles,
                shellL2OnlyTriangles);
        }

        internal static TreeBlueprintSO GetRequiredBlueprint(VegetationTreeAuthoring authoring)
        {
            return authoring.Blueprint ?? throw new InvalidOperationException($"{authoring.name} is missing blueprint.");
        }

        internal static BranchPlacement[] GetRequiredPlacements(TreeBlueprintSO blueprint)
        {
            BranchPlacement[] placements = blueprint.Branches;
            if (placements == null || placements.Length == 0)
            {
                throw new InvalidOperationException($"{blueprint.name} does not contain any branch placements.");
            }

            return placements;
        }

        internal static GameObject GetRequiredBranchRoot(VegetationTreeAuthoring authoring)
        {
            return authoring.BranchRoot ?? throw new InvalidOperationException($"{authoring.name} is missing _rootForBranches.");
        }

        private static void SaveAuthoringChanges(VegetationTreeAuthoring authoring, TreeBlueprintSO blueprint)
        {
            EditorUtility.SetDirty(authoring);
            EditorUtility.SetDirty(blueprint);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static int GetTriangleCount(Mesh? mesh)
        {
            return mesh == null ? 0 : checked((int)(mesh.GetIndexCount(0) / 3L));
        }
    }
}
