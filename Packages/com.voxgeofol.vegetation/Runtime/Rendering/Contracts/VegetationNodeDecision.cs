#nullable enable

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Runtime shell-node decision contract for the BFS hierarchy decode.
    /// </summary>
    public enum VegetationNodeDecision
    {
        Reject = 0,
        EmitSelf = 1,
        ExpandChildren = 2
    }
}
