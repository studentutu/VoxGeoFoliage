#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Stable Phase D handoff surface containing per-slot visible instances, indirect seeds, and bounds.
    /// </summary>
    public sealed class VegetationFrameOutput
    {
        private readonly List<int> activeSlotIndices = new List<int>();
        private readonly VegetationVisibleSlotOutput[] slotOutputs;

        public VegetationFrameOutput(IReadOnlyList<VegetationDrawSlot> drawSlots, bool captureDebugInstances)
        {
            slotOutputs = new VegetationVisibleSlotOutput[drawSlots.Count];
            for (int i = 0; i < drawSlots.Count; i++)
            {
                slotOutputs[i] = new VegetationVisibleSlotOutput(drawSlots[i], captureDebugInstances);
            }
        }

        public IReadOnlyList<int> ActiveSlotIndices => activeSlotIndices;

        public IReadOnlyList<VegetationVisibleSlotOutput> SlotOutputs => slotOutputs;

        /// <summary>
        /// [INTEGRATION] Clears every per-slot output before a new Phase D decode pass rebuilds them.
        /// </summary>
        public void Reset()
        {
            activeSlotIndices.Clear();
            for (int i = 0; i < slotOutputs.Length; i++)
            {
                slotOutputs[i].Reset();
            }
        }

        /// <summary>
        /// [INTEGRATION] Appends one visible instance into the exact draw slot selected by the runtime decision path.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AddVisibleInstance(
            int drawSlotIndex,
            int treeIndex,
            in VegetationIndirectInstanceData uploadInstance,
            in Bounds worldBounds)
        {
            VegetationVisibleSlotOutput slotOutput = slotOutputs[drawSlotIndex];
            if (slotOutput.InstanceCount == 0)
            {
                activeSlotIndices.Add(drawSlotIndex);
            }

            slotOutput.AddInstance(treeIndex, in uploadInstance, in worldBounds);
        }
    }
}
