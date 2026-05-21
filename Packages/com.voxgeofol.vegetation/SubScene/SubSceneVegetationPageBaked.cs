#nullable enable

using Unity.Entities;
using VoxGeoFol.Features.Vegetation.Rendering;

namespace VoxGeoFol.Features.Vegetation.SubScene
{
    /// <summary>
    /// Baked compiled foliage page reference loaded by the SubScene provider bootstrap.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct SubSceneVegetationPageBaked : IBufferElementData
    {
        public UnityObjectRef<FoliagePageAsset> Page;
    }
}
