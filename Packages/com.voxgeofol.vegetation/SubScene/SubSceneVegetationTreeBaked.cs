#nullable enable

using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace VoxGeoFol.Features.Vegetation.SubScene
{
    /// <summary>
    /// Baked runtime-safe tree record shared with the classic provider contract.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct SubSceneVegetationTreeBaked : IBufferElementData
    {
        public Unity.Entities.Hash128 StableTreeIdHash;
        public FixedString64Bytes DebugName;
        public Matrix4x4 LocalToWorld;
        public UnityObjectRef<TreeBlueprintSO> Blueprint;
        public byte IsActive;
    }
}
