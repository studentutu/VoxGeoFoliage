#nullable enable

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Stable shell-node decision payload aligned with the MVP BFS hierarchy contract.
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
