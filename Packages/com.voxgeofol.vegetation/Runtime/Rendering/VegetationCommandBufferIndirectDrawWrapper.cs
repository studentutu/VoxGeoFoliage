#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] Issues indirect vegetation draws through the compatibility <see cref="CommandBuffer"/> path without render-loop closures.
    /// </summary>
    internal sealed class VegetationCommandBufferIndirectDrawWrapper : IVegetationIndirectDrawWrapper
    {
        private CommandBuffer? commandBuffer;

        public void RefreshCommandBuffer(CommandBuffer targetCommandBuffer)
        {
            commandBuffer = targetCommandBuffer;
        }

        public void ClearCommandBuffer()
        {
            commandBuffer = null;
        }

        public void SetGlobalBuffer(int nameId, GraphicsBuffer value)
        {
            commandBuffer?.SetGlobalBuffer(nameId, value);
        }

        public void SetGlobalBuffer(int nameId, ComputeBuffer value)
        {
            commandBuffer?.SetGlobalBuffer(nameId, value);
        }

        public void SetGlobalInt(int nameId, int value)
        {
            commandBuffer?.SetGlobalInt(nameId, value);
        }

        public void DrawMeshInstancedIndirect(
            Mesh mesh,
            Material material,
            GraphicsBuffer argsBuffer,
            int argsOffset,
            int shaderPass)
        {
            if (mesh == null)
            {
                return;
            }

            if (material == null)
            {
                return;
            }

            if (argsBuffer == null)
            {
                return;
            }

            if (commandBuffer == null)
            {
               return;
            }

            commandBuffer.DrawMeshInstancedIndirect(mesh, 0, material, shaderPass, argsBuffer, argsOffset);
        }
    }
}
