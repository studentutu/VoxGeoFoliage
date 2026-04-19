#nullable enable

using UnityEngine;
using UnityEngine.Rendering;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] Minimal indirect draw wrapper used so the renderer can issue slot draws without per-call delegate captures.
    /// </summary>
    internal interface IVegetationIndirectDrawWrapper
    {
        void SetGlobalBuffer(int propertyId, GraphicsBuffer buffer);

        void SetGlobalBuffer(int propertyId, ComputeBuffer buffer);

        void SetGlobalInt(int propertyId, int value);

        void DrawMeshInstancedIndirect(
            Mesh mesh,
            Material material,
            GraphicsBuffer argsBuffer,
            int argsOffset,
            int shaderPass,
            MaterialPropertyBlock? propertyBlock);
    }
}
