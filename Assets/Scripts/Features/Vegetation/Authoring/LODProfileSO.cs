#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Authoring
{
    /// <summary>
    /// Immutable projected-area thresholds for vegetation tier selection.
    /// </summary>
    [CreateAssetMenu(fileName = "VegetationLODProfile", menuName = "VoxGeoFol/Vegetation/LOD Profile")]
    public sealed class LODProfileSO : ScriptableObject
    {
        [SerializeField] private float r0MinProjectedArea = 0.4f;
        [SerializeField] private float r1MinProjectedArea = 0.2f;
        [SerializeField] private float shellL0MinProjectedArea = 0.1f;
        [SerializeField] private float shellL1MinProjectedArea = 0.05f;
        [SerializeField] private float shellL2MinProjectedArea = 0.02f;
        [SerializeField] private float absoluteCullProjectedMin = 0.005f;
        [SerializeField] private float backsideBiasScale = 0.3f;
        [SerializeField] private float silhouetteKeepThreshold = 0.7f;

        public float R0MinProjectedArea => r0MinProjectedArea;

        public float R1MinProjectedArea => r1MinProjectedArea;

        public float ShellL0MinProjectedArea => shellL0MinProjectedArea;

        public float ShellL1MinProjectedArea => shellL1MinProjectedArea;

        public float ShellL2MinProjectedArea => shellL2MinProjectedArea;

        public float AbsoluteCullProjectedMin => absoluteCullProjectedMin;

        public float BacksideBiasScale => backsideBiasScale;

        public float SilhouetteKeepThreshold => silhouetteKeepThreshold;

        /// <summary>
        /// [INTEGRATION] Validates this LOD profile before runtime buffer flattening consumes it.
        /// </summary>
        public VegetationValidationResult Validate()
        {
            return VegetationAuthoringValidator.ValidateLodProfile(this);
        }
    }
}
