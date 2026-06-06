#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] Prepared vegetation GPU frame imported into URP RenderGraph passes.
    /// </summary>
    internal readonly struct VegetationRenderGraphFrame
    {
        public VegetationRenderGraphFrame(
            GraphicsBuffer instanceBuffer,
            GraphicsBuffer argsBuffer,
            int instanceCount,
            int packetCount,
            int activeGroupCount,
            int frustumMask,
            int graphVersion,
            int preparationSlotIndex = -1,
            int preparationVersion = 0)
        {
            InstanceBuffer = instanceBuffer;
            ArgsBuffer = argsBuffer;
            InstanceCount = instanceCount;
            PacketCount = packetCount;
            ActiveGroupCount = activeGroupCount;
            FrustumMask = frustumMask;
            GraphVersion = graphVersion;
            PreparationSlotIndex = preparationSlotIndex;
            PreparationVersion = preparationVersion;
        }

        public GraphicsBuffer InstanceBuffer { get; }

        public GraphicsBuffer ArgsBuffer { get; }

        public int InstanceCount { get; }

        public int PacketCount { get; }

        public int ActiveGroupCount { get; }

        public int FrustumMask { get; }

        public int GraphVersion { get; }

        public int PreparationSlotIndex { get; }

        public int PreparationVersion { get; }
    }
}
