#nullable enable

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Compiled shadow behavior for one packet.
    /// </summary>
    public enum FoliageShadowPacketMode
    {
        None = 0,
        SameAsColor = 1,
        CheapTree = 2,
        Hlod = 3
    }
}
