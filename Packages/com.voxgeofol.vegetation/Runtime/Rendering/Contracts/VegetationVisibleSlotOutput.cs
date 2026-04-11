#nullable enable

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Reusable visible-instance aggregation for one exact draw slot.
    /// </summary>
    public sealed class VegetationVisibleSlotOutput
    {
        private static readonly IReadOnlyList<VegetationVisibleInstance> EmptyInstances = System.Array.Empty<VegetationVisibleInstance>();
        private readonly List<VegetationVisibleInstance> instances = new List<VegetationVisibleInstance>();
        private readonly bool captureDebugInstances;
        private VegetationIndirectInstanceData[] uploadInstances = System.Array.Empty<VegetationIndirectInstanceData>();
        private int instanceCount;
        private Vector3 visibleMin;
        private Vector3 visibleMax;
        private bool hasVisibleBounds;

        public VegetationVisibleSlotOutput(VegetationDrawSlot drawSlot, bool captureDebugInstances)
        {
            DrawSlot = drawSlot;
            this.captureDebugInstances = captureDebugInstances;
        }

        public VegetationDrawSlot DrawSlot { get; }

        public IReadOnlyList<VegetationVisibleInstance> Instances => captureDebugInstances ? instances : EmptyInstances;

        internal VegetationIndirectInstanceData[] UploadInstances => uploadInstances;

        public int InstanceCount => instanceCount;

        public Bounds VisibleBounds => hasVisibleBounds
            ? new Bounds((visibleMin + visibleMax) * 0.5f, visibleMax - visibleMin)
            : default;

        public bool HasVisibleBounds => hasVisibleBounds;

        /// <summary>
        /// [INTEGRATION] Clears the previous frame output so Phase D can rebuild visible slot data deterministically.
        /// </summary>
        public void Reset()
        {
            instanceCount = 0;
            if (captureDebugInstances)
            {
                instances.Clear();
            }

            visibleMin = default;
            visibleMax = default;
            hasVisibleBounds = false;
        }

        /// <summary>
        /// [INTEGRATION] Appends one visible instance and refreshes the exact visible-data bounds for this slot.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AddInstance(int treeIndex, in VegetationIndirectInstanceData uploadInstance, in Bounds worldBounds)
        {
            int instanceIndex = instanceCount;
            EnsureUploadCapacity(instanceIndex + 1);
            uploadInstances[instanceIndex] = uploadInstance;

            if (!hasVisibleBounds)
            {
                visibleMin = worldBounds.min;
                visibleMax = worldBounds.max;
                hasVisibleBounds = true;
            }
            else
            {
                Vector3 worldMin = worldBounds.min;
                Vector3 worldMax = worldBounds.max;
                visibleMin = Vector3.Min(visibleMin, worldMin);
                visibleMax = Vector3.Max(visibleMax, worldMax);
            }

            instanceCount++;
            if (!captureDebugInstances)
            {
                return;
            }

            instances.Add(new VegetationVisibleInstance
            {
                TreeIndex = treeIndex,
                WorldBounds = worldBounds
            });
        }

        /// <summary>
        /// [INTEGRATION] Exposes the final indirect-args seed for this slot from the current visible list.
        /// </summary>
        public VegetationIndirectArgsSeed BuildIndirectArgsSeed()
        {
            return DrawSlot.BuildIndirectArgsSeed((uint)instanceCount);
        }

        private void EnsureUploadCapacity(int requiredCount)
        {
            if (uploadInstances.Length >= requiredCount)
            {
                return;
            }

            int newCapacity = Mathf.Max(1, uploadInstances.Length);
            while (newCapacity < requiredCount)
            {
                newCapacity <<= 1;
            }

            uploadInstances = new VegetationIndirectInstanceData[newCapacity];
        }
    }
}
