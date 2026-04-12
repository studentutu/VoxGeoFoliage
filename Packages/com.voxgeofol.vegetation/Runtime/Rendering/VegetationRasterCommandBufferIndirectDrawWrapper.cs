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

        public void DrawMeshInstancedIndirect(
            Mesh mesh,
            Material material,
            GraphicsBuffer argsBuffer,
            int argsOffset)
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

            commandBuffer.DrawMeshInstancedIndirect(mesh, 0, material, 0, argsBuffer, argsOffset);
        }
    }
}
