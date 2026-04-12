#nullable enable

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Runtime tree-level visibility outcome for one frame.
    /// Current shipped limitation: Expanded still leads into flat branch-span work unless later prioritization/compaction rejects that tree.
    /// </summary>
    public enum VegetationTreeRenderMode
    {
        Culled = 0,
        Expanded = 1,
        Impostor = 2
    }
}
