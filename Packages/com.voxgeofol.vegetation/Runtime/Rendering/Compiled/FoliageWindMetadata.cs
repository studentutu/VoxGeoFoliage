#nullable enable

using System;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Static per-packet wind metadata; animated wind strength remains shader-constant driven.
    /// </summary>
    [Serializable]
    public struct FoliageWindMetadata
    {
        [SerializeField] private float phase01;
        [SerializeField] private float trunkBendWeight;
        [SerializeField] private float branchFlutterWeight;
        [SerializeField] private float anchorHeight;

        public FoliageWindMetadata(
            float phase01,
            float trunkBendWeight,
            float branchFlutterWeight,
            float anchorHeight)
        {
            this.phase01 = Mathf.Repeat(phase01, 1f);
            this.trunkBendWeight = Mathf.Clamp01(trunkBendWeight);
            this.branchFlutterWeight = Mathf.Clamp01(branchFlutterWeight);
            this.anchorHeight = anchorHeight;
        }

        public float Phase01 => phase01;

        public float TrunkBendWeight => trunkBendWeight;

        public float BranchFlutterWeight => branchFlutterWeight;

        public float AnchorHeight => anchorHeight;

        public static FoliageWindMetadata None => new FoliageWindMetadata(0f, 0f, 0f, 0f);
    }
}
