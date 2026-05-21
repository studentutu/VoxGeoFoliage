#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Shared runtime math helpers for compiled packet bounds, culling, and shader payload packing.
    /// </summary>
    public static class VegetationRuntimeMathUtility
    {
        /// <summary>
        /// [INTEGRATION] Transforms a local AABB into world space for compiler and render-world packet bounds.
        /// </summary>
        public static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            // Range: input bounds are authoritative authoring or mesh-local bounds. Condition: matrix is the final local-to-world transform for the current packet. Output: exact world AABB used by culling and diagnostics.
            Vector3 extents = bounds.extents;
            Vector3 worldCenter = matrix.MultiplyPoint3x4(bounds.center);
            Vector3 worldExtents = new Vector3(
                Mathf.Abs(matrix.m00) * extents.x + Mathf.Abs(matrix.m01) * extents.y + Mathf.Abs(matrix.m02) * extents.z,
                Mathf.Abs(matrix.m10) * extents.x + Mathf.Abs(matrix.m11) * extents.y + Mathf.Abs(matrix.m12) * extents.z,
                Mathf.Abs(matrix.m20) * extents.x + Mathf.Abs(matrix.m21) * extents.y + Mathf.Abs(matrix.m22) * extents.z);
            return new Bounds(worldCenter, worldExtents * 2f);
        }

        /// <summary>
        /// [INTEGRATION] Computes the conservative sphere radius used by packet culling and LOD selection.
        /// </summary>
        public static float ComputeBoundingSphereRadius(Bounds bounds, Matrix4x4 matrix)
        {
            Bounds worldBounds = TransformBounds(bounds, matrix);
            return worldBounds.extents.magnitude;
        }

        /// <summary>
        /// [INTEGRATION] Computes sphere-surface distance for coarse packet LOD selection.
        /// </summary>
        public static float ComputeSphereSurfaceDistance(Vector3 cameraWorldPosition, Vector3 sphereCenterWorld, float sphereRadiusWorld)
        {
            // Range: accepts any camera position plus a conservative world-space sphere. Condition: the sphere radius must already account for non-uniform scale. Output: non-negative sphere-surface distance consumed by packet selection.
            return Mathf.Max(0f, Vector3.Distance(cameraWorldPosition, sphereCenterWorld) - sphereRadiusWorld);
        }

        /// <summary>
        /// [INTEGRATION] Packs the authoring tint into one uint so matches the runtime RSUV transport contract.
        /// </summary>
        public static uint PackColorToUint(Color color)
        {
            Color32 color32 = color;
            return (uint)(color32.r |
                         (color32.g << 8) |
                         (color32.b << 16) |
                         (color32.a << 24));
        }
    }
}
