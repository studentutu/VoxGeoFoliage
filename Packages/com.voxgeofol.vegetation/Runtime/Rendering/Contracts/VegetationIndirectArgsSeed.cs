#nullable enable

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Stable Phase E handoff input for one indirect draw slot.
    /// </summary>
    public struct VegetationIndirectArgsSeed
    {
        public int DrawSlotIndex;
        public uint IndexCountPerInstance;
        public uint InstanceCount;
        public uint StartIndexLocation;
        public int BaseVertexLocation;
        public uint StartInstanceLocation;
    }
}
