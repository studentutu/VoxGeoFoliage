#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Authoring
{
    /// <summary>
    /// Scene binding that points to one immutable tree blueprint asset.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VegetationTreeAuthoring : MonoBehaviour
    {
        [SerializeField] private TreeBlueprintSO? blueprint;
        [SerializeField] private GameObject? _rootForBranches;
        [SerializeField] [HideInInspector] private int runtimeTreeIndex = -1;

        public TreeBlueprintSO? Blueprint => blueprint;

        public GameObject? BranchRoot => _rootForBranches;

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
    }
}
