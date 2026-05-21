#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Rendering settings for runtime of the <see cref="VegetationRendererFeature"/>.
    /// </summary>
    [Serializable]
    public sealed class VegetationFoliageFeatureSettings
    {
        /// <summary>
        /// First camera packet selection and depth submission run here.
        /// </summary>
        [Tooltip("Render event for vegetation depth submission. The first vegetation pass of the frame prepares compiled packet buffers.")]
        public RenderPassEvent DepthPassEvent = RenderPassEvent.BeforeRenderingOpaques;

        /// <summary>
        /// Final vegetation color submission will be executed at this stage.
        /// </summary>
        [Tooltip("Render event for the vegetation color pass after the GPU-resident frame has been prepared.")]
        public RenderPassEvent ColorPassEvent = RenderPassEvent.AfterRenderingOpaques;

        /// <summary>
        /// Main-light shadow atlas submission runs here.
        /// </summary>
        [Tooltip("Render event for vegetation shadow-map submission. Current contract: main-light directional shadow atlas only, using cascade-specific resident frames derived from the camera-visible vegetation set.")]
        public RenderPassEvent ShadowPassEvent = RenderPassEvent.AfterRenderingShadows;

        [Tooltip("Production shadow mode. CheapTree uses compiled packet shadow metadata; Off skips vegetation shadow-caster submission.")]
        public VegetationShadowMode ShadowMode = VegetationShadowMode.CheapTree;

        [Tooltip("When enabled, vegetation submits a dedicated depth pass. Disable when the color pass depth write is sufficient for the active URP feature stack.")]
        public bool EnableDepthPass = true;

        [Min(1f)]
        [Tooltip("Cells closer than this distance can request near-detail TreeL0/L1/L2 packets. Farther visible cells draw compiled HLOD packets.")]
        public float NearDetailDistance = 11f;

        [Min(1)]
        [Tooltip("Global color/depth accepted packet work budget for the compiled render world.")]
        public int ColorWorkBudget = 131072;

        [Min(1)]
        [Tooltip("Global shadow accepted packet work budget for the compiled render world.")]
        public int ShadowWorkBudget = 65536;

        [Min(1)]
        [Tooltip("Hard cap for visible packet instances uploaded by the compiled render world.")]
        public int MaxVisiblePacketInstances = 131072;

        [Min(0)]
        [Tooltip("Total near-detail packet payload bytes that may stay resident in the render world. HLOD packets are always resident and do not count against this budget.")]
        public int NearDetailResidentByteBudget = 64 * 1024 * 1024;

        [Min(0)]
        [Tooltip("Near-detail packet payload bytes that may be streamed into residency during one render-world prepare.")]
        public int NearDetailUploadByteBudget = 8 * 1024 * 1024;

        [Min(0f)]
        [Tooltip("Global wind displacement strength. Per-packet metadata controls phase and weights.")]
        public float WindStrength = 0.35f;

        [Min(0f)]
        [Tooltip("Global wind animation frequency in shader time units.")]
        public float WindFrequency = 1f;

        [Tooltip("Horizontal wind direction used by vegetation shaders.")]
        public Vector3 WindDirection = new Vector3(1f, 0f, 0.35f);

        [Min(0f)]
        [Tooltip("Canopy-local leaf flutter amplitude multiplier. This affects only local leaf vertex motion, not branch/trunk sway.")]
        public float LeafFlutterStrength = 0.035f;

        [Min(0f)]
        [Tooltip("Canopy-local leaf flutter animation speed relative to trunk wind frequency.")]
        public float LeafFlutterFrequencyMultiplier = 2.13f;

        [Min(0f)]
        [Tooltip("Canopy-local leaf flutter world-space variation scale across the canopy.")]
        public float LeafFlutterSpatialScale = 4f;

        [Min(0f)]
        [Tooltip("Secondary harmonic strength for canopy-local leaf flutter variation.")]
        public float LeafFlutterSecondaryStrength = 0.5f;

        [Tooltip("When enabled, vegetation renders for Game cameras.")]
        public bool RenderGameCameras = true;

        [Tooltip("When enabled, vegetation renders for SceneView cameras.")]
        public bool RenderSceneViewCameras = true;

        [Tooltip("Emits renderer-wide compiled packet diagnostics for provider registration, page/cell culling, packet selection, indirect submission, shadows, and wind.")]
        public bool EnableDiagnostics;

        public int GetWorkBudget(VegetationRenderPassMode passMode)
        {
            return passMode == VegetationRenderPassMode.Shadow
                ? Mathf.Max(1, ShadowWorkBudget)
                : Mathf.Max(1, ColorWorkBudget);
        }

        public long GetNearDetailResidentByteBudget()
        {
            return Math.Max(0, NearDetailResidentByteBudget);
        }

        public long GetNearDetailUploadByteBudget()
        {
            return Math.Max(0, NearDetailUploadByteBudget);
        }
    }
}
