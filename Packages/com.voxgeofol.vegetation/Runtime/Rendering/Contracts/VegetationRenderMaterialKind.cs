#nullable enable

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Exact runtime shader family required by one compiled asset group.
    /// Material kind is part of asset-group identity even when mesh and material asset references match.
    /// </summary>
    public enum VegetationRenderMaterialKind
    {
        Trunk = 0,
        CanopyFoliage = 1,
        CanopyShell = 2,
        FarMesh = 3
    }
}
