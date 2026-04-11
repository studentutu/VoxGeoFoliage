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
        public RenderPassEvent DepthPassEvent = RenderPassEvent.BeforeRenderingOpaques;

        /// <summary>
        /// Final vegetation color submission will be executed at this stage.
        /// </summary>
        public RenderPassEvent ColorPassEvent = RenderPassEvent.AfterRenderingOpaques;

        public bool RenderGameCameras = true;
        public bool RenderSceneViewCameras = true;
        public bool EnableDiagnostics;

        /// <summary>
        /// Primary classification compute, e.g. VegetationClassify.compute.
        /// </summary>
        public ComputeShader? ClassifyShader;
    }
}
