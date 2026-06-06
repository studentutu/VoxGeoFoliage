#nullable enable

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] RenderGraph-visible vegetation preparation contract. This first slice records a compute pass and dependency buffer that all vegetation raster passes consume.
    /// </summary>
    internal static class VegetationRenderGraphPreparationContract
    {
        public const string DefaultComputeResourceName = "VegetationRenderGraphPrepare";
        private const string KernelName = "WriteVegetationFrameContract";
        private const int ContractUIntCount = 8;
        private const uint ContractMagic = 0x56474652u;

        private static readonly int ContractBufferId = Shader.PropertyToID("_VegetationRenderGraphContract");
        private static readonly int FrameIndexId = Shader.PropertyToID("_VegetationRenderGraphFrameIndex");
        private static readonly int PassModeId = Shader.PropertyToID("_VegetationRenderGraphPassMode");
        private static readonly int InstanceCountId = Shader.PropertyToID("_VegetationRenderGraphInstanceCount");
        private static readonly int PacketCountId = Shader.PropertyToID("_VegetationRenderGraphPacketCount");
        private static readonly int ActiveGroupCountId = Shader.PropertyToID("_VegetationRenderGraphActiveGroupCount");
        private static readonly int FrustumMaskId = Shader.PropertyToID("_VegetationRenderGraphFrustumMask");
        private static readonly int GraphVersionId = Shader.PropertyToID("_VegetationRenderGraphGraphVersion");
        private static readonly int ContractMagicId = Shader.PropertyToID("_VegetationRenderGraphContractMagic");
        private static readonly BaseRenderFunc<PassData, ComputeGraphContext> ExecuteFunc = Execute;

        public static BufferHandle Record(
            RenderGraph renderGraph,
            ComputeShader computeShader,
            VegetationRenderPassMode passMode,
            in VegetationRenderGraphFrame frame,
            BufferHandle instanceBuffer,
            BufferHandle argsBuffer)
        {
            using (var builder = renderGraph.AddComputePass<PassData>(
                       "Vegetation RenderGraph Prepare",
                       out PassData passData))
            {
                passData.Shader = computeShader;
                passData.KernelIndex = computeShader.FindKernel(KernelName);
                passData.World = VegetationRenderWorld.Shared;
                passData.Frame = frame;
                passData.InstanceBuffer = instanceBuffer;
                passData.ArgsBuffer = argsBuffer;
                passData.Contract = renderGraph.CreateBuffer(new BufferDesc(
                    ContractUIntCount,
                    sizeof(uint),
                    GraphicsBuffer.Target.Structured)
                {
                    name = "Vegetation RenderGraph Contract"
                });
                passData.FrameIndex = Time.renderedFrameCount;
                passData.PassMode = (int)passMode;
                passData.GraphVersion = frame.GraphVersion;

                builder.UseBuffer(instanceBuffer, AccessFlags.Write);
                builder.UseBuffer(argsBuffer, AccessFlags.Write);
                builder.UseBuffer(passData.Contract, AccessFlags.Write);
                builder.SetRenderFunc(ExecuteFunc);
                return passData.Contract;
            }
        }

        private static void Execute(PassData data, ComputeGraphContext context)
        {
            data.World.CompleteRenderGraphPreparation(
                data.Frame,
                context.cmd,
                data.InstanceBuffer,
                data.ArgsBuffer);

            context.cmd.SetComputeIntParam(data.Shader, ContractMagicId, unchecked((int)ContractMagic));
            context.cmd.SetComputeIntParam(data.Shader, FrameIndexId, data.FrameIndex);
            context.cmd.SetComputeIntParam(data.Shader, PassModeId, data.PassMode);
            context.cmd.SetComputeIntParam(data.Shader, InstanceCountId, data.Frame.InstanceCount);
            context.cmd.SetComputeIntParam(data.Shader, PacketCountId, data.Frame.PacketCount);
            context.cmd.SetComputeIntParam(data.Shader, ActiveGroupCountId, data.Frame.ActiveGroupCount);
            context.cmd.SetComputeIntParam(data.Shader, FrustumMaskId, data.Frame.FrustumMask);
            context.cmd.SetComputeIntParam(data.Shader, GraphVersionId, data.GraphVersion);
            context.cmd.SetComputeBufferParam(data.Shader, data.KernelIndex, ContractBufferId, data.Contract);
            context.cmd.DispatchCompute(data.Shader, data.KernelIndex, 1, 1, 1);
        }

        private sealed class PassData
        {
            public ComputeShader Shader = null!;
            public int KernelIndex;
            public VegetationRenderWorld World = null!;
            public VegetationRenderGraphFrame Frame;
            public BufferHandle InstanceBuffer;
            public BufferHandle ArgsBuffer;
            public BufferHandle Contract;
            public int FrameIndex;
            public int PassMode;
            public int GraphVersion;
        }
    }
}
