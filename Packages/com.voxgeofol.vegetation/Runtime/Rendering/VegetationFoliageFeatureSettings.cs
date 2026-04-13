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
        /// Main work for classification will be executed at this stage.
        /// </summary>
        [Tooltip("Render event for the vegetation depth pass. The first vegetation pass of the frame also triggers GPU frame preparation.")]
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

        [Tooltip("When enabled, the feature appends vegetation shadow casters into the main-light shadow atlas. Current contract: main-light directional shadows only, using cascade-specific resident frames derived from the camera-visible vegetation set.")]
        public bool RenderMainLightShadows = true;

        [Tooltip("When disabled, shadow preparation clamps visible non-far vegetation to the TreeL3 floor and skips expanded branch shadow casters. Keep this off unless you can afford the GPU cost of dense near-shadow foliage.")]
        public bool AllowExpandedTreePromotionInShadows;

        [Tooltip("When enabled, vegetation renders for Game cameras.")]
        public bool RenderGameCameras = true;

        [Tooltip("When enabled, vegetation renders for SceneView cameras.")]
        public bool RenderSceneViewCameras = true;

        [Tooltip("Emits renderer-wide runtime diagnostics for registration, preparation, indirect submission, branch/shell/visible-instance telemetry, and one-shot emitted-slot readback for architecture review.")]
        public bool EnableDiagnostics;

        /// <summary>
        /// Primary classification compute, e.g. VegetationClassify.compute.
        /// </summary>
        [Tooltip("Compute shader used to classify vegetation and emit indirect instance payloads. Assign VegetationClassify.compute.")]
        public ComputeShader? ClassifyShader;
    }
}
