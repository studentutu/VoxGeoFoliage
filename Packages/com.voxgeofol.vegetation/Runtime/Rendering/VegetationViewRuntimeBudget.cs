#nullable enable

using System;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] Per-view hard runtime limits for one prepared vegetation view.
    /// </summary>
    public readonly struct VegetationViewRuntimeBudget : IEquatable<VegetationViewRuntimeBudget>
    {
        public VegetationViewRuntimeBudget(int maxVisibleInstances, int maxExpandedBranchWorkItems, int maxApproxWorkUnits)
        {
            MaxVisibleInstances = Mathf.Max(1, maxVisibleInstances);
            MaxExpandedBranchWorkItems = Mathf.Max(1, maxExpandedBranchWorkItems);
            MaxApproxWorkUnits = Mathf.Max(1, maxApproxWorkUnits);
        }

        public int MaxVisibleInstances { get; }

        public int MaxExpandedBranchWorkItems { get; }

        public int MaxApproxWorkUnits { get; }

        public bool Equals(VegetationViewRuntimeBudget other)
        {
            return MaxVisibleInstances == other.MaxVisibleInstances &&
                   MaxExpandedBranchWorkItems == other.MaxExpandedBranchWorkItems &&
                   MaxApproxWorkUnits == other.MaxApproxWorkUnits;
        }

        public override bool Equals(object? obj)
        {
            return obj is VegetationViewRuntimeBudget other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = MaxVisibleInstances;
                hash = (hash * 397) ^ MaxExpandedBranchWorkItems;
                return (hash * 397) ^ MaxApproxWorkUnits;
            }
        }
    }
}
