#nullable enable

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Runtime-side flattened tree blueprint payload.
    /// </summary>
    public struct VegetationTreeBlueprintRuntime
    {
        public int LodProfileIndex;
        public int BranchPlacementStartIndex;
        public int BranchPlacementCount;
        public int TrunkFullDrawSlot;
        public int TrunkL3DrawSlot;
        public int ImpostorDrawSlot;
    }
}
