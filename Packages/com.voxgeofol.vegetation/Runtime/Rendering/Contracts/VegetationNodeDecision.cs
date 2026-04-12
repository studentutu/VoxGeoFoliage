#nullable enable

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Intended runtime shell-node decision contract for real hierarchy traversal.
    /// Current shipped limitation: the shader does not yet consume this as part of a true BFS frontier decode.
    /// </summary>
    public enum VegetationNodeDecision
    {
        Reject = 0,
        EmitSelf = 1,
        ExpandChildren = 2
    }
}
