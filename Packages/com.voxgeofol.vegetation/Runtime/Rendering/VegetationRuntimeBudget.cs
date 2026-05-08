#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] Per-container runtime budgets split by color/shadow prepared-view ownership.
    /// </summary>
    public readonly struct VegetationRuntimeBudget
    {
        public VegetationRuntimeBudget(
            VegetationViewRuntimeBudget colorBudget,
            VegetationViewRuntimeBudget shadowBudget,
            int maxRegisteredDrawSlots)
        {
            ColorBudget = colorBudget;
            ShadowBudget = shadowBudget;
            MaxRegisteredDrawSlots = Mathf.Max(1, maxRegisteredDrawSlots);
        }

        public VegetationViewRuntimeBudget ColorBudget { get; }

        public VegetationViewRuntimeBudget ShadowBudget { get; }

        public int MaxRegisteredDrawSlots { get; }
    }
}
