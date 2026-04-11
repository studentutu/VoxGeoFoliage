#nullable enable

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Visible-frame ownership mode used by the runtime manager before Phase E submission.
    /// </summary>
    public enum VegetationRuntimeFrameSource
    {
        CpuReference = 0,
        GpuDecisionReadback = 1,
        GpuResident = 2
    }
}
