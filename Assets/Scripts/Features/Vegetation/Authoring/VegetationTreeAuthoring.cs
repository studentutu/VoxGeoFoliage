#nullable enable

using System;
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
        ///     Editor preview for the saved data based on the original branch and leaves.
        /// </summary>
        [ContextMenu("ReconstructOriginalFromData")]
        public void ReconstructFromDataAndOriginalBranch()
        {
            DeleteBranchRootChildren();
            RefreshOriginalBranchAssemblyFromBlueprint();
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
            TreeBlueprintSO currentBlueprint = blueprint ?? throw new InvalidOperationException($"{name} is missing blueprint.");
            BranchPlacement[] placements = currentBlueprint.Branches;
            if (placements == null || placements.Length == 0)
            {
                throw new InvalidOperationException($"{currentBlueprint.name} does not contain any branch placements.");
            }

            DeleteBranchRootChildren();

            Transform branchRootTransform = GetRequiredBranchRoot().transform;
            for (int i = 0; i < placements.Length; i++)
            {
                CreateOriginalBranchGameObject(branchRootTransform, placements[i], i);
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
    }
}
