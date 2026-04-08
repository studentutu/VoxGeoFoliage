#nullable enable

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Runtime-side flattened LOD profile payload.
    /// </summary>
    public struct VegetationLodProfileRuntime
    {
        public float L0Distance;
        public float L1Distance;
        public float L2Distance;
        public float ImpostorDistance;
        public float AbsoluteCullDistance;
    }
}
