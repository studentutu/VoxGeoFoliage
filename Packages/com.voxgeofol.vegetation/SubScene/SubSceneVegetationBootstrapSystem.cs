#nullable enable

using Unity.Collections;
using Unity.Entities;
using VoxGeoFol.Features.Vegetation.Rendering;

namespace VoxGeoFol.Features.Vegetation.SubScene
{
    /// <summary>
    /// [INTEGRATION] Registers one compiled vegetation provider when baked SubScene data loads and relies on managed-component disposal for unload teardown.
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
                    typeof(SubSceneVegetationPageBaked)
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
                DynamicBuffer<SubSceneVegetationPageBaked> bakedPages = EntityManager.GetBuffer<SubSceneVegetationPageBaked>(entity);
                FoliagePageAsset[] pages = new FoliagePageAsset[bakedPages.Length];
                for (int pageIndex = 0; pageIndex < bakedPages.Length; pageIndex++)
                {
                    pages[pageIndex] = bakedPages[pageIndex].Page;
                }

                FoliageAssemblyAsset assemblyAsset = container.CompiledAssembly;
                string debugName = container.DebugName.Length > 0
                    ? container.DebugName.ToString()
                    : container.ContainerIdHash.ToString();
                string providerId = container.ContainerIdHash.ToString();
                VegetationRenderWorld.Shared.RegisterProvider(providerId, debugName, assemblyAsset, pages);
                EntityManager.AddComponentObject(entity, new SubSceneVegetationRuntimeState(providerId));
            }
        }
    }
}
