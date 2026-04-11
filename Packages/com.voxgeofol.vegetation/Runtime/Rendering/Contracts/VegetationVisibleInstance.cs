#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Stable visible-instance payload emitted by Phase D for one exact draw slot.
    /// </summary>
    public struct VegetationVisibleInstance
    {
        public int TreeIndex;
        public Bounds WorldBounds;
    }
}
