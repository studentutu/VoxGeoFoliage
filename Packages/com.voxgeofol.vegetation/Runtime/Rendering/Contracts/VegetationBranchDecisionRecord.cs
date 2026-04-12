#nullable enable

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Compact promoted-tree branch work payload used by the urgent runtime path.
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
