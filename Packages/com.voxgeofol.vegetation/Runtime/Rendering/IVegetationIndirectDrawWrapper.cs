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
        void DrawMeshInstancedIndirect(
            Mesh mesh,
            Material material,
            GraphicsBuffer argsBuffer,
            int argsOffset);
    }
}
