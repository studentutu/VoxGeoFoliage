#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Compact editor summary for one vegetation authoring component.
    /// </summary>
    public readonly struct VegetationAuthoringSummary
    {
        public VegetationAuthoringSummary(
            int branchCount,
            Bounds treeBounds,
            int l0Triangles,
            int l1Triangles,
            int l2Triangles,
            int l3Triangles,
            int treeL3Triangles,
            int impostorTriangles)
        {
            BranchCount = branchCount;
            TreeBounds = treeBounds;
            L0Triangles = l0Triangles;
            L1Triangles = l1Triangles;
            L2Triangles = l2Triangles;
            L3Triangles = l3Triangles;
            TreeL3Triangles = treeL3Triangles;
            ImpostorTriangles = impostorTriangles;
        }

        public int BranchCount { get; }

        public Bounds TreeBounds { get; }

        public int L0Triangles { get; }

        public int L1Triangles { get; }

        public int L2Triangles { get; }

        public int L3Triangles { get; }

        public int TreeL3Triangles { get; }

        public int ImpostorTriangles { get; }

        public int GetTriangleCount(VegetationPreviewTier previewTier)
        {
            return previewTier switch
            {
                VegetationPreviewTier.L0 => L0Triangles,
                VegetationPreviewTier.L1 => L1Triangles,
                VegetationPreviewTier.L2 => L2Triangles,
                VegetationPreviewTier.L3 => L3Triangles,
                VegetationPreviewTier.TreeL3 => TreeL3Triangles,
                VegetationPreviewTier.Impostor => ImpostorTriangles,
                _ => 0
            };
        }
    }
}
