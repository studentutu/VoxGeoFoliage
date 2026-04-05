#nullable enable

using System;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Authoring
{
    /// <summary>
    /// Immutable authoring asset describing one assembled tree species.
    /// </summary>
    [CreateAssetMenu(fileName = "TreeBlueprint", menuName = "VoxGeoFol/Vegetation/Tree Blueprint")]
    public sealed class TreeBlueprintSO : ScriptableObject
    {
        [SerializeField] private Mesh? trunkMesh;
        [SerializeField] private Mesh? trunkL3Mesh;
        [SerializeField] private Material? trunkMaterial;
        [SerializeField] private BranchPlacement[] branches = Array.Empty<BranchPlacement>();
        [SerializeField] private string generatedImpostorMeshesRelativeFolder = string.Empty;
        [SerializeField] private Mesh? impostorMesh;
        [SerializeField] private Material? impostorMaterial;
        [SerializeField] private LODProfileSO? lodProfile;
        [SerializeField] private ImpostorBakeSettings? ImposterBakeSettings;
        [SerializeField] private Bounds treeBounds = new Bounds(Vector3.zero, Vector3.one);

        public Mesh? TrunkMesh => trunkMesh;

        public Mesh? TrunkL3Mesh => trunkL3Mesh;

        public Material? TrunkMaterial => trunkMaterial;

        public BranchPlacement[] Branches => branches;

        public string GeneratedImpostorMeshesRelativeFolder => generatedImpostorMeshesRelativeFolder;

        public Mesh? ImpostorMesh => impostorMesh;

        public Material? ImpostorMaterial => impostorMaterial;

        public ImpostorBakeSettings ImposterSettings => ImposterBakeSettings ?? new ImpostorBakeSettings();

        public LODProfileSO? LodProfile => lodProfile;

        public Bounds TreeBounds => treeBounds;

        /// <summary>
        /// [INTEGRATION] Validates this tree blueprint before editor preview, baking, or runtime gather consume it.
        /// </summary>
        public VegetationValidationResult Validate()
        {
            return VegetationAuthoringValidator.ValidateTreeBlueprint(this);
        }
    }
}
