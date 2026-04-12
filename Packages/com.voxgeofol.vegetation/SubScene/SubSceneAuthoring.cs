#nullable enable

using UnityEngine;
using VoxGeoFol.Features.Vegetation.Rendering;

namespace VoxGeoFol.Features.Vegetation.SubScene
{
    /// <summary>
    /// [INTEGRATION] Lightweight SubScene provider marker that bakes one sibling vegetation runtime container into DOTS runtime bootstrap data.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VegetationRuntimeContainer))]
    public sealed class SubSceneAuthoring : MonoBehaviour
    {
    }
}
