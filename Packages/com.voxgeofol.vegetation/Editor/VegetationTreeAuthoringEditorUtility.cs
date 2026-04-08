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
            BakeCanopyShellsWithoutSave(blueprint);
            SaveAuthoringChanges(authoring, blueprint);
        }

        /// <summary>
        /// [INTEGRATION] Bakes the simplified runtime L3 trunk mesh for one tree blueprint referenced by the authoring component.
        /// </summary>
        public static void BakeTrunkL3(VegetationTreeAuthoring authoring)
        {
            // Range: requires a valid blueprint with a readable trunk mesh. Condition: trunk simplification clips every candidate back to the source trunk bounds before persistence. Output: the blueprint receives a refreshed trunkL3Mesh asset.
            if (authoring == null)
            {
                throw new ArgumentNullException(nameof(authoring));
            }

            TreeBlueprintSO blueprint = GetRequiredBlueprint(authoring);
            BakeTrunkL3WithoutSave(blueprint);
            SaveAuthoringChanges(authoring, blueprint);
        }

        /// <summary>
        /// [INTEGRATION] Bakes the far impostor mesh for one tree blueprint referenced by the authoring component.
        /// </summary>
        public static void BakeImpostor(VegetationTreeAuthoring authoring)
        {
            // Range: requires a valid blueprint with trunk mesh and readable source branch meshes on every referenced branch prototype. Condition: uses the editor-only coarse CPU voxel impostor bake pipeline. Output: the blueprint receives a refreshed impostor mesh asset.
            if (authoring == null)
            {
                throw new ArgumentNullException(nameof(authoring));
            }

            TreeBlueprintSO blueprint = GetRequiredBlueprint(authoring);
            BakeImpostorWithoutSave(blueprint);
            SaveAuthoringChanges(authoring, blueprint);
        }

        /// <summary>
        /// [INTEGRATION] Refreshes shells, trunkL3Mesh, and far impostor mesh in one editor operation.
        /// </summary>
        public static void BakeAllGeneratedMeshes(VegetationTreeAuthoring authoring)
        {
            // Range: requires the same authoring data as the individual shell, trunk, and impostor bake steps. Condition: all generated meshes are refreshed before one final asset save/refresh. Output: referenced prototypes and the blueprint are updated in one command.
            if (authoring == null)
            {
                throw new ArgumentNullException(nameof(authoring));
            }

            TreeBlueprintSO blueprint = GetRequiredBlueprint(authoring);
            BakeCanopyShellsWithoutSave(blueprint);
            BakeTrunkL3WithoutSave(blueprint);
            BakeImpostorWithoutSave(blueprint);
            SaveAuthoringChanges(authoring, blueprint);
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

            int l0Triangles = GetTriangleCount(blueprint.TrunkMesh);
            int l1Triangles = GetTriangleCount(blueprint.TrunkMesh);
            int l2Triangles = GetTriangleCount(blueprint.TrunkL3Mesh);
            int l3Triangles = GetTriangleCount(blueprint.TrunkL3Mesh);
            int impostorTriangles = GetTriangleCount(blueprint.ImpostorMesh);
            int shellL1OnlyTriangles = 0;
            int shellL2OnlyTriangles = 0;
            int shellL3OnlyTriangles = 0;

            for (int i = 0; i < placements.Length; i++)
            {
                BranchPrototypeSO prototype = placements[i].Prototype ??
                                              throw new InvalidOperationException($"branches[{i}] is missing prototype.");

                l0Triangles += GetTriangleCount(prototype.WoodMesh);
                l0Triangles += GetTriangleCount(prototype.FoliageMesh);

                l1Triangles += GetTriangleCount(prototype.WoodMesh);
                l1Triangles += BranchShellNodeUtility.GetTriangleCountForLeafFrontier(prototype.ShellNodesL0, 0);

                l2Triangles += GetTriangleCount(prototype.ShellL1WoodMesh);
                l2Triangles += BranchShellNodeUtility.GetTriangleCountForLeafFrontier(prototype.ShellNodesL1, 1);

                l3Triangles += GetTriangleCount(prototype.ShellL2WoodMesh);
                l3Triangles += BranchShellNodeUtility.GetTriangleCountForLeafFrontier(prototype.ShellNodesL2, 2);

                shellL1OnlyTriangles += BranchShellNodeUtility.GetTriangleCountForLeafFrontier(prototype.ShellNodesL0, 0);

                shellL2OnlyTriangles += BranchShellNodeUtility.GetTriangleCountForLeafFrontier(prototype.ShellNodesL1, 1);

                shellL3OnlyTriangles += BranchShellNodeUtility.GetTriangleCountForLeafFrontier(prototype.ShellNodesL2, 2);
            }

            return new VegetationAuthoringSummary(
                placements.Length,
                blueprint.TreeBounds,
                l0Triangles,
                l1Triangles,
                l2Triangles,
                l3Triangles,
                impostorTriangles,
                shellL1OnlyTriangles,
                shellL2OnlyTriangles,
                shellL3OnlyTriangles);
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

        private static void BakeCanopyShellsWithoutSave(TreeBlueprintSO blueprint)
        {
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
        }

        private static void BakeTrunkL3WithoutSave(TreeBlueprintSO blueprint)
        {
            TrunkL3MeshGenerator.BakeTrunkL3Mesh(blueprint, blueprint.ImposterSettings);
        }

        private static void BakeImpostorWithoutSave(TreeBlueprintSO blueprint)
        {
            GetRequiredPlacements(blueprint);
            ImpostorMeshGenerator.BakeImpostorMesh(blueprint, blueprint.ImposterSettings);
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
