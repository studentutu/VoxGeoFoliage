#nullable enable

using System;
using Unity.Entities;
using VoxGeoFol.Features.Vegetation.Rendering;

namespace VoxGeoFol.Features.Vegetation.SubScene
{
    /// <summary>
    /// Managed DOTS runtime state that unregisters one compiled render-world provider when the baked entity unloads.
    /// </summary>
    public sealed class SubSceneVegetationRuntimeState : IComponentData, IDisposable
    {
        private string? providerId;

        // Required by Unity for class-based IComponentData.
        public SubSceneVegetationRuntimeState()
        {
        }

        public SubSceneVegetationRuntimeState(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException("Provider id is required.", nameof(providerId));
            }

            this.providerId = providerId;
        }

        public void Dispose()
        {
            string? id = providerId;
            if (id != null && id.Trim().Length > 0)
            {
                VegetationRenderWorld.Shared.UnregisterProvider(id);
            }

            providerId = null;
        }
    }
}
