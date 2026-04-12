#nullable enable

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Stable shell-node decision payload aligned with the intended hierarchy traversal contract.
    /// Current shipped limitation: true per-node frontier traversal is not active in the shader path yet.
    /// </summary>
    public struct VegetationNodeDecisionRecord
    {
        public int TreeIndex;
        public int BranchInstanceIndex;
        public int BranchPlacementIndex;
        public int RuntimeTier;
        public int NodeIndex;
        public int Decision;
    }
}
