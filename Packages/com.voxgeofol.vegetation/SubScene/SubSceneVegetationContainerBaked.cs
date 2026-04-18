#nullable enable

using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.SubScene
{
    /// <summary>
    /// Baked runtime-container settings used to bootstrap one vegetation runtime owner from SubScene data.
    /// </summary>
    public struct SubSceneVegetationContainerBaked : IComponentData
    {
        public Unity.Entities.Hash128 ContainerIdHash;
        public FixedString64Bytes DebugName;
        public Vector3 GridOrigin;
        public Vector3 CellSize;
        public int RenderLayer;
        public int ColorMaxVisibleInstances;
        public int ColorMaxExpandedBranchWorkItems;
        public int ColorMaxApproxWorkUnits;
        public int ShadowMaxVisibleInstances;
        public int ShadowMaxExpandedBranchWorkItems;
        public int ShadowMaxApproxWorkUnits;
        public int MaxRegisteredDrawSlots;
    }
}
