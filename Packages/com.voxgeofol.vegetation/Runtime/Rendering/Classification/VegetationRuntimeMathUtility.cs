#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Shared runtime math helpers for Phase D registration, classification, and visible-bounds rebuilds.
    /// </summary>
    public static class VegetationRuntimeMathUtility
    {
        /// <summary>
        /// [INTEGRATION] Transforms a local AABB into world space for runtime registration and visible-bounds output.
        /// </summary>
        public static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            // Range: input bounds are authoritative authoring or mesh-local bounds. Condition: matrix is the final local-to-world transform for the current draw. Output: exact world AABB used by Phase D visibility and bounds rebuilds.
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            Vector3[] corners =
            {
                center + new Vector3(-extents.x, -extents.y, -extents.z),
                center + new Vector3(-extents.x, -extents.y, extents.z),
                center + new Vector3(-extents.x, extents.y, -extents.z),
                center + new Vector3(-extents.x, extents.y, extents.z),
                center + new Vector3(extents.x, -extents.y, -extents.z),
                center + new Vector3(extents.x, -extents.y, extents.z),
                center + new Vector3(extents.x, extents.y, -extents.z),
                center + new Vector3(extents.x, extents.y, extents.z)
            };

            Vector3 transformedCorner = matrix.MultiplyPoint3x4(corners[0]);
            Bounds transformedBounds = new Bounds(transformedCorner, Vector3.zero);
            for (int i = 1; i < corners.Length; i++)
            {
                transformedBounds.Encapsulate(matrix.MultiplyPoint3x4(corners[i]));
            }

            return transformedBounds;
        }

        /// <summary>
        /// [INTEGRATION] Computes the conservative sphere radius used by Phase D tree and branch distance classification.
        /// </summary>
        public static float ComputeBoundingSphereRadius(Bounds bounds, Matrix4x4 matrix)
        {
            Bounds worldBounds = TransformBounds(bounds, matrix);
            return worldBounds.extents.magnitude;
        }

        /// <summary>
        /// [INTEGRATION] Computes the sphere-surface distance required by the Milestone 1 LOD contract.
        /// </summary>
        public static float ComputeSphereSurfaceDistance(Vector3 cameraWorldPosition, Vector3 sphereCenterWorld, float sphereRadiusWorld)
        {
            // Range: accepts any camera position plus a conservative world-space sphere. Condition: the sphere radius must already account for non-uniform tree/branch scale. Output: non-negative sphere-surface distance consumed by tree and branch tier classification.
            return Mathf.Max(0f, Vector3.Distance(cameraWorldPosition, sphereCenterWorld) - sphereRadiusWorld);
        }

        /// <summary>
        /// [INTEGRATION] Packs the authoring tint into one uint so Phase D matches the runtime RSUV transport contract.
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
