#nullable enable

using System.Collections.Generic;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Stable Phase D handoff surface containing per-slot visible instances, indirect seeds, and bounds.
    /// </summary>
    public sealed class VegetationFrameOutput
    {
        private readonly List<int> activeSlotIndices = new List<int>();
        private readonly VegetationVisibleSlotOutput[] slotOutputs;

        public VegetationFrameOutput(IReadOnlyList<VegetationDrawSlot> drawSlots)
        {
            slotOutputs = new VegetationVisibleSlotOutput[drawSlots.Count];
            for (int i = 0; i < drawSlots.Count; i++)
            {
                slotOutputs[i] = new VegetationVisibleSlotOutput(drawSlots[i]);
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
        public void AddVisibleInstance(VegetationVisibleInstance instance)
        {
            VegetationVisibleSlotOutput slotOutput = slotOutputs[instance.DrawSlotIndex];
            if (slotOutput.InstanceCount == 0)
            {
                activeSlotIndices.Add(instance.DrawSlotIndex);
            }

            slotOutput.AddInstance(instance);
        }
    }
}
