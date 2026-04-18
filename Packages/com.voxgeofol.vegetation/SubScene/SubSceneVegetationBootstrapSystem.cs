#nullable enable

using Unity.Collections;
using Unity.Entities;
using VoxGeoFol.Features.Vegetation.Rendering;

namespace VoxGeoFol.Features.Vegetation.SubScene
{
    /// <summary>
    /// [INTEGRATION] Creates one vegetation runtime owner when baked SubScene data loads and relies on managed-component disposal for unload teardown.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public sealed partial class SubSceneVegetationBootstrapSystem : SystemBase
    {
        private EntityQuery pendingRuntimeBootstrapQuery;

        protected override void OnCreate()
        {
            pendingRuntimeBootstrapQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    typeof(SubSceneVegetationContainerBaked),
                    typeof(SubSceneVegetationTreeBaked)
                },
                None = new ComponentType[]
                {
                    typeof(SubSceneVegetationRuntimeState)
                }
            });
        }

        protected override void OnUpdate()
        {
            using NativeArray<Entity> entities = pendingRuntimeBootstrapQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                SubSceneVegetationContainerBaked container = EntityManager.GetComponentData<SubSceneVegetationContainerBaked>(entity);
                DynamicBuffer<SubSceneVegetationTreeBaked> bakedTrees = EntityManager.GetBuffer<SubSceneVegetationTreeBaked>(entity);
                VegetationTreeAuthoringRuntime[] runtimeTrees = new VegetationTreeAuthoringRuntime[bakedTrees.Length];

                for (int treeIndex = 0; treeIndex < bakedTrees.Length; treeIndex++)
                {
                    SubSceneVegetationTreeBaked bakedTree = bakedTrees[treeIndex];
                    runtimeTrees[treeIndex] = new VegetationTreeAuthoringRuntime(
                        bakedTree.StableTreeIdHash,
                        bakedTree.DebugName.ToString(),
                        bakedTree.Blueprint,
                        bakedTree.LocalToWorld,
                        bakedTree.IsActive != 0);
                }

                string debugName = container.DebugName.Length > 0
                    ? container.DebugName.ToString()
                    : container.ContainerIdHash.ToString();
                AuthoringContainerRuntime runtimeOwner = new AuthoringContainerRuntime(
                    container.ContainerIdHash.ToString(),
                    VegetationRuntimeProviderKind.SubScene,
                    debugName,
                    null,
                    container.RenderLayer,
                    container.GridOrigin,
                    container.CellSize,
                    new VegetationRuntimeBudget(
                        new VegetationViewRuntimeBudget(
                            container.ColorMaxVisibleInstances,
                            container.ColorMaxExpandedBranchWorkItems,
                            container.ColorMaxApproxWorkUnits),
                        new VegetationViewRuntimeBudget(
                            container.ShadowMaxVisibleInstances,
                            container.ShadowMaxExpandedBranchWorkItems,
                            container.ShadowMaxApproxWorkUnits),
                        container.MaxRegisteredDrawSlots),
                    runtimeTrees);
                runtimeOwner.Activate();
                runtimeOwner.RefreshRuntimeRegistration();
                EntityManager.AddComponentObject(entity, new SubSceneVegetationRuntimeState(runtimeOwner));
            }
        }
    }
}
