#nullable enable

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Runtime tree-level visibility outcome for one frame.
    /// </summary>
    public enum VegetationTreeRenderMode
    {
        Culled = 0,
        Expanded = 1,
        Impostor = 2
    }
}
