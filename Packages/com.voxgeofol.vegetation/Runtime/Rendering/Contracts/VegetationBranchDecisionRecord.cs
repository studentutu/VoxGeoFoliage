#nullable enable

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Stable branch-tier decision payload for one scene branch instance.
    /// </summary>
    public struct VegetationBranchDecisionRecord
    {
        public const int InactiveRuntimeTier = -1;

        public int TreeIndex;
        public int BranchInstanceIndex;
        public int BranchPlacementIndex;
        public int RuntimeTier;

        public bool IsActive => RuntimeTier >= 0;
    }
}
