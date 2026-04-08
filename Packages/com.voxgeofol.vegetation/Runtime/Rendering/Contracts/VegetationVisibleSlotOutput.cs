#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Reusable visible-instance aggregation for one exact draw slot.
    /// </summary>
    public sealed class VegetationVisibleSlotOutput
    {
        private readonly List<VegetationVisibleInstance> instances = new List<VegetationVisibleInstance>();
        private Bounds visibleBounds;
        private bool hasVisibleBounds;

        public VegetationVisibleSlotOutput(VegetationDrawSlot drawSlot)
        {
            DrawSlot = drawSlot;
        }

        public VegetationDrawSlot DrawSlot { get; }

        public IReadOnlyList<VegetationVisibleInstance> Instances => instances;

        public int InstanceCount => instances.Count;

        public Bounds VisibleBounds => visibleBounds;

        public bool HasVisibleBounds => hasVisibleBounds;

        /// <summary>
        /// [INTEGRATION] Clears the previous frame output so Phase D can rebuild visible slot data deterministically.
        /// </summary>
        public void Reset()
        {
            instances.Clear();
            visibleBounds = default;
            hasVisibleBounds = false;
        }

        /// <summary>
        /// [INTEGRATION] Appends one visible instance and refreshes the exact visible-data bounds for this slot.
        /// </summary>
        public void AddInstance(VegetationVisibleInstance instance)
        {
            if (!hasVisibleBounds)
            {
                visibleBounds = instance.WorldBounds;
                hasVisibleBounds = true;
            }
            else
            {
                visibleBounds.Encapsulate(instance.WorldBounds);
            }

            instances.Add(instance);
        }

        /// <summary>
        /// [INTEGRATION] Exposes the final indirect-args seed for this slot from the current visible list.
        /// </summary>
        public VegetationIndirectArgsSeed BuildIndirectArgsSeed()
        {
            return DrawSlot.BuildIndirectArgsSeed((uint)instances.Count);
        }
    }
}
