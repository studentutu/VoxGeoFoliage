#nullable enable

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Exact runtime shader family required by one draw slot.
    /// Material kind is part of draw-slot identity even when mesh and material asset references match.
    /// </summary>
    public enum VegetationRenderMaterialKind
    {
        Trunk = 0,
        CanopyFoliage = 1,
        CanopyShell = 2,
        FarMesh = 3
    }
}
