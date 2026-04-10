#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Debug-facing snapshot of one uploaded indirect draw batch.
    /// </summary>
    public struct VegetationIndirectDrawBatchSnapshot
    {
        public int SlotIndex;
        public string DebugLabel;
        public VegetationRenderMaterialKind MaterialKind;
        public int InstanceCount;
        public Bounds WorldBounds;
    }
}
