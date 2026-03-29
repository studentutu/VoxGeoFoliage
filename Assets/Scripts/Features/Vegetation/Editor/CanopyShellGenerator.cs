#nullable enable

using System;
using UnityEditor;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Editor-only canopy shell baker for reusable branch prototypes.
    /// </summary>
    public static class CanopyShellGenerator
    {
        private const float WoodReductionRatio = 0.5f;

        /// <summary>
        /// [INTEGRATION] Called from editor tooling to populate shell meshes on one branch prototype.
        /// </summary>
        public static void BakeCanopyShells(BranchPrototypeSO prototype, ShellBakeSettings? settings = null)
        {
            // Range: requires readable foliage and wood meshes plus valid target budgets. Condition: L0/L1/L2 canopy shells are baked from foliage while L1/L2 wood attachments are simplified from the source wood mesh. Output: the prototype receives shellL0Mesh/shellL1Mesh/shellL1WoodMesh/shellL2Mesh/shellL2WoodMesh.
            if (prototype == null)
            {
                throw new ArgumentNullException(nameof(prototype));
            }

            Mesh foliageMesh = prototype.FoliageMesh ?? throw new InvalidOperationException($"{prototype.name} is missing foliageMesh.");
            Mesh woodMesh = prototype.WoodMesh ?? throw new InvalidOperationException($"{prototype.name} is missing woodMesh.");
            if (!foliageMesh.isReadable)
            {
                throw new InvalidOperationException($"{prototype.name} foliageMesh must be readable before shell baking.");
            }

            if (!woodMesh.isReadable)
            {
                throw new InvalidOperationException($"{prototype.name} woodMesh must be readable before shell baking.");
            }

            ShellBakeSettings activeSettings = settings ?? new ShellBakeSettings();
            int l0Target = Mathf.Max(1, Mathf.Min(activeSettings.TargetTrianglesL0, prototype.TriangleBudgetShellL0));
            Mesh shellL0Mesh = BuildShellMesh(foliageMesh, activeSettings.VoxelResolutionL0, l0Target, 0);

            int l1Target = Mathf.Max(
                1,
                Math.Min(
                    activeSettings.TargetTrianglesL1,
                    Math.Min(prototype.TriangleBudgetShellL1, GetTriangleCount(shellL0Mesh) - 1)));
            Mesh shellL1Mesh = BuildShellMesh(foliageMesh, activeSettings.VoxelResolutionL1, l1Target, 1);

            int l2Target = Mathf.Max(
                1,
                Math.Min(
                    activeSettings.TargetTrianglesL2,
                    Math.Min(prototype.TriangleBudgetShellL2, GetTriangleCount(shellL1Mesh) - 1)));
            Mesh shellL2Mesh = BuildShellMesh(foliageMesh, activeSettings.VoxelResolutionL2, l2Target, 2);

            Mesh shellL1WoodMesh = BuildWoodAttachmentMesh(woodMesh, GetReducedTriangleTarget(woodMesh), 1);
            Mesh shellL2WoodMesh = BuildWoodAttachmentMesh(shellL1WoodMesh, GetReducedTriangleTarget(shellL1WoodMesh), 2);

            SerializedObject serializedPrototype = new SerializedObject(prototype);
            serializedPrototype.FindProperty("shellL0Mesh").objectReferenceValue =
                GeneratedMeshAssetUtility.PersistGeneratedMesh(prototype, $"{prototype.name}_ShellL0", shellL0Mesh);
            serializedPrototype.FindProperty("shellL1Mesh").objectReferenceValue =
                GeneratedMeshAssetUtility.PersistGeneratedMesh(prototype, $"{prototype.name}_ShellL1", shellL1Mesh);
            serializedPrototype.FindProperty("shellL1WoodMesh").objectReferenceValue =
                GeneratedMeshAssetUtility.PersistGeneratedMesh(prototype, $"{prototype.name}_ShellL1Wood", shellL1WoodMesh);
            serializedPrototype.FindProperty("shellL2Mesh").objectReferenceValue =
                GeneratedMeshAssetUtility.PersistGeneratedMesh(prototype, $"{prototype.name}_ShellL2", shellL2Mesh);
            serializedPrototype.FindProperty("shellL2WoodMesh").objectReferenceValue =
                GeneratedMeshAssetUtility.PersistGeneratedMesh(prototype, $"{prototype.name}_ShellL2Wood", shellL2WoodMesh);
            serializedPrototype.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prototype);
        }

        private static Mesh BuildShellMesh(Mesh foliageMesh, int resolution, int targetTriangleCount, int dilationIterations)
        {
            VoxelGrid voxelGrid = Voxelizer.VoxelizeSolid(foliageMesh, resolution);
            if (voxelGrid.OccupiedCount == 0)
            {
                throw new InvalidOperationException($"{foliageMesh.name} produced no occupied voxels at resolution {resolution}.");
            }

            VoxelGrid buildGrid = dilationIterations > 0
                ? voxelGrid.CreateDilated(dilationIterations)
                : voxelGrid;

            Mesh surfaceMesh = Voxelizer.CreateSurfaceMesh(buildGrid);
            if (GetTriangleCount(surfaceMesh) == 0)
            {
                throw new InvalidOperationException($"{foliageMesh.name} produced an empty shell surface at resolution {resolution}.");
            }

            Mesh simplifiedMesh = MeshSimplifier.Simplify(surfaceMesh, targetTriangleCount);
            UnityEngine.Object.DestroyImmediate(surfaceMesh);
            simplifiedMesh.name = $"{foliageMesh.name}_Shell";
            return simplifiedMesh;
        }

        private static Mesh BuildWoodAttachmentMesh(Mesh sourceWoodMesh, int targetTriangleCount, int shellLevel)
        {
            Mesh simplifiedMesh = MeshSimplifier.Simplify(sourceWoodMesh, targetTriangleCount);
            simplifiedMesh.name = $"{sourceWoodMesh.name}_ShellL{shellLevel}Wood";
            return simplifiedMesh;
        }

        private static int GetReducedTriangleTarget(Mesh sourceMesh)
        {
            int sourceTriangleCount = GetTriangleCount(sourceMesh);
            if (sourceTriangleCount <= 1)
            {
                return 1;
            }

            return Mathf.Clamp(Mathf.CeilToInt(sourceTriangleCount * WoodReductionRatio), 1, sourceTriangleCount - 1);
        }

        private static int GetTriangleCount(Mesh mesh)
        {
            return mesh.triangles.Length / 3;
        }
    }
}
