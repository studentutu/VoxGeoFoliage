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

        [Tooltip("When enabled, vegetation renders for Game cameras.")]
        public bool RenderGameCameras = true;

        [Tooltip("When enabled, vegetation renders for SceneView cameras.")]
        public bool RenderSceneViewCameras = true;

        [Tooltip("Emits renderer-wide runtime diagnostics for registration, preparation, and indirect submission.")]
        public bool EnableDiagnostics;

        /// <summary>
        /// Primary classification compute, e.g. VegetationClassify.compute.
        /// </summary>
        [Tooltip("Compute shader used to classify vegetation and emit indirect instance payloads. Assign VegetationClassify.compute.")]
        public ComputeShader? ClassifyShader;
    }
}
