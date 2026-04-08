#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using MeshVoxelizerProject;
using UnityEditor;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Editor-only canopy shell baker that persists canonical L0 plus compact L1/L2 hierarchies.
    /// </summary>
    public static class CanopyShellGenerator
    {
        private const int WoodResolutionFallbackStep = 2;
        private static readonly int[] MeshLodFallbackLimits = { 2, 4, 6 };

        /// <summary>
        /// [INTEGRATION] Called from editor tooling to populate hierarchical shell data on one branch prototype.
        /// </summary>
        public static void BakeCanopyShells(BranchPrototypeSO prototype, ShellBakeSettings? settings = null)
        {
            // Range: requires readable foliage and wood meshes plus valid shell budgets. Condition: builds canonical L0 plus compact L1/L2 hierarchies first, then only applies optional mesh-only fallback on the already generated tier meshes. Output: the prototype receives shellNodesL0/shellNodesL1/shellNodesL2 plus refreshed simplified wood attachments.
            if (prototype == null)
            {
                throw new ArgumentNullException(nameof(prototype));
            }

            Mesh foliageMesh = prototype.FoliageMesh ??
                               throw new InvalidOperationException($"{prototype.name} is missing foliageMesh.");
            Mesh woodMesh = prototype.WoodMesh ??
                            throw new InvalidOperationException($"{prototype.name} is missing woodMesh.");
            if (!foliageMesh.isReadable)
            {
                throw new InvalidOperationException(
                    $"{prototype.name} foliageMesh must be readable before shell baking.");
            }

            if (!woodMesh.isReadable)
            {
                throw new InvalidOperationException($"{prototype.name} woodMesh must be readable before shell baking.");
            }

            ShellBakeSettings activeSettings = settings ?? prototype.ShellBakeSettings;
            GeneratedMeshSimplificationUtility.GeneratedMeshCandidate? shellL1WoodCandidate = null;
            GeneratedMeshSimplificationUtility.GeneratedMeshCandidate? shellL2WoodCandidate = null;
            List<MeshVoxelizerHierarchyNode[]> temporaryHierarchies = new List<MeshVoxelizerHierarchyNode[]>();
            List<Mesh[]> temporaryFallbackLevels = new List<Mesh[]>();

            try
            {
                MeshVoxelizerHierarchyBuilder.BuildHierarchies(
                    foliageMesh,
                    activeSettings.VoxelResolutionL0,
                    activeSettings.VoxelResolutionL1,
                    activeSettings.VoxelResolutionL2,
                    activeSettings.SkipL0Reduction ? VoxelSystem.CpuVoxelSurfaceBuildOptions.Raw : VoxelSystem.CpuVoxelSurfaceBuildOptions.Reduced,
                    activeSettings.SkipReduction ? VoxelSystem.CpuVoxelSurfaceBuildOptions.Raw : VoxelSystem.CpuVoxelSurfaceBuildOptions.Reduced,
                    activeSettings.SkipReduction ? VoxelSystem.CpuVoxelSurfaceBuildOptions.Raw : VoxelSystem.CpuVoxelSurfaceBuildOptions.Reduced,
                    out MeshVoxelizerHierarchyNode[] hierarchyL0,
                    out MeshVoxelizerHierarchyNode[] hierarchyL1,
                    out MeshVoxelizerHierarchyNode[] hierarchyL2,
                    activeSettings.MaxOctreeDepth,
                    activeSettings.MinimumSurfaceVoxelCountToSplit);
                temporaryHierarchies.Add(hierarchyL0);
                temporaryHierarchies.Add(hierarchyL1);
                temporaryHierarchies.Add(hierarchyL2);

                ShellLevelSelection l0Selection = CreateShellLevelSelection(
                    hierarchyL0,
                    0,
                    $"canonical L0 voxel resolution {activeSettings.VoxelResolutionL0}");
                if (l0Selection.TriangleCount > prototype.TriangleBudgetShellL0)
                {
                    Debug.LogError(
                        $"{prototype.name} shell L0 leaf-frontier triangle count {l0Selection.TriangleCount} exceeds budget {prototype.TriangleBudgetShellL0}. Final source: {l0Selection.SourceDescription}. L0 fallback is not allowed.");
                }

                ShellLevelSelection l1Selection = SelectShellLevelSelection(
                    prototype,
                    activeSettings,
                    hierarchyL1,
                    1,
                    prototype.TriangleBudgetShellL1,
                    $"compact L1 voxel resolution {activeSettings.VoxelResolutionL1}",
                    temporaryFallbackLevels);

                ShellLevelSelection l2Selection = SelectShellLevelSelection(
                    prototype,
                    activeSettings,
                    hierarchyL2,
                    2,
                    prototype.TriangleBudgetShellL2,
                    $"compact L2 voxel resolution {activeSettings.VoxelResolutionL2}",
                    temporaryFallbackLevels);

                int sourceWoodTriangles = GeneratedMeshSimplificationUtility.GetTriangleCount(woodMesh);
                shellL1WoodCandidate = GeneratedMeshSimplificationUtility.SelectBestVoxelMeshCandidate(
                    woodMesh,
                    activeSettings.WoodVoxelResolutionL1,
                    activeSettings.WoodVoxelResolutionL2,
                    WoodResolutionFallbackStep,
                    sourceWoodTriangles,
                    activeSettings.SkipReduction,
                    activeSettings.SkipSimplifyFallback,
                    $"{prototype.name}_ShellL1Wood");
                if (shellL1WoodCandidate.TriangleCount > sourceWoodTriangles)
                {
                    string fallbackState = activeSettings.SkipSimplifyFallback ? "skipped" : "exhausted";
                    Debug.LogError(
                        $"{prototype.name} shellL1WoodMesh triangle count {shellL1WoodCandidate.TriangleCount} exceeds source wood triangle count {sourceWoodTriangles}. Final source: {shellL1WoodCandidate.SourceDescription}. Simplify fallback {fallbackState}.");
                }

                shellL2WoodCandidate = GeneratedMeshSimplificationUtility.SelectBestVoxelMeshCandidate(
                    woodMesh,
                    activeSettings.WoodVoxelResolutionL2,
                    2,
                    WoodResolutionFallbackStep,
                    shellL1WoodCandidate.TriangleCount,
                    activeSettings.SkipReduction,
                    activeSettings.SkipSimplifyFallback,
                    $"{prototype.name}_ShellL2Wood");
                if (shellL2WoodCandidate.TriangleCount > shellL1WoodCandidate.TriangleCount)
                {
                    string fallbackState = activeSettings.SkipSimplifyFallback ? "skipped" : "exhausted";
                    Debug.LogError(
                        $"{prototype.name} shellL2WoodMesh triangle count {shellL2WoodCandidate.TriangleCount} exceeds shellL1WoodMesh triangle count {shellL1WoodCandidate.TriangleCount}. Final source: {shellL2WoodCandidate.SourceDescription}. Simplify fallback {fallbackState}.");
                }

                BranchShellNode[] persistedL0 = PersistShellNodes(
                    prototype,
                    hierarchyL0,
                    l0Selection.Meshes,
                    0,
                    $"{prototype.name}_ShellNodeL0");
                BranchShellNode[] persistedL1 = PersistShellNodes(
                    prototype,
                    hierarchyL1,
                    l1Selection.Meshes,
                    1,
                    $"{prototype.name}_ShellNodeL1");
                BranchShellNode[] persistedL2 = PersistShellNodes(
                    prototype,
                    hierarchyL2,
                    l2Selection.Meshes,
                    2,
                    $"{prototype.name}_ShellNodeL2");

                SetPrivateField(
                    prototype,
                    "shellL1WoodMesh",
                    GeneratedMeshAssetUtility.PersistGeneratedMesh(
                        prototype,
                        $"{prototype.name}_ShellL1Wood",
                        shellL1WoodCandidate.Mesh,
                        prototype.GeneratedCanopyShellsRelativeFolder));
                SetPrivateField(
                    prototype,
                    "shellL2WoodMesh",
                    GeneratedMeshAssetUtility.PersistGeneratedMesh(
                        prototype,
                        $"{prototype.name}_ShellL2Wood",
                        shellL2WoodCandidate.Mesh,
                        prototype.GeneratedCanopyShellsRelativeFolder));
                SetPrivateField(prototype, "shellNodesL0", persistedL0);
                SetPrivateField(prototype, "shellNodesL1", persistedL1);
                SetPrivateField(prototype, "shellNodesL2", persistedL2);

                EditorUtility.SetDirty(prototype);
            }
            finally
            {
                CleanupTemporaryHierarchies(temporaryHierarchies);
                CleanupTemporaryLevels(temporaryFallbackLevels);
                if (shellL1WoodCandidate != null)
                {
                    GeneratedMeshSimplificationUtility.DestroyTemporaryMesh(shellL1WoodCandidate.Mesh);
                }

                if (shellL2WoodCandidate != null)
                {
                    GeneratedMeshSimplificationUtility.DestroyTemporaryMesh(shellL2WoodCandidate.Mesh);
                }
            }
        }

        private static ShellLevelSelection SelectShellLevelSelection(
            BranchPrototypeSO prototype,
            ShellBakeSettings settings,
            MeshVoxelizerHierarchyNode[] hierarchyNodes,
            int shellLevel,
            int triangleBudget,
            string sourceDescription,
            List<Mesh[]> temporaryFallbackLevels)
        {
            ShellLevelSelection bestSelection = CreateShellLevelSelection(
                hierarchyNodes,
                shellLevel,
                sourceDescription);
            if (bestSelection.TriangleCount <= triangleBudget || settings.SkipSimplifyFallback)
            {
                LogShellBudgetErrorIfNeeded(prototype, shellLevel, bestSelection, triangleBudget, settings.SkipSimplifyFallback);
                return bestSelection;
            }

            for (int i = 0; i < MeshLodFallbackLimits.Length; i++)
            {
                int lodLimit = MeshLodFallbackLimits[i];
                Mesh[] lodMeshes = CreateMeshLodFallbackLevel(bestSelection.Meshes, shellLevel, lodLimit);
                temporaryFallbackLevels.Add(lodMeshes);

                ShellLevelSelection lodSelection = CreateShellLevelSelection(
                    hierarchyNodes,
                    shellLevel,
                    lodMeshes,
                    $"MeshLodUtility LOD{lodLimit}");
                bestSelection = ChooseBetterShellSelection(bestSelection, lodSelection, triangleBudget);
                if (bestSelection.TriangleCount <= triangleBudget)
                {
                    return bestSelection;
                }
            }

            LogShellBudgetErrorIfNeeded(prototype, shellLevel, bestSelection, triangleBudget, false);
            return bestSelection;
        }

        private static ShellLevelSelection CreateShellLevelSelection(
            MeshVoxelizerHierarchyNode[] hierarchyNodes,
            int shellLevel,
            string sourceDescription)
        {
            return CreateShellLevelSelection(
                hierarchyNodes,
                shellLevel,
                ExtractLevelMeshes(hierarchyNodes, shellLevel),
                sourceDescription);
        }

        private static ShellLevelSelection CreateShellLevelSelection(
            MeshVoxelizerHierarchyNode[] hierarchyNodes,
            int shellLevel,
            Mesh[] levelMeshes,
            string sourceDescription)
        {
            int[] leafIndices = CollectLeafIndices(hierarchyNodes);
            return new ShellLevelSelection(
                levelMeshes,
                GetLeafTriangleCount(levelMeshes, leafIndices),
                sourceDescription);
        }

        private static ShellLevelSelection ChooseBetterShellSelection(
            ShellLevelSelection bestSelection,
            ShellLevelSelection candidate,
            int triangleBudget)
        {
            bool bestPasses = bestSelection.TriangleCount <= triangleBudget;
            bool candidatePasses = candidate.TriangleCount <= triangleBudget;
            if (candidatePasses && !bestPasses)
            {
                return candidate;
            }

            if (candidatePasses == bestPasses && candidate.TriangleCount < bestSelection.TriangleCount)
            {
                return candidate;
            }

            return bestSelection;
        }

        private static void LogShellBudgetErrorIfNeeded(
            BranchPrototypeSO prototype,
            int shellLevel,
            ShellLevelSelection selection,
            int triangleBudget,
            bool fallbackSkipped)
        {
            if (selection.TriangleCount <= triangleBudget)
            {
                return;
            }

            string fallbackState = fallbackSkipped ? "skipped" : "exhausted";
            Debug.LogError(
                $"{prototype.name} shell L{shellLevel} leaf-frontier triangle count {selection.TriangleCount} exceeds budget {triangleBudget}. Final source: {selection.SourceDescription}. Simplify fallback {fallbackState}.");
        }

        private static Mesh[] CreateMeshLodFallbackLevel(Mesh[] sourceMeshes, int shellLevel, int lodLimit)
        {
            Mesh[] lodMeshes = new Mesh[sourceMeshes.Length];
            for (int i = 0; i < sourceMeshes.Length; i++)
            {
                Mesh sourceMesh = sourceMeshes[i];
                Mesh? lodMesh = GeneratedMeshSimplificationUtility.CreateMeshLodFallbackMesh(
                    sourceMesh,
                    $"{sourceMesh.name}_L{shellLevel}_LOD{lodLimit}",
                    lodLimit);
                if (lodMesh == null)
                {
                    lodMesh = UnityEngine.Object.Instantiate(sourceMesh);
                    lodMesh.name = $"{sourceMesh.name}_L{shellLevel}_LOD{lodLimit}_SourceClone";
                }

                lodMeshes[i] = lodMesh;
            }

            return lodMeshes;
        }

        private static Mesh[] ExtractLevelMeshes(MeshVoxelizerHierarchyNode[] hierarchyNodes, int shellLevel)
        {
            Mesh[] levelMeshes = new Mesh[hierarchyNodes.Length];
            for (int i = 0; i < hierarchyNodes.Length; i++)
            {
                levelMeshes[i] = shellLevel switch
                {
                    0 => hierarchyNodes[i].ShellL0Mesh ?? throw new InvalidOperationException($"Node {i} is missing L0 shell mesh."),
                    1 => hierarchyNodes[i].ShellL1Mesh ?? throw new InvalidOperationException($"Node {i} is missing L1 shell mesh."),
                    2 => hierarchyNodes[i].ShellL2Mesh ?? throw new InvalidOperationException($"Node {i} is missing L2 shell mesh."),
                    _ => throw new ArgumentOutOfRangeException(nameof(shellLevel), shellLevel, "Shell level must be 0, 1, or 2.")
                };
            }

            return levelMeshes;
        }

        private static int[] CollectLeafIndices(MeshVoxelizerHierarchyNode[] hierarchyNodes)
        {
            List<int> leafIndices = new List<int>();
            for (int i = 0; i < hierarchyNodes.Length; i++)
            {
                if (hierarchyNodes[i].ChildMask == 0)
                {
                    leafIndices.Add(i);
                }
            }

            return leafIndices.ToArray();
        }

        private static int GetLeafTriangleCount(Mesh[] meshes, int[] leafIndices)
        {
            int triangleCount = 0;
            for (int i = 0; i < leafIndices.Length; i++)
            {
                triangleCount += GeneratedMeshSimplificationUtility.GetTriangleCount(meshes[leafIndices[i]]);
            }

            return triangleCount;
        }

        private static BranchShellNode[] PersistShellNodes(
            BranchPrototypeSO prototype,
            MeshVoxelizerHierarchyNode[] hierarchyNodes,
            Mesh[] levelMeshes,
            int shellLevel,
            string nodeMeshPrefix)
        {
            BranchShellNode[] persistedNodes = new BranchShellNode[hierarchyNodes.Length];
            for (int i = 0; i < hierarchyNodes.Length; i++)
            {
                MeshVoxelizerHierarchyNode node = hierarchyNodes[i];
                string nodePath = $"D{node.Depth}_{i:D3}";
                Mesh persistedMesh = GeneratedMeshAssetUtility.PersistGeneratedMesh(
                    prototype,
                    $"{nodeMeshPrefix}_{nodePath}",
                    levelMeshes[i],
                    prototype.GeneratedCanopyShellsRelativeFolder);

                Mesh? shellL0Mesh = null;
                Mesh? shellL1Mesh = null;
                Mesh? shellL2Mesh = null;
                switch (shellLevel)
                {
                    case 0:
                        shellL0Mesh = persistedMesh;
                        break;
                    case 1:
                        shellL1Mesh = persistedMesh;
                        break;
                    case 2:
                        shellL2Mesh = persistedMesh;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(shellLevel), shellLevel, "Shell level must be 0, 1, or 2.");
                }

                persistedNodes[i] = new BranchShellNode(
                    node.LocalBounds,
                    node.Depth,
                    node.FirstChildIndex,
                    node.ChildMask,
                    shellL0Mesh,
                    shellL1Mesh,
                    shellL2Mesh);
            }

            return persistedNodes;
        }

        private static void CleanupTemporaryHierarchies(List<MeshVoxelizerHierarchyNode[]> temporaryHierarchies)
        {
            for (int hierarchyIndex = 0; hierarchyIndex < temporaryHierarchies.Count; hierarchyIndex++)
            {
                MeshVoxelizerHierarchyNode[] hierarchy = temporaryHierarchies[hierarchyIndex];
                for (int i = 0; i < hierarchy.Length; i++)
                {
                    GeneratedMeshSimplificationUtility.DestroyTemporaryMesh(hierarchy[i].ShellL0Mesh);
                    GeneratedMeshSimplificationUtility.DestroyTemporaryMesh(hierarchy[i].ShellL1Mesh);
                    GeneratedMeshSimplificationUtility.DestroyTemporaryMesh(hierarchy[i].ShellL2Mesh);
                }
            }
        }

        private static void CleanupTemporaryLevels(List<Mesh[]> temporaryFallbackLevels)
        {
            for (int levelIndex = 0; levelIndex < temporaryFallbackLevels.Count; levelIndex++)
            {
                Mesh[] levelMeshes = temporaryFallbackLevels[levelIndex];
                for (int meshIndex = 0; meshIndex < levelMeshes.Length; meshIndex++)
                {
                    GeneratedMeshSimplificationUtility.DestroyTemporaryMesh(levelMeshes[meshIndex]);
                }
            }
        }

        private static void SetPrivateField(object target, string fieldName, object? value)
        {
            FieldInfo? fieldInfo = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (fieldInfo == null)
            {
                throw new InvalidOperationException($"Field '{fieldName}' was not found on '{target.GetType().Name}'.");
            }

            fieldInfo.SetValue(target, value);
        }

        private sealed class ShellLevelSelection
        {
            public ShellLevelSelection(Mesh[] meshes, int triangleCount, string sourceDescription)
            {
                Meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
                TriangleCount = triangleCount;
                SourceDescription = sourceDescription ?? throw new ArgumentNullException(nameof(sourceDescription));
            }

            public Mesh[] Meshes { get; }

            public int TriangleCount { get; }

            public string SourceDescription { get; }
        }
    }
}
