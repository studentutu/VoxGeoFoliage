#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Authoring
{
    /// <summary>
    /// Immutable authoring asset for one reusable branch module.
    /// </summary>
    [CreateAssetMenu(fileName = "BranchPrototype", menuName = "VoxGeoFol/Vegetation/Branch Prototype")]
    public sealed class BranchPrototypeSO : ScriptableObject
    {
        [SerializeField] private Mesh? woodMesh;
        [SerializeField] private Material? woodMaterial;
        [SerializeField] private Mesh? foliageMesh;
        [SerializeField] private Material? foliageMaterial;
        [SerializeField] private Color leafColorTint = Color.white;
        [SerializeField] private Mesh? shellL0Mesh;
        [SerializeField] private Mesh? shellL1Mesh;
        [SerializeField] private Mesh? shellL2Mesh;
        [SerializeField] private Material? shellMaterial;
        [SerializeField] private Bounds localBounds = new Bounds(Vector3.zero, Vector3.one);
        [SerializeField] [Min(1)] private int triangleBudgetWood = 2048;
        [SerializeField] [Min(1)] private int triangleBudgetFoliage = 2048;
        [SerializeField] [Min(1)] private int triangleBudgetShellL0 = 2000;
        [SerializeField] [Min(1)] private int triangleBudgetShellL1 = 500;
        [SerializeField] [Min(1)] private int triangleBudgetShellL2 = 150;

        public Mesh? WoodMesh => woodMesh;

        public Material? WoodMaterial => woodMaterial;

        public Mesh? FoliageMesh => foliageMesh;

        public Material? FoliageMaterial => foliageMaterial;

        public Color LeafColorTint => leafColorTint;

        public Mesh? ShellL0Mesh => shellL0Mesh;

        public Mesh? ShellL1Mesh => shellL1Mesh;

        public Mesh? ShellL2Mesh => shellL2Mesh;

        public Material? ShellMaterial => shellMaterial;

        public Bounds LocalBounds => localBounds;

        public int TriangleBudgetWood => triangleBudgetWood;

        public int TriangleBudgetFoliage => triangleBudgetFoliage;

        public int TriangleBudgetShellL0 => triangleBudgetShellL0;

        public int TriangleBudgetShellL1 => triangleBudgetShellL1;

        public int TriangleBudgetShellL2 => triangleBudgetShellL2;

        /// <summary>
        /// [INTEGRATION] Validates this branch prototype against the vegetation authoring contract.
        /// </summary>
        public VegetationValidationResult Validate()
        {
            return VegetationAuthoringValidator.ValidateBranchPrototype(this);
        }
    }
}
