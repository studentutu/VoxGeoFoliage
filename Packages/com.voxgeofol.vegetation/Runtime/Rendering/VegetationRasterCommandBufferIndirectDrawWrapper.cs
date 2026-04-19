#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] Issues indirect vegetation draws through the render-graph <see cref="IRasterCommandBuffer"/> path without render-loop closures.
    /// </summary>
    internal sealed class VegetationRasterCommandBufferIndirectDrawWrapper : IVegetationIndirectDrawWrapper
    {
        private IRasterCommandBuffer? commandBuffer;

        public void RefreshCommandBuffer(IRasterCommandBuffer targetCommandBuffer)
        {
            commandBuffer = targetCommandBuffer;
        }

        public void ClearCommandBuffer()
        {
            commandBuffer = null;
        }

        public void SetGlobalBuffer(int propertyId, GraphicsBuffer buffer)
        {
            if (commandBuffer == null || buffer == null)
            {
                return;
            }

            commandBuffer.SetGlobalBuffer(propertyId, buffer);
        }

        public void SetGlobalBuffer(int propertyId, ComputeBuffer buffer)
        {
            if (commandBuffer == null || buffer == null)
            {
                return;
            }

            commandBuffer.SetGlobalBuffer(propertyId, buffer);
        }

        public void SetGlobalInt(int propertyId, int value)
        {
            if (commandBuffer == null)
            {
                return;
            }

            commandBuffer.SetGlobalInt(propertyId, value);
        }

        public void DrawMeshInstancedIndirect(
            Mesh mesh,
            Material material,
            GraphicsBuffer argsBuffer,
            int argsOffset,
            int shaderPass,
            MaterialPropertyBlock? propertyBlock)
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

            commandBuffer.DrawMeshInstancedIndirect(mesh, 0, material, shaderPass, argsBuffer, argsOffset, propertyBlock);
        }
    }
}
