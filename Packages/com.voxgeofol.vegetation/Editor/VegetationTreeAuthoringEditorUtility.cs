#nullable enable

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;
using VoxGeoFol.Features.Vegetation.Rendering;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Editor-only authoring operations for vegetation tree scene bindings.
    /// </summary>
    public static class VegetationTreeAuthoringEditorUtility
    {
        private const string RegisteredAuthoringsPropertyName = "registeredAuthorings";

        /// <summary>
        /// [INTEGRATION] Bakes the single-mesh canopy L1/L2/L3 chain for every unique branch prototype referenced by one authoring component.
        /// </summary>
        public static void BakeCanopyShells(VegetationTreeAuthoring authoring)
        {
            // Range: requires a valid blueprint with readable source branch meshes. Condition: each unique prototype is baked once through the editor-only canopy simplification pipeline. Output: referenced prototypes receive refreshed canopy and simplified wood tier meshes.
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
        /// [INTEGRATION] Bakes the mandatory whole-tree TreeL3 floor mesh for one tree blueprint referenced by the authoring component.
        /// </summary>
        public static void BakeTreeL3(VegetationTreeAuthoring authoring)
        {
            // Range: requires a valid blueprint with readable trunk and source branch meshes. Condition: tree-local source geometry is merged once and simplified into the mandatory TreeL3 floor mesh. Output: the blueprint receives a refreshed treeL3Mesh asset.
            if (authoring == null)
            {
                throw new ArgumentNullException(nameof(authoring));
            }

            TreeBlueprintSO blueprint = GetRequiredBlueprint(authoring);
            BakeTreeL3WithoutSave(blueprint);
            SaveAuthoringChanges(authoring, blueprint);
        }

        /// <summary>
        /// [INTEGRATION] Bakes the near-shadow L0 whole-tree proxy mesh for one tree blueprint referenced by the authoring component.
        /// </summary>
        public static void BakeShadowProxyL0(VegetationTreeAuthoring authoring)
        {
            // Range: requires a valid blueprint with readable TreeL3 and source branch meshes. Condition: whole-tree source geometry is merged once and simplified into the near-shadow L0 proxy. Output: the blueprint receives a refreshed shadowProxyMeshL0 asset.
            if (authoring == null)
            {
                throw new ArgumentNullException(nameof(authoring));
            }

            TreeBlueprintSO blueprint = GetRequiredBlueprint(authoring);
            BakeShadowProxyL0WithoutSave(blueprint);
            SaveAuthoringChanges(authoring, blueprint);
        }

        /// <summary>
        /// [INTEGRATION] Bakes the near-shadow L1 whole-tree proxy mesh for one tree blueprint referenced by the authoring component.
        /// </summary>
        public static void BakeShadowProxyL1(VegetationTreeAuthoring authoring)
        {
            // Range: requires a valid blueprint with readable TreeL3 and source branch meshes. Condition: whole-tree source geometry is merged once and simplified into the near-shadow L1 proxy. Output: the blueprint receives a refreshed shadowProxyMeshL1 asset.
            if (authoring == null)
            {
                throw new ArgumentNullException(nameof(authoring));
            }

            TreeBlueprintSO blueprint = GetRequiredBlueprint(authoring);
            BakeShadowProxyL1WithoutSave(blueprint);
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
        /// [INTEGRATION] Refreshes shells, trunkL3Mesh, treeL3Mesh, near-shadow proxy meshes, and far impostor mesh in one editor operation.
        /// </summary>
        public static void BakeAllGeneratedMeshes(VegetationTreeAuthoring authoring)
        {
            // Range: requires the same authoring data as the individual shell, trunk, TreeL3, shadow proxy, and impostor bake steps. Condition: all generated meshes are refreshed before one final asset save/refresh. Output: referenced prototypes and the blueprint are updated in one command.
            if (authoring == null)
            {
                throw new ArgumentNullException(nameof(authoring));
            }

            TreeBlueprintSO blueprint = GetRequiredBlueprint(authoring);
            BakeCanopyShellsWithoutSave(blueprint);
            BakeTrunkL3WithoutSave(blueprint);
            BakeTreeL3WithoutSave(blueprint);
            BakeShadowProxyL1WithoutSave(blueprint);
            BakeShadowProxyL0WithoutSave(blueprint);
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
            int treeL3Triangles = GetTriangleCount(blueprint.TreeL3Mesh);
            int impostorTriangles = GetTriangleCount(blueprint.ImpostorMesh);
            for (int i = 0; i < placements.Length; i++)
            {
                BranchPrototypeSO prototype = placements[i].Prototype ??
                                              throw new InvalidOperationException($"branches[{i}] is missing prototype.");

                l0Triangles += GetTriangleCount(prototype.WoodMesh);
                l0Triangles += GetTriangleCount(prototype.FoliageMesh);

                l1Triangles += GetTriangleCount(prototype.BranchL1WoodMesh);
                l1Triangles += GetTriangleCount(prototype.BranchL1CanopyMesh);

                l2Triangles += GetTriangleCount(prototype.BranchL2WoodMesh);
                l2Triangles += GetTriangleCount(prototype.BranchL2CanopyMesh);

                l3Triangles += GetTriangleCount(prototype.BranchL3WoodMesh);
                l3Triangles += GetTriangleCount(prototype.BranchL3CanopyMesh);
            }

            return new VegetationAuthoringSummary(
                placements.Length,
                blueprint.TreeBounds,
                l0Triangles,
                l1Triangles,
                l2Triangles,
                l3Triangles,
                treeL3Triangles,
                impostorTriangles);
        }

        /// <summary>
        /// [INTEGRATION] Rebuilds one runtime container's serialized authorings list from its hierarchy while excluding descendants owned by nested child containers.
        /// </summary>
        public static void FillRuntimeContainerAuthorings(VegetationRuntimeContainer container)
        {
            // Range: requires one runtime container in a loaded scene hierarchy. Condition: editor-only hierarchy discovery populates the serialized list and preserves nested-container ownership boundaries. Output: the container stores explicit authoring references and refreshes its runtime registration snapshot.
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            List<VegetationTreeAuthoring> discoveredAuthorings = new List<VegetationTreeAuthoring>();
            CollectOwnedRuntimeContainerAuthorings(container, discoveredAuthorings);

            SerializedObject serializedObject = new SerializedObject(container);
            SerializedProperty registeredAuthoringsProperty = serializedObject.FindProperty(RegisteredAuthoringsPropertyName)
                ?? throw new InvalidOperationException(
                    $"Property '{RegisteredAuthoringsPropertyName}' was not found on {nameof(VegetationRuntimeContainer)}.");

            Undo.RecordObject(container, "Fill Vegetation Runtime Authorings");
            registeredAuthoringsProperty.arraySize = discoveredAuthorings.Count;
            for (int i = 0; i < discoveredAuthorings.Count; i++)
            {
                registeredAuthoringsProperty.GetArrayElementAtIndex(i).objectReferenceValue = discoveredAuthorings[i];
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(container);
            if (!container.isActiveAndEnabled)
            {
                container.ResetRuntimeState();
                return;
            }

            container.RefreshRuntimeRegistration();
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

        internal static void CollectOwnedRuntimeContainerAuthorings(
            VegetationRuntimeContainer container,
            List<VegetationTreeAuthoring> target)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            target.Clear();
            container.GetComponentsInChildren(true, target);
            int writeIndex = 0;
            for (int readIndex = 0; readIndex < target.Count; readIndex++)
            {
                VegetationTreeAuthoring authoring = target[readIndex];
                if (authoring == null)
                {
                    continue;
                }

                VegetationRuntimeContainer? owningContainer = authoring.GetComponentInParent<VegetationRuntimeContainer>();
                if (owningContainer != container)
                {
                    continue;
                }

                target[writeIndex] = authoring;
                writeIndex++;
            }

            if (writeIndex < target.Count)
            {
                target.RemoveRange(writeIndex, target.Count - writeIndex);
            }
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

        private static void BakeTreeL3WithoutSave(TreeBlueprintSO blueprint)
        {
            ImpostorMeshGenerator.BakeTreeL3Mesh(blueprint, blueprint.ImposterSettings);
        }

        private static void BakeShadowProxyL0WithoutSave(TreeBlueprintSO blueprint)
        {
            ImpostorMeshGenerator.BakeShadowProxyMeshL0(blueprint, blueprint.ShadowProxySettings);
        }

        private static void BakeShadowProxyL1WithoutSave(TreeBlueprintSO blueprint)
        {
            ImpostorMeshGenerator.BakeShadowProxyMeshL1(blueprint, blueprint.ShadowProxySettings);
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
