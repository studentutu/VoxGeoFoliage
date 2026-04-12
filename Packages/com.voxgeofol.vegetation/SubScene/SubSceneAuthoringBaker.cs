#nullable enable

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using VoxGeoFol.Features.Vegetation.Rendering;

namespace VoxGeoFol.Features.Vegetation.SubScene
{
    /// <summary>
    /// [INTEGRATION] Bakes one sibling vegetation runtime container into runtime-safe SubScene bootstrap data.
    /// </summary>
    public sealed class SubSceneAuthoringBaker : Baker<SubSceneAuthoring>
    {
        public override void Bake(SubSceneAuthoring authoring)
        {
            if (authoring == null)
            {
                throw new ArgumentNullException(nameof(authoring));
            }

            VegetationRuntimeContainer container = authoring.GetComponent<VegetationRuntimeContainer>() ??
                                                   throw new InvalidOperationException(
                                                       $"{nameof(SubSceneAuthoring)} requires {nameof(VegetationRuntimeContainer)} on the same GameObject.");

            List<VegetationTreeAuthoringRuntime> runtimeTrees = new List<VegetationTreeAuthoringRuntime>();
            container.BuildRuntimeTreeAuthorings(runtimeTrees);

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new SubSceneVegetationContainerBaked
            {
                ContainerIdHash = container.ContainerIdHash,
                DebugName = new FixedString64Bytes(container.name),
                GridOrigin = container.GridOrigin,
                CellSize = container.CellSize,
                RenderLayer = container.RenderLayer,
                MaxVisibleInstanceCapacity = container.MaxVisibleInstanceCapacity
            });

            DynamicBuffer<SubSceneVegetationTreeBaked> treeBuffer = AddBuffer<SubSceneVegetationTreeBaked>(entity);
            for (int i = 0; i < runtimeTrees.Count; i++)
            {
                VegetationTreeAuthoringRuntime runtimeTree = runtimeTrees[i];
                treeBuffer.Add(new SubSceneVegetationTreeBaked
                {
                    StableTreeIdHash = runtimeTree.StableTreeIdHash,
                    DebugName = new FixedString64Bytes(runtimeTree.DebugName),
                    LocalToWorld = runtimeTree.LocalToWorld,
                    Blueprint = runtimeTree.Blueprint,
                    IsActive = runtimeTree.IsActive ? (byte)1 : (byte)0
                });
            }
        }
    }
}
