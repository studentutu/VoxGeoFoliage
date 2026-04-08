#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Runtime-side flattened prototype payload for one reusable branch module.
    /// </summary>
    public struct VegetationBranchPrototypeRuntime
    {
        public int WoodDrawSlotL0;
        public int WoodDrawSlotL1;
        public int WoodDrawSlotL2;
        public int WoodDrawSlotL3;
        public int FoliageDrawSlotL0;
        public int ShellNodeStartIndexL1;
        public int ShellNodeCountL1;
        public int ShellNodeStartIndexL2;
        public int ShellNodeCountL2;
        public int ShellNodeStartIndexL3;
        public int ShellNodeCountL3;
        public uint PackedLeafTint;
        public Vector3 LocalBoundsCenter;
        public Vector3 LocalBoundsExtents;
    }
}
