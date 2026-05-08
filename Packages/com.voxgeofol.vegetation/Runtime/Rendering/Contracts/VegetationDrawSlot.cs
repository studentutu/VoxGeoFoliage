#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Stable exact runtime registry identity defined by mesh, material, and material kind.
    /// One draw slot owns one slot index, one indirect-args record, and one potential final indirect submission in a pass.
    /// It is not the same thing as a packed visible instance.
    /// </summary>
    public sealed class VegetationDrawSlot
    {
        public VegetationDrawSlot(int slotIndex, Mesh mesh, Material material, VegetationRenderMaterialKind materialKind, string debugLabel)
        {
            SlotIndex = slotIndex;
            Mesh = mesh;
            Material = material;
            MaterialKind = materialKind;
            DebugLabel = debugLabel;
            IndexCountPerInstance = (uint)mesh.GetIndexCount(0);
            StartIndexLocation = (uint)mesh.GetIndexStart(0);
            BaseVertexLocation = checked((int)mesh.GetBaseVertex(0));
            LocalBounds = mesh.bounds;
        }

        public int SlotIndex { get; }

        public Mesh Mesh { get; }

        public Material Material { get; }

        public VegetationRenderMaterialKind MaterialKind { get; }

        public string DebugLabel { get; }

        public uint IndexCountPerInstance { get; }

        public uint StartIndexLocation { get; }

        public int BaseVertexLocation { get; }

        public Bounds LocalBounds { get; }

    }
}
