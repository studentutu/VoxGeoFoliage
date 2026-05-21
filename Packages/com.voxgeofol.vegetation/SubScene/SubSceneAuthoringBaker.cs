#nullable enable

using System;
using Unity.Collections;
using Unity.Entities;
using VoxGeoFol.Features.Vegetation.Rendering;

namespace VoxGeoFol.Features.Vegetation.SubScene
{
    /// <summary>
    /// [INTEGRATION] Bakes one sibling vegetation runtime container into compiled-page SubScene provider data.
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

            FoliageAssemblyAsset assemblyAsset = container.CompiledAssembly ??
                                                 throw new InvalidOperationException(
                                                     $"{nameof(SubSceneAuthoring)} on '{container.name}' requires compiled foliage assembly data.");
            if (container.CompiledPages.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(SubSceneAuthoring)} on '{container.name}' requires at least one compiled foliage page.");
            }

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new SubSceneVegetationContainerBaked
            {
                ContainerIdHash = container.ContainerIdHash,
                DebugName = new FixedString64Bytes(container.name),
                CompiledAssembly = assemblyAsset
            });

            DynamicBuffer<SubSceneVegetationPageBaked> pageBuffer = AddBuffer<SubSceneVegetationPageBaked>(entity);
            for (int i = 0; i < container.CompiledPages.Count; i++)
            {
                FoliagePageAsset page = container.CompiledPages[i] ??
                                        throw new InvalidOperationException(
                                            $"{nameof(SubSceneAuthoring)} on '{container.name}' has a null compiled page at index {i}.");
                pageBuffer.Add(new SubSceneVegetationPageBaked
                {
                    Page = page
                });
            }
        }
    }
}
