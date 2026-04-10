#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Stable exact mesh/material slot consumed by Phase D outputs and Phase E rendering.
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

        /// <summary>
        /// [INTEGRATION] Builds the Phase E indirect-args seed from the visible count produced in Phase D.
        /// </summary>
        public VegetationIndirectArgsSeed BuildIndirectArgsSeed(uint instanceCount)
        {
            return new VegetationIndirectArgsSeed
            {
                DrawSlotIndex = SlotIndex,
                IndexCountPerInstance = IndexCountPerInstance,
                InstanceCount = instanceCount,
                StartIndexLocation = StartIndexLocation,
                BaseVertexLocation = BaseVertexLocation,
                StartInstanceLocation = 0u
            };
        }
    }
}
