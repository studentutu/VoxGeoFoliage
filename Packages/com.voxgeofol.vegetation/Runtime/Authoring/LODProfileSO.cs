#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Authoring
{
    /// <summary>
    /// Immutable authored distance bands for vegetation tier selection.
    /// </summary>
    [CreateAssetMenu(fileName = "VegetationLODProfile", menuName = "VoxGeoFol/Vegetation/LOD Profile")]
    public sealed class LODProfileSO : ScriptableObject
    {
        [SerializeField] [Min(0.01f)] private float l0Distance = 5f;
        [SerializeField] [Min(0.01f)] private float l1Distance = 15f;
        [SerializeField] [Min(0.01f)] private float l2Distance = 30f;
        [SerializeField] [Min(0.01f)] private float l3Distance = 60f;
        [SerializeField] [Min(0.01f)] private float impostorDistance = 120f;
        [SerializeField] [Min(0.01f)] private float absoluteCullDistance = 10_000f;

        public float L0Distance => l0Distance;

        public float L1Distance => l1Distance;

        public float L2Distance => l2Distance;

        public float L3Distance => l3Distance;

        public float ImpostorDistance => impostorDistance;

        public float AbsoluteCullDistance => absoluteCullDistance;

        /// <summary>
        /// [INTEGRATION] Validates this LOD profile before runtime buffer flattening consumes it.
        /// </summary>
        public VegetationValidationResult Validate()
        {
            return VegetationAuthoringValidator.ValidateLodProfile(this);
        }
    }
}
