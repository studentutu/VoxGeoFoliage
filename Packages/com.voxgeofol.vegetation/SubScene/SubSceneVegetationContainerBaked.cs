#nullable enable

using Unity.Collections;
using Unity.Entities;
using VoxGeoFol.Features.Vegetation.Rendering;

namespace VoxGeoFol.Features.Vegetation.SubScene
{
    /// <summary>
    /// Baked compiled-page provider settings used to register one vegetation SubScene provider.
    /// </summary>
    public struct SubSceneVegetationContainerBaked : IComponentData
    {
        public Unity.Entities.Hash128 ContainerIdHash;
        public FixedString64Bytes DebugName;
        public UnityObjectRef<FoliageAssemblyAsset> CompiledAssembly;
    }
}
