#nullable enable

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Editor-only preview builder for vegetation representation tiers.
    /// </summary>
    public static class VegetationEditorPreview
    {
        private const string PreviewRootPrefix = "__VegetationPreview_";
        private const HideFlags PreviewHideFlags = HideFlags.DontSave | HideFlags.NotEditable;
        private const string PreviewTierStateKeyPrefix = "VoxGeoFol.Vegetation.PreviewTier.";

        /// <summary>
        /// [INTEGRATION] Rebuilds the transient branch-root preview hierarchy for the selected representation tier.
        /// </summary>
        public static void ShowPreview(VegetationTreeAuthoring authoring, VegetationPreviewTier previewTier)
        {
            // Range: requires a valid blueprint and branch root on the selected authoring component. Condition: preview children are transient and rebuilt from current authoring assets only. Output: the branch root shows exactly the requested preview tier.
            if (authoring == null)
            {
                throw new ArgumentNullException(nameof(authoring));
            }

            if (Application.isPlaying)
            {
                throw new InvalidOperationException("Vegetation editor preview is edit-mode only.");
            }

            TreeBlueprintSO blueprint = VegetationTreeAuthoringEditorUtility.GetRequiredBlueprint(authoring);
            BranchPlacement[] placements = VegetationTreeAuthoringEditorUtility.GetRequiredPlacements(blueprint);
            Transform branchRootTransform = VegetationTreeAuthoringEditorUtility.GetRequiredBranchRoot(authoring).transform;

            ClearPreview(authoring);
            SetStoredPreviewTier(authoring, previewTier);

            GameObject previewRoot = CreatePreviewGameObject($"{PreviewRootPrefix}{previewTier}", branchRootTransform);

            switch (previewTier)
            {
                case VegetationPreviewTier.L0:
                    CreateTrunkPreview(previewRoot.transform, blueprint);
                    CreateOriginalBranchPreview(previewRoot.transform, placements);
                    break;
                case VegetationPreviewTier.L1:
                    CreateTrunkPreview(previewRoot.transform, blueprint);
                    CreateSplitBranchPreview(previewRoot.transform, placements, VegetationPreviewTier.L1);
                    break;
                case VegetationPreviewTier.L2:
                    CreateTrunkL3Preview(previewRoot.transform, blueprint);
                    CreateSplitBranchPreview(previewRoot.transform, placements, VegetationPreviewTier.L2);
                    break;
                case VegetationPreviewTier.L3:
                    CreateTrunkL3Preview(previewRoot.transform, blueprint);
                    CreateSplitBranchPreview(previewRoot.transform, placements, VegetationPreviewTier.L3);
                    break;
                case VegetationPreviewTier.TreeL3:
                    CreateTreeL3Preview(previewRoot.transform, blueprint);
                    break;
                case VegetationPreviewTier.Impostor:
                    CreateImpostorPreview(previewRoot.transform, blueprint);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(previewTier), previewTier, "Unsupported vegetation preview tier.");
            }

            EditorUtility.SetDirty(authoring);
            EditorUtility.SetDirty(branchRootTransform.gameObject);
            SceneView.RepaintAll();
        }

        /// <summary>
        /// [INTEGRATION] Clears all transient preview children from the configured branch root.
        /// </summary>
        public static void ClearPreview(VegetationTreeAuthoring authoring)
        {
            // Range: requires an assigned branch root. Condition: clears the branch-root contents used by the editor preview workflow before another preview is shown. Output: branch root has no preview children left.
            if (authoring == null)
            {
                throw new ArgumentNullException(nameof(authoring));
            }

            if (Application.isPlaying)
            {
                return;
            }

            Transform branchRootTransform = VegetationTreeAuthoringEditorUtility.GetRequiredBranchRoot(authoring).transform;
            for (int i = branchRootTransform.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(branchRootTransform.GetChild(i).gameObject);
            }

            EditorUtility.SetDirty(authoring);
            EditorUtility.SetDirty(branchRootTransform.gameObject);
            SceneView.RepaintAll();
        }

        public static bool TryGetActivePreviewTier(VegetationTreeAuthoring authoring, out VegetationPreviewTier previewTier)
        {
            previewTier = GetStoredPreviewTier(authoring);
            if (authoring == null || authoring.BranchRoot == null)
            {
                return false;
            }

            Transform branchRootTransform = authoring.BranchRoot.transform;
            if (branchRootTransform.childCount != 1)
            {
                return false;
            }

            string previewRootName = branchRootTransform.GetChild(0).name;
            if (!previewRootName.StartsWith(PreviewRootPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string rawTierName = previewRootName.Substring(PreviewRootPrefix.Length);
            return Enum.TryParse(rawTierName, out previewTier);
        }

        public static VegetationPreviewTier GetStoredPreviewTier(VegetationTreeAuthoring authoring)
        {
            if (authoring == null)
            {
                throw new ArgumentNullException(nameof(authoring));
            }

            int storedValue = SessionState.GetInt(GetPreviewTierStateKey(authoring), (int)VegetationPreviewTier.L0);
            return Enum.IsDefined(typeof(VegetationPreviewTier), storedValue)
                ? (VegetationPreviewTier)storedValue
                : VegetationPreviewTier.L0;
        }

        public static void SetStoredPreviewTier(VegetationTreeAuthoring authoring, VegetationPreviewTier previewTier)
        {
            if (authoring == null)
            {
                throw new ArgumentNullException(nameof(authoring));
            }

            SessionState.SetInt(GetPreviewTierStateKey(authoring), (int)previewTier);
        }

        private static void CreateOriginalBranchPreview(Transform parent, BranchPlacement[] placements)
        {
            for (int i = 0; i < placements.Length; i++)
            {
                BranchPlacement placement = placements[i] ?? throw new InvalidOperationException($"branches[{i}] is missing.");
                BranchPrototypeSO prototype = placement.Prototype ??
                                              throw new InvalidOperationException($"branches[{i}] is missing prototype.");
                Mesh woodMesh = prototype.WoodMesh ?? throw new InvalidOperationException($"{prototype.name} is missing woodMesh.");
                Material woodMaterial = prototype.WoodMaterial ??
                                        throw new InvalidOperationException($"{prototype.name} is missing woodMaterial.");
                Mesh foliageMesh = prototype.FoliageMesh ?? throw new InvalidOperationException($"{prototype.name} is missing foliageMesh.");
                Material foliageMaterial = prototype.FoliageMaterial ??
                                           throw new InvalidOperationException($"{prototype.name} is missing foliageMaterial.");

                GameObject branchObject = CreatePreviewGameObject($"{prototype.name}_Branch_{i:D2}", parent);
                ApplyPlacement(branchObject.transform, placement);
                CreateMeshChild(branchObject.transform, "Wood", woodMesh, woodMaterial);
                CreateMeshChild(branchObject.transform, "Foliage", foliageMesh, foliageMaterial);
            }
        }

        private static void CreateSplitBranchPreview(
            Transform parent,
            BranchPlacement[] placements,
            VegetationPreviewTier previewTier)
        {
            for (int i = 0; i < placements.Length; i++)
            {
                BranchPlacement placement = placements[i] ?? throw new InvalidOperationException($"branches[{i}] is missing.");
                BranchPrototypeSO prototype = placement.Prototype ??
                                              throw new InvalidOperationException($"branches[{i}] is missing prototype.");
                Material woodMaterial = prototype.WoodMaterial ??
                                        throw new InvalidOperationException($"{prototype.name} is missing woodMaterial.");
                Material shellMaterial = prototype.ShellMaterial ??
                                         throw new InvalidOperationException($"{prototype.name} is missing shellMaterial.");

                GameObject branchObject = CreatePreviewGameObject($"{prototype.name}_{previewTier}_{i:D2}", parent);
                ApplyPlacement(branchObject.transform, placement);
                CreateMeshChild(branchObject.transform, "Wood", GetRequiredSplitWoodMesh(prototype, previewTier), woodMaterial);
                CreateMeshChild(branchObject.transform, "Canopy", GetRequiredSplitCanopyMesh(prototype, previewTier), shellMaterial);
            }
        }

        private static void CreateTrunkPreview(Transform parent, TreeBlueprintSO blueprint)
        {
            Mesh trunkMesh = blueprint.TrunkMesh ?? throw new InvalidOperationException($"{blueprint.name} is missing trunkMesh.");
            Material trunkMaterial = blueprint.TrunkMaterial ??
                                     throw new InvalidOperationException($"{blueprint.name} is missing trunkMaterial.");
            CreateMeshChild(parent, "Trunk", trunkMesh, trunkMaterial);
        }

        private static void CreateTrunkL3Preview(Transform parent, TreeBlueprintSO blueprint)
        {
            Mesh trunkL3Mesh = blueprint.TrunkL3Mesh ?? throw new InvalidOperationException($"{blueprint.name} is missing trunkL3Mesh.");
            Material trunkMaterial = blueprint.TrunkMaterial ??
                                     throw new InvalidOperationException($"{blueprint.name} is missing trunkMaterial.");
            CreateMeshChild(parent, "TrunkL3", trunkL3Mesh, trunkMaterial);
        }

        private static void CreateImpostorPreview(Transform parent, TreeBlueprintSO blueprint)
        {
            Mesh impostorMesh = blueprint.ImpostorMesh ?? throw new InvalidOperationException($"{blueprint.name} is missing impostorMesh.");
            Material impostorMaterial = blueprint.ImpostorMaterial ??
                                        throw new InvalidOperationException($"{blueprint.name} is missing impostorMaterial.");
            CreateMeshChild(parent, "Impostor", impostorMesh, impostorMaterial);
        }

        private static void CreateTreeL3Preview(Transform parent, TreeBlueprintSO blueprint)
        {
            Mesh treeL3Mesh = blueprint.TreeL3Mesh ?? throw new InvalidOperationException($"{blueprint.name} is missing treeL3Mesh.");
            Material treeL3Material = blueprint.ImpostorMaterial ??
                                      throw new InvalidOperationException($"{blueprint.name} is missing impostorMaterial.");
            CreateMeshChild(parent, "TreeL3", treeL3Mesh, treeL3Material);
        }

        private static Mesh GetRequiredSplitCanopyMesh(BranchPrototypeSO prototype, VegetationPreviewTier previewTier)
        {
            return previewTier switch
            {
                VegetationPreviewTier.L1 => prototype.BranchL1CanopyMesh ?? throw new InvalidOperationException($"{prototype.name} is missing branchL1CanopyMesh."),
                VegetationPreviewTier.L2 => prototype.BranchL2CanopyMesh ?? throw new InvalidOperationException($"{prototype.name} is missing branchL2CanopyMesh."),
                VegetationPreviewTier.L3 => prototype.BranchL3CanopyMesh ?? throw new InvalidOperationException($"{prototype.name} is missing branchL3CanopyMesh."),
                _ => throw new ArgumentOutOfRangeException(nameof(previewTier), previewTier, "Preview tier must be an expanded branch split tier.")
            };
        }

        private static Mesh GetRequiredSplitWoodMesh(BranchPrototypeSO prototype, VegetationPreviewTier previewTier)
        {
            return previewTier switch
            {
                VegetationPreviewTier.L1 => prototype.BranchL1WoodMesh ?? throw new InvalidOperationException($"{prototype.name} is missing branchL1WoodMesh."),
                VegetationPreviewTier.L2 => prototype.BranchL2WoodMesh ?? throw new InvalidOperationException($"{prototype.name} is missing branchL2WoodMesh."),
                VegetationPreviewTier.L3 => prototype.BranchL3WoodMesh ?? throw new InvalidOperationException($"{prototype.name} is missing branchL3WoodMesh."),
                _ => throw new ArgumentOutOfRangeException(nameof(previewTier), previewTier, "Preview tier must be an expanded branch split tier.")
            };
        }

        private static void ApplyPlacement(Transform targetTransform, BranchPlacement placement)
        {
            targetTransform.localPosition = placement.LocalPosition;
            targetTransform.localRotation = placement.LocalRotation;
            targetTransform.localScale = Vector3.one * placement.Scale;
        }

        private static void CreateMeshChild(Transform parent, string childName, Mesh mesh, Material material)
        {
            GameObject childObject = CreatePreviewGameObject(childName, parent);
            MeshFilter meshFilter = childObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;
            meshFilter.hideFlags = PreviewHideFlags;

            MeshRenderer meshRenderer = childObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.hideFlags = PreviewHideFlags;
        }

        private static GameObject CreatePreviewGameObject(string childName, Transform parent)
        {
            GameObject childObject = new GameObject(childName)
            {
                hideFlags = PreviewHideFlags,
                layer = parent.gameObject.layer
            };

            childObject.transform.SetParent(parent, false);
            childObject.transform.hideFlags = PreviewHideFlags;
            return childObject;
        }

        private static string GetPreviewTierStateKey(VegetationTreeAuthoring authoring)
        {
            return $"{PreviewTierStateKeyPrefix}{authoring.GetInstanceID()}";
        }
    }
}
