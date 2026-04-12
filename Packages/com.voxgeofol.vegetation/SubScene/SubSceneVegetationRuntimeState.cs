#nullable enable

using System;
using Unity.Entities;
using VoxGeoFol.Features.Vegetation.Rendering;

namespace VoxGeoFol.Features.Vegetation.SubScene
{
    /// <summary>
    /// Managed DOTS runtime state that owns one active vegetation runtime owner until the baked entity unloads.
    /// </summary>
    public sealed class SubSceneVegetationRuntimeState : IComponentData, IDisposable
    {
        private AuthoringContainerRuntime? runtimeOwner;

        // Required by Unity for class-based IComponentData.
        public SubSceneVegetationRuntimeState()
        {
        }

        public SubSceneVegetationRuntimeState(AuthoringContainerRuntime runtimeOwner)
        {
            this.runtimeOwner = runtimeOwner ?? throw new ArgumentNullException(nameof(runtimeOwner));
        }

        public AuthoringContainerRuntime RuntimeOwner => runtimeOwner ??
                                                         throw new InvalidOperationException(
                                                             "SubSceneVegetationRuntimeState was constructed without a runtime owner.");

        public void Dispose()
        {
            runtimeOwner?.Dispose();
            runtimeOwner = null;
        }
    }
}
