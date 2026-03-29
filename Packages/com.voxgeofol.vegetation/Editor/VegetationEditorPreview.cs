#nullable enable

using System;
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
                case VegetationPreviewTier.R0Full:
                    CreateTrunkPreview(previewRoot.transform, blueprint);
                    CreateOriginalBranchPreview(previewRoot.transform, placements, includeShellL0: true);
                    break;
                case VegetationPreviewTier.R1ShellL1:
                    CreateTrunkPreview(previewRoot.transform, blueprint);
                    CreateShellBranchPreview(previewRoot.transform, placements, 1);
                    break;
                case VegetationPreviewTier.R2ShellL2:
                    CreateTrunkPreview(previewRoot.transform, blueprint);
                    CreateShellBranchPreview(previewRoot.transform, placements, 2);
                    break;
                case VegetationPreviewTier.R3Impostor:
                    CreateImpostorPreview(previewRoot.transform, blueprint);
                    break;
                case VegetationPreviewTier.ShellL0Only:
                    CreateShellBranchPreview(previewRoot.transform, placements, 0);
                    break;
                case VegetationPreviewTier.ShellL1Only:
                    CreateShellBranchPreview(previewRoot.transform, placements, 1);
                    break;
                case VegetationPreviewTier.ShellL2Only:
                    CreateShellBranchPreview(previewRoot.transform, placements, 2);
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

            int storedValue = SessionState.GetInt(GetPreviewTierStateKey(authoring), (int)VegetationPreviewTier.R0Full);
            return Enum.IsDefined(typeof(VegetationPreviewTier), storedValue)
                ? (VegetationPreviewTier)storedValue
                : VegetationPreviewTier.R0Full;
        }

        public static void SetStoredPreviewTier(VegetationTreeAuthoring authoring, VegetationPreviewTier previewTier)
        {
            if (authoring == null)
            {
                throw new ArgumentNullException(nameof(authoring));
            }

            SessionState.SetInt(GetPreviewTierStateKey(authoring), (int)previewTier);
        }

        private static void CreateOriginalBranchPreview(Transform parent, BranchPlacement[] placements, bool includeShellL0)
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

                if (includeShellL0)
                {
                    Mesh shellL0Mesh = prototype.ShellL0Mesh ?? throw new InvalidOperationException($"{prototype.name} is missing shellL0Mesh.");
                    Material shellMaterial = prototype.ShellMaterial ??
                                             throw new InvalidOperationException($"{prototype.name} is missing shellMaterial.");
                    CreateMeshChild(branchObject.transform, "ShellL0", shellL0Mesh, shellMaterial);
                }
            }
        }

        private static void CreateShellBranchPreview(Transform parent, BranchPlacement[] placements, int shellLevel)
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

                GameObject branchObject = CreatePreviewGameObject($"{prototype.name}_ShellL{shellLevel}_{i:D2}", parent);
                ApplyPlacement(branchObject.transform, placement);
                CreateMeshChild(
                    branchObject.transform,
                    $"WoodL{shellLevel}",
                    GetRequiredWoodMesh(prototype, shellLevel),
                    woodMaterial);
                CreateMeshChild(
                    branchObject.transform,
                    $"ShellL{shellLevel}",
                    GetRequiredShellMesh(prototype, shellLevel),
                    shellMaterial);
            }
        }

        private static void CreateTrunkPreview(Transform parent, TreeBlueprintSO blueprint)
        {
            Mesh trunkMesh = blueprint.TrunkMesh ?? throw new InvalidOperationException($"{blueprint.name} is missing trunkMesh.");
            Material trunkMaterial = blueprint.TrunkMaterial ??
                                     throw new InvalidOperationException($"{blueprint.name} is missing trunkMaterial.");
            CreateMeshChild(parent, "Trunk", trunkMesh, trunkMaterial);
        }

        private static void CreateImpostorPreview(Transform parent, TreeBlueprintSO blueprint)
        {
            Mesh impostorMesh = blueprint.ImpostorMesh ?? throw new InvalidOperationException($"{blueprint.name} is missing impostorMesh.");
            Material impostorMaterial = blueprint.ImpostorMaterial ??
                                        throw new InvalidOperationException($"{blueprint.name} is missing impostorMaterial.");
            CreateMeshChild(parent, "Impostor", impostorMesh, impostorMaterial);
        }

        private static Mesh GetRequiredShellMesh(BranchPrototypeSO prototype, int shellLevel)
        {
            return shellLevel switch
            {
                0 => prototype.ShellL0Mesh ?? throw new InvalidOperationException($"{prototype.name} is missing shellL0Mesh."),
                1 => prototype.ShellL1Mesh ?? throw new InvalidOperationException($"{prototype.name} is missing shellL1Mesh."),
                2 => prototype.ShellL2Mesh ?? throw new InvalidOperationException($"{prototype.name} is missing shellL2Mesh."),
                _ => throw new ArgumentOutOfRangeException(nameof(shellLevel), shellLevel, "Shell level must be 0, 1, or 2.")
            };
        }

        private static Mesh GetRequiredWoodMesh(BranchPrototypeSO prototype, int shellLevel)
        {
            return shellLevel switch
            {
                0 => prototype.WoodMesh ?? throw new InvalidOperationException($"{prototype.name} is missing woodMesh."),
                1 => prototype.ShellL1WoodMesh ?? throw new InvalidOperationException($"{prototype.name} is missing shellL1WoodMesh."),
                2 => prototype.ShellL2WoodMesh ?? throw new InvalidOperationException($"{prototype.name} is missing shellL2WoodMesh."),
                _ => throw new ArgumentOutOfRangeException(nameof(shellLevel), shellLevel, "Shell level must be 0, 1, or 2.")
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
