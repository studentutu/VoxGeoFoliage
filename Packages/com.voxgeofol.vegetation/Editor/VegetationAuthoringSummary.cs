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
            int r0Triangles,
            int r1Triangles,
            int r2Triangles,
            int r3Triangles,
            int shellL0OnlyTriangles,
            int shellL1OnlyTriangles,
            int shellL2OnlyTriangles)
        {
            BranchCount = branchCount;
            TreeBounds = treeBounds;
            R0Triangles = r0Triangles;
            R1Triangles = r1Triangles;
            R2Triangles = r2Triangles;
            R3Triangles = r3Triangles;
            ShellL0OnlyTriangles = shellL0OnlyTriangles;
            ShellL1OnlyTriangles = shellL1OnlyTriangles;
            ShellL2OnlyTriangles = shellL2OnlyTriangles;
        }

        public int BranchCount { get; }

        public Bounds TreeBounds { get; }

        public int R0Triangles { get; }

        public int R1Triangles { get; }

        public int R2Triangles { get; }

        public int R3Triangles { get; }

        public int ShellL0OnlyTriangles { get; }

        public int ShellL1OnlyTriangles { get; }

        public int ShellL2OnlyTriangles { get; }

        public int GetTriangleCount(VegetationPreviewTier previewTier)
        {
            return previewTier switch
            {
                VegetationPreviewTier.R0Full => R0Triangles,
                VegetationPreviewTier.R1ShellL1 => R1Triangles,
                VegetationPreviewTier.R2ShellL2 => R2Triangles,
                VegetationPreviewTier.R3Impostor => R3Triangles,
                VegetationPreviewTier.ShellL0Only => ShellL0OnlyTriangles,
                VegetationPreviewTier.ShellL1Only => ShellL1OnlyTriangles,
                VegetationPreviewTier.ShellL2Only => ShellL2OnlyTriangles,
                _ => 0
            };
        }
    }
}
