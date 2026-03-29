#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VoxGeoFol.Features.Vegetation.Authoring
{
    /// <summary>
    /// Scene binding that points to one immutable tree blueprint asset.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VegetationTreeAuthoring : MonoBehaviour
    {
        [SerializeField] private TreeBlueprintSO? blueprint;
        [SerializeField] private GameObject _rootForBranches = null!;
        [SerializeField] [HideInInspector] private int runtimeTreeIndex = -1;

        public TreeBlueprintSO? Blueprint => blueprint;

        public int RuntimeTreeIndex => runtimeTreeIndex;

        /// <summary>
        /// [INTEGRATION] Assigns the runtime tree index produced by the runtime gather/orchestration stage.
        /// </summary>
        public void RefreshRuntimeTreeIndex(int treeIndex)
        {
            runtimeTreeIndex = treeIndex;
        }

        /// <summary>
        /// [INTEGRATION] Clears runtime-only indexing when runtime services rebuild or reset.
        /// </summary>
        public void ResetRuntimeTreeIndex()
        {
            runtimeTreeIndex = -1;
        }

        /// <summary>
        /// [INTEGRATION] Validates the scene binding and the referenced tree blueprint contract.
        /// </summary>
        public VegetationValidationResult Validate()
        {
            return VegetationAuthoringValidator.ValidateTreeAuthoring(this);
        }

        /// <summary>
        ///     Editor entry point that bakes shell L0/L1/L2 for every unique branch prototype referenced by the blueprint.
        /// </summary>
        [ContextMenu("BakeCanopyShells")]
        public void BakeCanopyShells()
        {
#if UNITY_EDITOR
            // Range: requires a valid blueprint with branch placements that reference readable foliage and wood meshes. Condition: each unique prototype is baked exactly once through the editor-only shell pipeline. Output: referenced prototypes receive shellL0Mesh/shellL1Mesh/shellL1WoodMesh/shellL2Mesh/shellL2WoodMesh and assets are saved.
            TreeBlueprintSO currentBlueprint = GetRequiredBlueprint();
            BranchPlacement[] placements = GetRequiredPlacements(currentBlueprint);
            HashSet<BranchPrototypeSO> bakedPrototypes = new HashSet<BranchPrototypeSO>();

            for (int i = 0; i < placements.Length; i++)
            {
                BranchPrototypeSO prototype = placements[i].Prototype ??
                                              throw new InvalidOperationException($"branches[{i}] is missing prototype.");
                if (bakedPrototypes.Add(prototype))
                {
                    InvokeEditorBakeMethod(
                        "VoxGeoFol.Features.Vegetation.Editor.CanopyShellGenerator",
                        "BakeCanopyShells",
                        prototype,
                        null);
                }
            }

            SaveEditorAuthoringChanges();
#else
            throw new InvalidOperationException("Canopy shell baking is editor-only.");
#endif
        }

        /// <summary>
        ///     Editor entry point that bakes the blueprint impostor from the trunk and baked shell L2 geometry.
        /// </summary>
        [ContextMenu("BakeImpostor")]
        public void BakeImpostor()
        {
#if UNITY_EDITOR
            // Range: requires a valid blueprint with trunk mesh and baked shellL2 meshes on all referenced prototypes. Condition: uses the editor-only impostor bake pipeline. Output: blueprint receives impostorMesh and assets are saved.
            TreeBlueprintSO currentBlueprint = GetRequiredBlueprint();
            GetRequiredPlacements(currentBlueprint);
            InvokeEditorBakeMethod(
                "VoxGeoFol.Features.Vegetation.Editor.ImpostorMeshGenerator",
                "BakeImpostorMesh",
                currentBlueprint,
                null);

            SaveEditorAuthoringChanges();
#else
            throw new InvalidOperationException("Impostor baking is editor-only.");
#endif
        }

        /// <summary>
        ///     Editor entry point that first bakes shells and then bakes the impostor.
        /// </summary>
        [ContextMenu("BakeCanopyShellsAndImpostor")]
        public void BakeCanopyShellsAndImpostor()
        {
#if UNITY_EDITOR
            // Range: requires the same authoring data as shell and impostor bake operations. Condition: shells are regenerated before the impostor so shellL2 is current. Output: referenced prototypes and the blueprint are updated in one command.
            BakeCanopyShells();
            BakeImpostor();
#else
            throw new InvalidOperationException("Vegetation baking is editor-only.");
#endif
        }

        /// <summary>
        ///     Editor preview for the saved data based on the original branch and leaves.
        /// </summary>
        [ContextMenu("ReconstructOriginalFromData")]
        public void ReconstructFromDataAndOriginalBranch()
        {
            RefreshOriginalBranchAssemblyFromBlueprint();
        }

        /// <summary>
        ///     Editor preview for the saved shell L0 data.
        /// </summary>
        [ContextMenu("ReconstructShellL0FromData")]
        public void ReconstructShellL0FromData()
        {
            RefreshShellAssemblyFromBlueprint(0);
        }

        /// <summary>
        ///     Editor preview for the saved shell L1 data.
        /// </summary>
        [ContextMenu("ReconstructShellL1FromData")]
        public void ReconstructShellL1FromData()
        {
            RefreshShellAssemblyFromBlueprint(1);
        }

        /// <summary>
        ///     Editor preview for the saved shell L2 data.
        /// </summary>
        [ContextMenu("ReconstructShellL2FromData")]
        public void ReconstructShellL2FromData()
        {
            RefreshShellAssemblyFromBlueprint(2);
        }
        
        /// <summary>
        ///     Editor preview for the saved data based on the original branch and leaves.
        /// </summary>
        [ContextMenu("DeleteOriginals")]
        public void DeleteOriginals()
        {
            DeleteBranchRootChildren();
            MarkAuthoringObjectsDirty();
        }

        private void RefreshOriginalBranchAssemblyFromBlueprint()
        {
            // Range: requires a valid blueprint and assigned branch root. Condition: rebuilds only the original branch hierarchy under _rootForBranches. Output: branch hierarchy matches blueprint branch placements using prototype source meshes/materials.
            TreeBlueprintSO currentBlueprint = GetRequiredBlueprint();
            BranchPlacement[] placements = GetRequiredPlacements(currentBlueprint);

            DeleteBranchRootChildren();

            Transform branchRootTransform = GetRequiredBranchRoot().transform;
            for (int i = 0; i < placements.Length; i++)
            {
                CreateOriginalBranchGameObject(branchRootTransform, placements[i], i);
            }

            MarkAuthoringObjectsDirty();
        }

        private void RefreshShellAssemblyFromBlueprint(int shellLevel)
        {
            // Range: requires a valid blueprint with baked shell meshes and branch wood materials. Condition: rebuilds only the selected shell representation tier under _rootForBranches. Output: L0 uses source wood plus shell L0, while L1/L2 use the baked simplified wood attachments plus the selected canopy shell mesh.
            TreeBlueprintSO currentBlueprint = GetRequiredBlueprint();
            BranchPlacement[] placements = GetRequiredPlacements(currentBlueprint);

            DeleteBranchRootChildren();

            Transform branchRootTransform = GetRequiredBranchRoot().transform;
            for (int i = 0; i < placements.Length; i++)
            {
                CreateShellBranchGameObject(branchRootTransform, placements[i], i, shellLevel);
            }

            MarkAuthoringObjectsDirty();
        }

        private void CreateOriginalBranchGameObject(Transform parent, BranchPlacement placement, int placementIndex)
        {
            BranchPrototypeSO prototype = placement.Prototype ??
                                          throw new InvalidOperationException($"branches[{placementIndex}] is missing prototype.");
            Mesh woodMesh = prototype.WoodMesh ?? throw new InvalidOperationException($"{prototype.name} is missing woodMesh.");
            Material woodMaterial = prototype.WoodMaterial ??
                                    throw new InvalidOperationException($"{prototype.name} is missing woodMaterial.");
            Mesh foliageMesh = prototype.FoliageMesh ?? throw new InvalidOperationException($"{prototype.name} is missing foliageMesh.");
            Material foliageMaterial = prototype.FoliageMaterial ??
                                       throw new InvalidOperationException($"{prototype.name} is missing foliageMaterial.");

            GameObject branchObject = CreateChildGameObject($"{prototype.name}_Branch_{placementIndex:D2}", parent);
            Transform branchTransform = branchObject.transform;
            branchTransform.localPosition = placement.LocalPosition;
            branchTransform.localRotation = placement.LocalRotation;
            branchTransform.localScale = Vector3.one * placement.Scale;

            CreateMeshChildGameObject(branchTransform, "Wood", woodMesh, woodMaterial);
            CreateMeshChildGameObject(branchTransform, "Foliage", foliageMesh, foliageMaterial);
        }

        private void CreateShellBranchGameObject(Transform parent, BranchPlacement placement, int placementIndex, int shellLevel)
        {
            BranchPrototypeSO prototype = placement.Prototype ??
                                          throw new InvalidOperationException($"branches[{placementIndex}] is missing prototype.");
            Material woodMaterial = prototype.WoodMaterial ??
                                    throw new InvalidOperationException($"{prototype.name} is missing woodMaterial.");
            Mesh shellMesh = GetRequiredShellMesh(prototype, shellLevel);
            Material shellMaterial = prototype.ShellMaterial ??
                                     throw new InvalidOperationException($"{prototype.name} is missing shellMaterial.");
            Mesh woodMesh = GetRequiredShellWoodMesh(prototype, shellLevel);

            GameObject branchObject = CreateChildGameObject($"{prototype.name}_ShellL{shellLevel}_{placementIndex:D2}", parent);
            Transform branchTransform = branchObject.transform;
            branchTransform.localPosition = placement.LocalPosition;
            branchTransform.localRotation = placement.LocalRotation;
            branchTransform.localScale = Vector3.one * placement.Scale;

            CreateMeshChildGameObject(branchTransform, $"WoodL{shellLevel}", woodMesh, woodMaterial);
            CreateMeshChildGameObject(branchTransform, $"ShellL{shellLevel}", shellMesh, shellMaterial);
        }

        private void CreateMeshChildGameObject(Transform parent, string childName, Mesh mesh, Material material)
        {
            GameObject childObject = CreateChildGameObject(childName, parent);
            MeshFilter meshFilter = childObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            MeshRenderer meshRenderer = childObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
        }

        private void DeleteBranchRootChildren()
        {
            Transform branchRootTransform = GetRequiredBranchRoot().transform;
            for (int i = branchRootTransform.childCount - 1; i >= 0; i--)
            {
                DestroyChildGameObject(branchRootTransform.GetChild(i).gameObject);
            }
        }

        private GameObject GetRequiredBranchRoot()
        {
            return _rootForBranches != null
                ? _rootForBranches
                : throw new InvalidOperationException($"{name} is missing _rootForBranches.");
        }

        private TreeBlueprintSO GetRequiredBlueprint()
        {
            return blueprint ?? throw new InvalidOperationException($"{name} is missing blueprint.");
        }

        private static BranchPlacement[] GetRequiredPlacements(TreeBlueprintSO currentBlueprint)
        {
            BranchPlacement[] placements = currentBlueprint.Branches;
            if (placements == null || placements.Length == 0)
            {
                throw new InvalidOperationException($"{currentBlueprint.name} does not contain any branch placements.");
            }

            return placements;
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

        private static Mesh GetRequiredShellWoodMesh(BranchPrototypeSO prototype, int shellLevel)
        {
            return shellLevel switch
            {
                0 => prototype.WoodMesh ?? throw new InvalidOperationException($"{prototype.name} is missing woodMesh."),
                1 => prototype.ShellL1WoodMesh ?? throw new InvalidOperationException($"{prototype.name} is missing shellL1WoodMesh."),
                2 => prototype.ShellL2WoodMesh ?? throw new InvalidOperationException($"{prototype.name} is missing shellL2WoodMesh."),
                _ => throw new ArgumentOutOfRangeException(nameof(shellLevel), shellLevel, "Shell level must be 0, 1, or 2.")
            };
        }

        private static GameObject CreateChildGameObject(string childName, Transform parent)
        {
            GameObject childObject = new GameObject(childName)
            {
                layer = parent.gameObject.layer
            };

            childObject.transform.SetParent(parent, false);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.RegisterCreatedObjectUndo(childObject, $"Create {childName}");
            }
#endif

            return childObject;
        }

        private static void DestroyChildGameObject(GameObject childObject)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.DestroyObjectImmediate(childObject);
                return;
            }
#endif

            Destroy(childObject);
        }

        private void MarkAuthoringObjectsDirty()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                return;
            }

            EditorUtility.SetDirty(this);
            EditorUtility.SetDirty(GetRequiredBranchRoot());
#endif
        }

#if UNITY_EDITOR
        private void SaveEditorAuthoringChanges()
        {
            EditorUtility.SetDirty(this);
            EditorUtility.SetDirty(GetRequiredBlueprint());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void InvokeEditorBakeMethod(string typeName, string methodName, params object?[] arguments)
        {
            Type editorType = Type.GetType($"{typeName}, Vegetation.Editor") ??
                              throw new InvalidOperationException($"Editor bake type '{typeName}' was not found.");
            MethodInfo method = editorType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static) ??
                                throw new InvalidOperationException($"Editor bake method '{methodName}' was not found on '{typeName}'.");

            try
            {
                method.Invoke(null, arguments);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }
#endif
    }
}
