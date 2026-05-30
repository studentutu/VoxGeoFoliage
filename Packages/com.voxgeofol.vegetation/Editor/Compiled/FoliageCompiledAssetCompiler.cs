#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;
using VoxGeoFol.Features.Vegetation.Rendering;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Editor compiler that cuts static foliage data out of runtime registration and into page assets.
    /// </summary>
    public static class FoliageCompiledAssetCompiler
    {
        private const int IndirectWorkCostIndexQuantum = 1024;
        private const long EstimatedAssetGroupBytes = 96L;
        private const long EstimatedBranchTemplateBytes = 160L;
        private const long EstimatedTreeBytes = 224L;
        private const long EstimatedCellBytes = 64L;
        private const long EstimatedPacketBytes = 80L;
        private const long EstimatedInstanceBytes = 144L;
        private const long EstimatedMeshVertexBytes = 64L;

        /// <summary>
        /// [INTEGRATION] Compiles one runtime container into in-memory foliage assembly and page assets.
        /// </summary>
        public static FoliageCompilationResult CompileContainer(
            VegetationRuntimeContainer container,
            FoliageCompilerSettings settings)
        {
            // Range: one configured classic-scene container. Condition: validates current registered authoring and enforces hard page/cell/packet caps. Output: generated assets with no shadow-proxy packet family.
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            settings = new FoliageCompilerSettings(
                settings.CellSize,
                settings.MaxTreesPerPage,
                settings.MaxTreesPerCell,
                settings.MaxPacketsPerPage,
                settings.MaxAssetGroups,
                settings.MaxNearDetailBytesPerPage);

            List<string> failures = new List<string>();
            ValidateRegisteredAuthorings(container, failures);

            List<VegetationTreeAuthoringRuntime> runtimeTrees = new List<VegetationTreeAuthoringRuntime>();
            try
            {
                container.BuildRuntimeTreeAuthorings(runtimeTrees);
            }
            catch (Exception exception)
            {
                failures.Add(exception.Message);
            }

            if (failures.Count > 0)
            {
                return CreateFailedResult(failures);
            }

            try
            {
                return CompileValidatedContainer(container, runtimeTrees, settings, failures);
            }
            catch (Exception exception)
            {
                failures.Add(exception.Message);
                return CreateFailedResult(failures);
            }
        }

        /// <summary>
        /// [INTEGRATION] Compiles one runtime container and persists generated assets under an Assets/ folder.
        /// </summary>
        public static FoliageCompilationResult CompileContainerToAssets(
            VegetationRuntimeContainer container,
            string outputFolder,
            FoliageCompilerSettings settings)
        {
            // Range: accepts a selected container and a generated output folder. Condition: asset paths are stable and stale page assets for this container prefix are removed. Output: refreshed assembly and page assets ready for runtime registration.
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            outputFolder = NormalizeAssetFolder(outputFolder);
            FoliageCompilationResult result = CompileContainer(container, settings);
            if (!result.Succeeded || result.AssemblyAsset == null)
            {
                return result;
            }

            CreateAssetFolder(outputFolder);
            string safeContainerName = MakeSafeAssetName(container.name);
            string assemblyPath = $"{outputFolder}/{safeContainerName}_FoliageAssembly.asset";
            List<string> currentPagePaths = new List<string>();

            AssetDatabase.StartAssetEditing();
            try
            {
                FoliageAssetGroup[] persistentAssetGroups = BuildPersistentAssetGroups(
                    result.AssemblyAsset.AssetGroups,
                    outputFolder,
                    safeContainerName);
                FoliageAssemblyAsset assemblyAsset = LoadOrCreateAsset<FoliageAssemblyAsset>(assemblyPath);
                assemblyAsset.InitializeForCompiler(
                    result.AssemblyAsset.SourceContainerId,
                    persistentAssetGroups,
                    CopyToArray(result.AssemblyAsset.BranchPlacementTemplates),
                    result.AssemblyAsset.BuildReport ?? result.BuildReport);
                EditorUtility.SetDirty(assemblyAsset);

                for (int i = 0; i < result.PageAssets.Count; i++)
                {
                    FoliagePageAsset sourcePage = result.PageAssets[i];
                    string pagePath = $"{outputFolder}/{safeContainerName}_Page_{i:000}.asset";
                    currentPagePaths.Add(pagePath);
                    FoliagePageAsset pageAsset = LoadOrCreateAsset<FoliagePageAsset>(pagePath);
                    pageAsset.InitializeForCompiler(
                        sourcePage.PageId,
                        sourcePage.WorldBounds,
                        CopyToArray(sourcePage.Cells),
                        CopyToArray(sourcePage.Trees),
                        CopyToArray(sourcePage.Instances),
                        CopyToArray(sourcePage.Packets),
                        sourcePage.BuildReport ?? result.BuildReport);
                    EditorUtility.SetDirty(pageAsset);
                }

                DeleteStalePageAssets(outputFolder, safeContainerName, currentPagePaths);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return result;
        }

        private static FoliageCompilationResult CompileValidatedContainer(
            VegetationRuntimeContainer container,
            List<VegetationTreeAuthoringRuntime> runtimeTrees,
            FoliageCompilerSettings settings,
            List<string> failures)
        {
            FoliageCompiledAssetGroupRegistry assetGroups = new FoliageCompiledAssetGroupRegistry(settings.MaxAssetGroups);
            Dictionary<TreeBlueprintSO, int> blueprintIndices = new Dictionary<TreeBlueprintSO, int>();
            Dictionary<BranchPrototypeSO, int> prototypeIndices = new Dictionary<BranchPrototypeSO, int>();
            List<FoliageBranchPlacementTemplate> placementTemplates = new List<FoliageBranchPlacementTemplate>();
            List<FoliageCompiledTreeRecord> compiledTrees = new List<FoliageCompiledTreeRecord>();
            Vector3 cellSize = FoliageCompilerSettings.SanitizeCellSize(settings.CellSize);

            for (int i = 0; i < runtimeTrees.Count; i++)
            {
                VegetationTreeAuthoringRuntime runtimeTree = runtimeTrees[i];
                if (!runtimeTree.IsActive)
                {
                    continue;
                }

                TreeBlueprintSO blueprint = runtimeTree.Blueprint;
                int blueprintIndex = RegisterBlueprint(
                    blueprint,
                    assetGroups,
                    blueprintIndices,
                    prototypeIndices,
                    placementTemplates);
                Matrix4x4 localToWorld = runtimeTree.LocalToWorld;
                Bounds worldBounds = VegetationRuntimeMathUtility.TransformBounds(blueprint.TreeBounds, localToWorld);
                GetCellCoordinate(worldBounds.center, container.GridOrigin, cellSize, out int cellX, out int cellY, out int cellZ);
                compiledTrees.Add(new FoliageCompiledTreeRecord(
                    i,
                    blueprintIndex,
                    blueprint,
                    runtimeTree.StableTreeId,
                    runtimeTree.DebugName,
                    localToWorld,
                    localToWorld.inverse,
                    worldBounds,
                    cellX,
                    cellY,
                    cellZ));
            }

            List<FoliagePageAsset> pages = BuildPages(compiledTrees, assetGroups, settings, failures);
            if (failures.Count > 0)
            {
                return CreateFailedResult(failures);
            }

            FoliageAssetGroup[] compiledAssetGroups = assetGroups.ToArray();
            FoliageCompilerBuildReport report = BuildAssemblyReport(
                pages,
                compiledAssetGroups,
                placementTemplates.Count,
                compiledTrees.Count,
                Math.Max(0, pages.Count - 1),
                failures);

            FoliageAssemblyAsset assemblyAsset = ScriptableObject.CreateInstance<FoliageAssemblyAsset>();
            assemblyAsset.name = $"{container.name}_FoliageAssembly";
            assemblyAsset.hideFlags = HideFlags.DontSave;
            assemblyAsset.InitializeForCompiler(
                container.ContainerId,
                compiledAssetGroups,
                placementTemplates.ToArray(),
                report);

            return new FoliageCompilationResult(assemblyAsset, pages.ToArray(), report, Array.Empty<string>());
        }

        private static void ValidateRegisteredAuthorings(VegetationRuntimeContainer container, List<string> failures)
        {
            IReadOnlyList<VegetationTreeAuthoring> authorings = container.RegisteredAuthorings;
            HashSet<TreeBlueprintSO> validatedBlueprints = new HashSet<TreeBlueprintSO>();
            HashSet<BranchPrototypeSO> validatedPrototypes = new HashSet<BranchPrototypeSO>();
            for (int i = 0; i < authorings.Count; i++)
            {
                VegetationTreeAuthoring? authoring = authorings[i];
                if (authoring == null)
                {
                    failures.Add($"{container.name}.registeredAuthorings[{i}] is missing.");
                    continue;
                }

                TreeBlueprintSO? blueprint = authoring.Blueprint;
                if (blueprint == null)
                {
                    failures.Add($"{authoring.name}: VegetationTreeAuthoring is missing blueprint.");
                    continue;
                }

                if (validatedBlueprints.Add(blueprint))
                {
                    ValidateBlueprintCompileInputs(blueprint, validatedPrototypes, failures);
                }
            }
        }

        private static void ValidateBlueprintCompileInputs(
            TreeBlueprintSO blueprint,
            HashSet<BranchPrototypeSO> validatedPrototypes,
            List<string> failures)
        {
            string ownerName = GetValidationOwnerName(blueprint, "TreeBlueprint");
            AddRequiredReadableMesh(blueprint.TrunkMesh, ownerName, "trunkMesh", failures);
            AddRequiredReadableMesh(blueprint.TrunkL3Mesh, ownerName, "trunkL3Mesh", failures);
            AddRequiredReadableMesh(blueprint.ImpostorMesh, ownerName, "impostorMesh", failures);
            AddRequiredOpaqueMaterial(blueprint.TrunkMaterial, ownerName, "trunkMaterial", failures);
            AddRequiredOpaqueMaterial(blueprint.ImpostorMaterial, ownerName, "impostorMaterial", failures);

            BranchPlacement[] placements = blueprint.Branches;
            if (placements == null)
            {
                failures.Add($"{ownerName}.branches is missing.");
                return;
            }

            for (int placementIndex = 0; placementIndex < placements.Length; placementIndex++)
            {
                BranchPlacement? placement = placements[placementIndex];
                if (placement == null)
                {
                    failures.Add($"{ownerName}.branches[{placementIndex}] is missing.");
                    continue;
                }

                if (placement.Scale <= 0f || float.IsNaN(placement.Scale) || float.IsInfinity(placement.Scale))
                {
                    failures.Add($"{ownerName}.branches[{placementIndex}].scale must be finite and greater than zero.");
                }

                BranchPrototypeSO? prototype = placement.Prototype;
                if (prototype == null)
                {
                    failures.Add($"{ownerName}.branches[{placementIndex}] is missing prototype.");
                    continue;
                }

                if (validatedPrototypes.Add(prototype))
                {
                    ValidatePrototypeCompileInputs(prototype, failures);
                }
            }
        }

        private static void ValidatePrototypeCompileInputs(BranchPrototypeSO prototype, List<string> failures)
        {
            string ownerName = GetValidationOwnerName(prototype, "BranchPrototype");
            AddRequiredReadableMesh(prototype.WoodMesh, ownerName, "woodMesh", failures);
            AddRequiredReadableMesh(prototype.FoliageMesh, ownerName, "foliageMesh", failures);
            AddRequiredReadableMesh(prototype.BranchL1WoodMesh, ownerName, "branchL1WoodMesh", failures);
            AddRequiredReadableMesh(prototype.BranchL2WoodMesh, ownerName, "branchL2WoodMesh", failures);
            AddRequiredReadableMesh(prototype.BranchL3WoodMesh, ownerName, "branchL3WoodMesh", failures);
            AddRequiredReadableMesh(prototype.BranchL1CanopyMesh, ownerName, "branchL1CanopyMesh", failures);
            AddRequiredReadableMesh(prototype.BranchL2CanopyMesh, ownerName, "branchL2CanopyMesh", failures);
            AddRequiredReadableMesh(prototype.BranchL3CanopyMesh, ownerName, "branchL3CanopyMesh", failures);
            AddRequiredOpaqueMaterial(prototype.WoodMaterial, ownerName, "woodMaterial", failures);
            AddRequiredOpaqueMaterial(prototype.FoliageMaterial, ownerName, "foliageMaterial", failures);
            AddRequiredOpaqueMaterial(prototype.ShellMaterial, ownerName, "shellMaterial", failures);
        }

        private static void AddRequiredReadableMesh(Mesh? mesh, string ownerName, string fieldName, List<string> failures)
        {
            if (mesh == null)
            {
                failures.Add($"{ownerName} is missing {fieldName}.");
                return;
            }

            if (!mesh.isReadable)
            {
                failures.Add($"{ownerName}.{fieldName} must be readable.");
            }
        }

        private static void AddRequiredOpaqueMaterial(Material? material, string ownerName, string fieldName, List<string> failures)
        {
            if (material == null)
            {
                failures.Add($"{ownerName} is missing {fieldName}.");
                return;
            }

            if (!VegetationAuthoringValidator.TryValidateOpaqueMaterial(material, out string reason))
            {
                failures.Add($"{ownerName}.{fieldName} must be opaque. {reason}");
            }
        }

        private static string GetValidationOwnerName(UnityEngine.Object? asset, string fallback)
        {
            return asset != null && !string.IsNullOrWhiteSpace(asset.name)
                ? asset.name
                : fallback;
        }

        private static int RegisterBlueprint(
            TreeBlueprintSO blueprint,
            FoliageCompiledAssetGroupRegistry assetGroups,
            Dictionary<TreeBlueprintSO, int> blueprintIndices,
            Dictionary<BranchPrototypeSO, int> prototypeIndices,
            List<FoliageBranchPlacementTemplate> placementTemplates)
        {
            if (blueprintIndices.TryGetValue(blueprint, out int existingIndex))
            {
                return existingIndex;
            }

            int blueprintIndex = blueprintIndices.Count;
            blueprintIndices.Add(blueprint, blueprintIndex);
            RegisterBlueprintAssetGroups(blueprint, assetGroups);

            BranchPlacement[] branches = blueprint.Branches;
            for (int i = 0; i < branches.Length; i++)
            {
                BranchPlacement placement = branches[i];
                BranchPrototypeSO prototype = placement.Prototype ??
                                              throw new InvalidOperationException($"{blueprint.name}.branches[{i}] is missing prototype.");
                if (!prototypeIndices.TryGetValue(prototype, out int prototypeIndex))
                {
                    prototypeIndex = prototypeIndices.Count;
                    prototypeIndices.Add(prototype, prototypeIndex);
                    RegisterPrototypeAssetGroups(prototype, assetGroups);
                }

                Matrix4x4 localToTree = Matrix4x4.TRS(
                    placement.LocalPosition,
                    placement.LocalRotation,
                    Vector3.one * Mathf.Max(0.0001f, placement.Scale));
                placementTemplates.Add(new FoliageBranchPlacementTemplate(
                    blueprintIndex,
                    prototypeIndex,
                    localToTree,
                    localToTree.inverse,
                    prototype.LocalBounds,
                    prototype.LocalBounds.extents.magnitude * Mathf.Max(0.0001f, placement.Scale),
                    VegetationRuntimeMathUtility.PackColorToUint(prototype.LeafColorTint)));
            }

            return blueprintIndex;
        }

        private static void RegisterBlueprintAssetGroups(TreeBlueprintSO blueprint, FoliageCompiledAssetGroupRegistry assetGroups)
        {
            Mesh trunkMesh = RequireMesh(blueprint.TrunkMesh, blueprint.name, "trunkMesh");
            Mesh trunkL3Mesh = RequireMesh(blueprint.TrunkL3Mesh, blueprint.name, "trunkL3Mesh");
            Material trunkMaterial = RequireMaterial(blueprint.TrunkMaterial, blueprint.name, "trunkMaterial");

            assetGroups.Register(trunkMesh, trunkMaterial, VegetationRenderMaterialKind.Trunk, $"{blueprint.name}:TrunkFull");
            assetGroups.Register(trunkL3Mesh, trunkMaterial, VegetationRenderMaterialKind.Trunk, $"{blueprint.name}:TrunkL3");
        }

        private static void RegisterPrototypeAssetGroups(BranchPrototypeSO prototype, FoliageCompiledAssetGroupRegistry assetGroups)
        {
            Material woodMaterial = RequireMaterial(prototype.WoodMaterial, prototype.name, "woodMaterial");
            Material foliageMaterial = RequireMaterial(prototype.FoliageMaterial, prototype.name, "foliageMaterial");
            Material shellMaterial = RequireMaterial(prototype.ShellMaterial, prototype.name, "shellMaterial");

            assetGroups.Register(
                RequireMesh(prototype.WoodMesh, prototype.name, "woodMesh"),
                woodMaterial,
                VegetationRenderMaterialKind.Trunk,
                $"{prototype.name}:WoodL0");
            assetGroups.Register(
                RequireMesh(prototype.FoliageMesh, prototype.name, "foliageMesh"),
                foliageMaterial,
                VegetationRenderMaterialKind.CanopyFoliage,
                $"{prototype.name}:FoliageL0");
            assetGroups.Register(
                RequireMesh(prototype.BranchL1WoodMesh, prototype.name, "branchL1WoodMesh"),
                woodMaterial,
                VegetationRenderMaterialKind.Trunk,
                $"{prototype.name}:WoodL1");
            assetGroups.Register(
                RequireMesh(prototype.BranchL1CanopyMesh, prototype.name, "branchL1CanopyMesh"),
                shellMaterial,
                VegetationRenderMaterialKind.CanopyShell,
                $"{prototype.name}:CanopyL1");
            assetGroups.Register(
                RequireMesh(prototype.BranchL2WoodMesh, prototype.name, "branchL2WoodMesh"),
                woodMaterial,
                VegetationRenderMaterialKind.Trunk,
                $"{prototype.name}:WoodL2");
            assetGroups.Register(
                RequireMesh(prototype.BranchL2CanopyMesh, prototype.name, "branchL2CanopyMesh"),
                shellMaterial,
                VegetationRenderMaterialKind.CanopyShell,
                $"{prototype.name}:CanopyL2");
        }

        private static List<FoliagePageAsset> BuildPages(
            List<FoliageCompiledTreeRecord> compiledTrees,
            FoliageCompiledAssetGroupRegistry assetGroups,
            FoliageCompilerSettings settings,
            List<string> failures)
        {
            List<FoliagePageAsset> pages = new List<FoliagePageAsset>();
            int maxTreesPerPage = settings.MaxTreesPerPage;
            for (int start = 0; start < compiledTrees.Count; start += maxTreesPerPage)
            {
                int count = Math.Min(maxTreesPerPage, compiledTrees.Count - start);
                List<FoliageCompiledTreeRecord> pageTrees = compiledTrees.GetRange(start, count);
                FoliagePageAsset? page = BuildPage(
                    pages.Count,
                    pageTrees,
                    assetGroups,
                    settings,
                    failures);
                if (page != null)
                {
                    pages.Add(page);
                }
            }

            return pages;
        }

        private static FoliagePageAsset? BuildPage(
            int pageIndex,
            List<FoliageCompiledTreeRecord> pageTrees,
            FoliageCompiledAssetGroupRegistry assetGroups,
            FoliageCompilerSettings settings,
            List<string> failures)
        {
            pageTrees.Sort(CompareByCellThenTree);
            FoliagePageCell[] cells = BuildCells(pageTrees, settings.MaxTreesPerCell, failures);
            if (failures.Count > 0)
            {
                return null;
            }

            FoliagePageTree[] serializedTrees = new FoliagePageTree[pageTrees.Count];
            Bounds pageBounds = pageTrees.Count > 0 ? pageTrees[0].WorldBounds : new Bounds(Vector3.zero, Vector3.zero);
            for (int i = 0; i < pageTrees.Count; i++)
            {
                serializedTrees[i] = pageTrees[i].ToPageTree();
                if (i > 0)
                {
                    pageBounds.Encapsulate(pageTrees[i].WorldBounds);
                }
            }

            List<FoliageCompiledPacketDraft> drafts = BuildPacketDrafts(pageIndex, pageTrees, cells, assetGroups);
            FoliagePacketInstance[] instances;
            FoliageRepresentationPacket[] packets;
            BuildContiguousPackets(drafts, out instances, out packets);
            if (packets.Length > settings.MaxPacketsPerPage)
            {
                failures.Add(
                    $"Compiled page {pageIndex} produced {packets.Length} packets, exceeding maxPacketsPerPage={settings.MaxPacketsPerPage}.");
                return null;
            }

            long nearDetailBytes = EstimatePacketBytes(packets, FoliagePacketResidency.NearDetail);
            if (nearDetailBytes > settings.MaxNearDetailBytesPerPage)
            {
                failures.Add(
                    $"Compiled page {pageIndex} near-detail stream uses {nearDetailBytes} bytes, exceeding maxNearDetailBytesPerPage={settings.MaxNearDetailBytesPerPage}.");
                return null;
            }

            ValidateCompiledPackets(packets, failures);
            if (failures.Count > 0)
            {
                return null;
            }

            long alwaysResidentBytes = EstimatePacketBytes(packets, FoliagePacketResidency.AlwaysResident);

            FoliageCompilerBuildReport pageReport = BuildReport(
                1,
                cells.Length,
                serializedTrees.Length,
                packets.Length,
                assetGroups.Count,
                instances.Length,
                alwaysResidentBytes,
                nearDetailBytes,
                EstimatePageBytes(cells.Length, serializedTrees.Length, packets.Length, instances.Length),
                EstimateMaxCellBytes(cells, packets),
                packets.Length,
                CountShadowCompatiblePackets(packets),
                packets.Length,
                CountMaxPacketsPerCell(packets),
                0,
                0,
                Array.Empty<string>());

            FoliagePageAsset pageAsset = ScriptableObject.CreateInstance<FoliagePageAsset>();
            pageAsset.name = $"FoliagePage_{pageIndex:000}";
            pageAsset.hideFlags = HideFlags.DontSave;
            pageAsset.InitializeForCompiler(
                $"Page_{pageIndex:000}",
                pageBounds,
                cells,
                serializedTrees,
                instances,
                packets,
                pageReport);
            return pageAsset;
        }

        private static FoliagePageCell[] BuildCells(
            List<FoliageCompiledTreeRecord> pageTrees,
            int maxTreesPerCell,
            List<string> failures)
        {
            List<FoliagePageCell> cells = new List<FoliagePageCell>();
            int firstTreeIndex = 0;
            while (firstTreeIndex < pageTrees.Count)
            {
                FoliageCompiledTreeRecord firstTree = pageTrees[firstTreeIndex];
                int treeCount = 1;
                Bounds cellBounds = firstTree.WorldBounds;
                while (firstTreeIndex + treeCount < pageTrees.Count &&
                       IsSameCell(firstTree, pageTrees[firstTreeIndex + treeCount]))
                {
                    cellBounds.Encapsulate(pageTrees[firstTreeIndex + treeCount].WorldBounds);
                    treeCount++;
                }

                if (treeCount > maxTreesPerCell)
                {
                    failures.Add(
                        $"Cell ({firstTree.CellX},{firstTree.CellY},{firstTree.CellZ}) contains {treeCount} trees, exceeding maxTreesPerCell={maxTreesPerCell}.");
                    return Array.Empty<FoliagePageCell>();
                }

                int cellIndex = cells.Count;
                for (int i = 0; i < treeCount; i++)
                {
                    pageTrees[firstTreeIndex + i].PageCellIndex = cellIndex;
                }

                cells.Add(new FoliagePageCell(
                    cellIndex,
                    cellBounds,
                    cellBounds.center,
                    cellBounds.extents.magnitude,
                    firstTreeIndex,
                    treeCount));
                firstTreeIndex += treeCount;
            }

            return cells.ToArray();
        }

        private static List<FoliageCompiledPacketDraft> BuildPacketDrafts(
            int pageIndex,
            List<FoliageCompiledTreeRecord> pageTrees,
            FoliagePageCell[] cells,
            FoliageCompiledAssetGroupRegistry assetGroups)
        {
            List<FoliageCompiledPacketDraft> packets = new List<FoliageCompiledPacketDraft>();
            AddHlodPackets(packets, pageIndex, pageTrees, cells, assetGroups);
            for (int i = 0; i < pageTrees.Count; i++)
            {
                FoliageCompiledTreeRecord tree = pageTrees[i];
                AddTreePackets(packets, tree, tree.Blueprint, assetGroups);
                AddBranchPackets(packets, tree, tree.Blueprint, assetGroups);
            }

            return packets;
        }

        private static void AddHlodPackets(
            List<FoliageCompiledPacketDraft> packets,
            int pageIndex,
            List<FoliageCompiledTreeRecord> pageTrees,
            FoliagePageCell[] cells,
            FoliageCompiledAssetGroupRegistry assetGroups)
        {
            AddHlodPacketsForTrees(
                packets,
                FoliageRepresentationKind.PageHLOD,
                pageIndex,
                -1,
                pageTrees,
                assetGroups);

            for (int i = 0; i < cells.Length; i++)
            {
                FoliagePageCell cell = cells[i];
                List<FoliageCompiledTreeRecord> cellTrees = new List<FoliageCompiledTreeRecord>(cell.TreeCount);
                for (int treeOffset = 0; treeOffset < cell.TreeCount; treeOffset++)
                {
                    cellTrees.Add(pageTrees[cell.FirstTreeIndex + treeOffset]);
                }

                AddHlodPacketsForTrees(
                    packets,
                    FoliageRepresentationKind.CellHLOD,
                    pageIndex,
                    cell.CellIndex,
                    cellTrees,
                    assetGroups);
            }
        }

        private static void AddHlodPacketsForTrees(
            List<FoliageCompiledPacketDraft> packets,
            FoliageRepresentationKind representationKind,
            int pageIndex,
            int cellIndex,
            List<FoliageCompiledTreeRecord> trees,
            FoliageCompiledAssetGroupRegistry assetGroups)
        {
            for (int i = 0; i < trees.Count; i++)
            {
                FoliageCompiledTreeRecord tree = trees[i];
                TreeBlueprintSO blueprint = tree.Blueprint;
                Mesh impostorMesh = RequireMesh(blueprint.ImpostorMesh, blueprint.name, "impostorMesh");
                Material impostorMaterial = RequireMaterial(blueprint.ImpostorMaterial, blueprint.name, "impostorMaterial");
                AddHlodPacket(
                    packets,
                    representationKind,
                    pageIndex,
                    cellIndex,
                    tree,
                    assetGroups,
                    impostorMesh,
                    impostorMaterial,
                    tree.LocalToWorld,
                    tree.WorldToObject,
                    0u,
                    0.15f,
                    0f,
                    $"{representationKind}:Page{pageIndex:000}:Cell{cellIndex}:{tree.DebugName}:Impostor");
            }
        }

        private static void AddHlodPacket(
            List<FoliageCompiledPacketDraft> packets,
            FoliageRepresentationKind representationKind,
            int pageIndex,
            int cellIndex,
            FoliageCompiledTreeRecord tree,
            FoliageCompiledAssetGroupRegistry assetGroups,
            Mesh mesh,
            Material material,
            Matrix4x4 objectToWorld,
            Matrix4x4 worldToObject,
            uint packedLeafTint,
            float trunkBendWeight,
            float branchFlutterWeight,
            string debugLabel)
        {
            Bounds worldBounds = VegetationRuntimeMathUtility.TransformBounds(mesh.bounds, objectToWorld);
            int assetGroupIndex = assetGroups.Register(
                mesh,
                material,
                VegetationRenderMaterialKind.FarMesh,
                $"{mesh.name}:{representationKind}:Page{pageIndex:000}:Cell{cellIndex}");
            packets.Add(new FoliageCompiledPacketDraft(
                representationKind,
                tree.SourceTreeIndex,
                cellIndex,
                assetGroupIndex,
                new FoliagePacketInstance(
                    tree.SourceTreeIndex,
                    assetGroupIndex,
                    objectToWorld,
                    worldToObject,
                    worldBounds,
                    packedLeafTint,
                    CreateWindMetadata(tree.SourceTreeIndex, tree.WorldBounds, trunkBendWeight, branchFlutterWeight)),
                worldBounds,
                ComputeWorkCost(mesh),
                FoliagePacketResidency.AlwaysResident,
                FoliageShadowPacketMode.Hlod,
                debugLabel));
        }

        private static void AddTreePackets(
            List<FoliageCompiledPacketDraft> packets,
            FoliageCompiledTreeRecord tree,
            TreeBlueprintSO blueprint,
            FoliageCompiledAssetGroupRegistry assetGroups)
        {
            Material trunkMaterial = RequireMaterial(blueprint.TrunkMaterial, blueprint.name, "trunkMaterial");
            Mesh trunkMesh = RequireMesh(blueprint.TrunkMesh, blueprint.name, "trunkMesh");
            Mesh trunkL3Mesh = RequireMesh(blueprint.TrunkL3Mesh, blueprint.name, "trunkL3Mesh");

            AddPacket(
                packets,
                FoliageRepresentationKind.TreeL0,
                tree,
                assetGroups.Register(trunkMesh, trunkMaterial, VegetationRenderMaterialKind.Trunk, $"{blueprint.name}:TrunkFull"),
                tree.LocalToWorld,
                tree.WorldToObject,
                VegetationRuntimeMathUtility.TransformBounds(trunkMesh.bounds, tree.LocalToWorld),
                0u,
                ComputeWorkCost(trunkMesh),
                FoliagePacketResidency.NearDetail,
                FoliageShadowPacketMode.SameAsColor,
                CreateWindMetadata(tree.SourceTreeIndex, tree.WorldBounds, 1f, 0.05f),
                $"{tree.DebugName}:TreeL0:TrunkFull");
            AddPacket(
                packets,
                FoliageRepresentationKind.TreeL1,
                tree,
                assetGroups.Register(trunkMesh, trunkMaterial, VegetationRenderMaterialKind.Trunk, $"{blueprint.name}:TrunkFull"),
                tree.LocalToWorld,
                tree.WorldToObject,
                VegetationRuntimeMathUtility.TransformBounds(trunkMesh.bounds, tree.LocalToWorld),
                0u,
                ComputeWorkCost(trunkMesh),
                FoliagePacketResidency.NearDetail,
                FoliageShadowPacketMode.SameAsColor,
                CreateWindMetadata(tree.SourceTreeIndex, tree.WorldBounds, 1f, 0.05f),
                $"{tree.DebugName}:TreeL1:TrunkFull");
            AddPacket(
                packets,
                FoliageRepresentationKind.TreeL2,
                tree,
                assetGroups.Register(trunkL3Mesh, trunkMaterial, VegetationRenderMaterialKind.Trunk, $"{blueprint.name}:TrunkL3"),
                tree.LocalToWorld,
                tree.WorldToObject,
                VegetationRuntimeMathUtility.TransformBounds(trunkL3Mesh.bounds, tree.LocalToWorld),
                0u,
                ComputeWorkCost(trunkL3Mesh),
                FoliagePacketResidency.NearDetail,
                FoliageShadowPacketMode.CheapTree,
                CreateWindMetadata(tree.SourceTreeIndex, tree.WorldBounds, 0.75f, 0f),
                $"{tree.DebugName}:TreeL2:TrunkL3");
        }

        private static void AddBranchPackets(
            List<FoliageCompiledPacketDraft> packets,
            FoliageCompiledTreeRecord tree,
            TreeBlueprintSO blueprint,
            FoliageCompiledAssetGroupRegistry assetGroups)
        {
            BranchPlacement[] branches = blueprint.Branches;
            for (int i = 0; i < branches.Length; i++)
            {
                BranchPlacement placement = branches[i];
                BranchPrototypeSO prototype = placement.Prototype ??
                                              throw new InvalidOperationException($"{blueprint.name}.branches[{i}] is missing prototype.");
                Matrix4x4 localToTree = Matrix4x4.TRS(
                    placement.LocalPosition,
                    placement.LocalRotation,
                    Vector3.one * Mathf.Max(0.0001f, placement.Scale));
                Matrix4x4 objectToWorld = tree.LocalToWorld * localToTree;
                Matrix4x4 worldToObject = localToTree.inverse * tree.WorldToObject;
                uint packedLeafTint = VegetationRuntimeMathUtility.PackColorToUint(prototype.LeafColorTint);
                Material woodMaterial = RequireMaterial(prototype.WoodMaterial, prototype.name, "woodMaterial");
                Material foliageMaterial = RequireMaterial(prototype.FoliageMaterial, prototype.name, "foliageMaterial");
                Material shellMaterial = RequireMaterial(prototype.ShellMaterial, prototype.name, "shellMaterial");

                AddBranchPacket(packets, FoliageRepresentationKind.TreeL0, tree, prototype, assetGroups, RequireMesh(prototype.WoodMesh, prototype.name, "woodMesh"), woodMaterial, VegetationRenderMaterialKind.Trunk, objectToWorld, worldToObject, 0u, FoliageShadowPacketMode.SameAsColor, 1f, 0.05f, $"{tree.DebugName}:TreeL0:Wood{i}");
                AddBranchPacket(packets, FoliageRepresentationKind.TreeL0, tree, prototype, assetGroups, RequireMesh(prototype.FoliageMesh, prototype.name, "foliageMesh"), foliageMaterial, VegetationRenderMaterialKind.CanopyFoliage, objectToWorld, worldToObject, packedLeafTint, FoliageShadowPacketMode.SameAsColor, 1f, 1f, $"{tree.DebugName}:TreeL0:Foliage{i}");
                AddBranchPacket(packets, FoliageRepresentationKind.TreeL1, tree, prototype, assetGroups, RequireMesh(prototype.BranchL1WoodMesh, prototype.name, "branchL1WoodMesh"), woodMaterial, VegetationRenderMaterialKind.Trunk, objectToWorld, worldToObject, 0u, FoliageShadowPacketMode.SameAsColor, 1f, 0.05f, $"{tree.DebugName}:TreeL1:Wood{i}");
                AddBranchPacket(packets, FoliageRepresentationKind.TreeL1, tree, prototype, assetGroups, RequireMesh(prototype.BranchL1CanopyMesh, prototype.name, "branchL1CanopyMesh"), shellMaterial, VegetationRenderMaterialKind.CanopyShell, objectToWorld, worldToObject, packedLeafTint, FoliageShadowPacketMode.SameAsColor, 1f, 0.7f, $"{tree.DebugName}:TreeL1:Canopy{i}");
                AddBranchPacket(packets, FoliageRepresentationKind.TreeL2, tree, prototype, assetGroups, RequireMesh(prototype.BranchL2WoodMesh, prototype.name, "branchL2WoodMesh"), woodMaterial, VegetationRenderMaterialKind.Trunk, objectToWorld, worldToObject, 0u, FoliageShadowPacketMode.None, 1f, 0f, $"{tree.DebugName}:TreeL2:Wood{i}");
                AddBranchPacket(packets, FoliageRepresentationKind.TreeL2, tree, prototype, assetGroups, RequireMesh(prototype.BranchL2CanopyMesh, prototype.name, "branchL2CanopyMesh"), shellMaterial, VegetationRenderMaterialKind.CanopyShell, objectToWorld, worldToObject, packedLeafTint, FoliageShadowPacketMode.None, 1f, 0.35f, $"{tree.DebugName}:TreeL2:Canopy{i}");
            }
        }

        private static void AddBranchPacket(
            List<FoliageCompiledPacketDraft> packets,
            FoliageRepresentationKind representationKind,
            FoliageCompiledTreeRecord tree,
            BranchPrototypeSO prototype,
            FoliageCompiledAssetGroupRegistry assetGroups,
            Mesh mesh,
            Material material,
            VegetationRenderMaterialKind materialKind,
            Matrix4x4 objectToWorld,
            Matrix4x4 worldToObject,
            uint packedLeafTint,
            FoliageShadowPacketMode shadowMode,
            float trunkBendWeight,
            float branchFlutterWeight,
            string debugLabel)
        {
            Bounds worldBounds = VegetationRuntimeMathUtility.TransformBounds(mesh.bounds, objectToWorld);
            AddPacket(
                packets,
                representationKind,
                tree,
                assetGroups.Register(mesh, material, materialKind, $"{prototype.name}:{mesh.name}"),
                objectToWorld,
                worldToObject,
                worldBounds,
                packedLeafTint,
                ComputeWorkCost(mesh),
                FoliagePacketResidency.NearDetail,
                shadowMode,
                CreateWindMetadata(tree.SourceTreeIndex, tree.WorldBounds, trunkBendWeight, branchFlutterWeight),
                debugLabel);
        }

        private static void AddPacket(
            List<FoliageCompiledPacketDraft> packets,
            FoliageRepresentationKind representationKind,
            FoliageCompiledTreeRecord tree,
            int assetGroupIndex,
            Matrix4x4 objectToWorld,
            Matrix4x4 worldToObject,
            Bounds worldBounds,
            uint packedLeafTint,
            int workCost,
            FoliagePacketResidency residency,
            FoliageShadowPacketMode shadowMode,
            FoliageWindMetadata windMetadata,
            string debugLabel)
        {
            packets.Add(new FoliageCompiledPacketDraft(
                representationKind,
                tree.SourceTreeIndex,
                tree.PageCellIndex,
                assetGroupIndex,
                new FoliagePacketInstance(
                    tree.SourceTreeIndex,
                    assetGroupIndex,
                    objectToWorld,
                    worldToObject,
                    worldBounds,
                    packedLeafTint,
                    windMetadata),
                worldBounds,
                workCost,
                residency,
                shadowMode,
                debugLabel));
        }

        private static void BuildContiguousPackets(
            List<FoliageCompiledPacketDraft> drafts,
            out FoliagePacketInstance[] instances,
            out FoliageRepresentationPacket[] packets)
        {
            drafts.Sort(ComparePackets);
            List<FoliagePacketInstance> instanceList = new List<FoliagePacketInstance>(drafts.Count);
            List<FoliageRepresentationPacket> packetList = new List<FoliageRepresentationPacket>();
            int draftIndex = 0;
            while (draftIndex < drafts.Count)
            {
                FoliageCompiledPacketDraft draft = drafts[draftIndex];
                int firstInstance = instanceList.Count;
                Bounds packetBounds = draft.WorldBounds;
                int workCost = 0;
                int instanceCount = 0;
                int sourceTreeIndex = draft.SourceTreeIndex;
                while (draftIndex < drafts.Count && CanMergePacket(draft, drafts[draftIndex]))
                {
                    FoliageCompiledPacketDraft mergedDraft = drafts[draftIndex];
                    if (instanceCount > 0)
                    {
                        packetBounds.Encapsulate(mergedDraft.WorldBounds);
                    }

                    instanceList.Add(mergedDraft.Instance);
                    workCost += mergedDraft.WorkCost;
                    if (sourceTreeIndex != mergedDraft.SourceTreeIndex)
                    {
                        sourceTreeIndex = -1;
                    }

                    instanceCount++;
                    draftIndex++;
                }

                string debugLabel = instanceCount == 1
                    ? draft.DebugLabel
                    : $"{draft.DebugLabel}+{instanceCount - 1}";
                int packetIndex = packetList.Count;
                int shadowPacketIndex = draft.ShadowMode == FoliageShadowPacketMode.None ? -1 : packetIndex;
                packetList.Add(new FoliageRepresentationPacket(
                    draft.RepresentationKind,
                    sourceTreeIndex,
                    draft.CellIndex,
                    draft.AssetGroupIndex,
                    firstInstance,
                    instanceCount,
                    packetBounds,
                    workCost,
                    draft.Residency,
                    draft.ShadowMode,
                    shadowPacketIndex,
                    debugLabel));
            }

            instances = instanceList.ToArray();
            packets = packetList.ToArray();
        }

        private static bool CanMergePacket(FoliageCompiledPacketDraft left, FoliageCompiledPacketDraft right)
        {
            return left.AssetGroupIndex == right.AssetGroupIndex &&
                   left.CellIndex == right.CellIndex &&
                   left.RepresentationKind == right.RepresentationKind &&
                   left.Residency == right.Residency &&
                   left.ShadowMode == right.ShadowMode;
        }

        private static void ValidateCompiledPackets(FoliageRepresentationPacket[] packets, List<string> failures)
        {
            for (int i = 0; i < packets.Length; i++)
            {
                FoliageRepresentationPacket packet = packets[i];
                if (packet.ShadowMode != FoliageShadowPacketMode.None)
                {
                    if (packet.ShadowPacketIndex < 0 || packet.ShadowPacketIndex >= packets.Length)
                    {
                        failures.Add($"{packet.DebugLabel} has invalid shadow packet index {packet.ShadowPacketIndex}.");
                    }
                    else if (!ContainsBounds(packet.WorldBounds, packets[packet.ShadowPacketIndex].WorldBounds, 0.001f))
                    {
                        failures.Add($"{packet.DebugLabel} shadow bounds exceed the allowed color packet bounds.");
                    }
                }
            }
        }

        private static bool ContainsBounds(Bounds outer, Bounds inner, float tolerance)
        {
            Vector3 outerMin = outer.min - Vector3.one * tolerance;
            Vector3 outerMax = outer.max + Vector3.one * tolerance;
            Vector3 innerMin = inner.min;
            Vector3 innerMax = inner.max;
            return innerMin.x >= outerMin.x && innerMin.y >= outerMin.y && innerMin.z >= outerMin.z &&
                   innerMax.x <= outerMax.x && innerMax.y <= outerMax.y && innerMax.z <= outerMax.z;
        }

        private static long EstimatePacketBytes(
            FoliageRepresentationPacket[] packets,
            FoliagePacketResidency residency)
        {
            long bytes = 0L;
            for (int i = 0; i < packets.Length; i++)
            {
                FoliageRepresentationPacket packet = packets[i];
                if (packet.Residency == residency)
                {
                    bytes += EstimatedPacketBytes + packet.InstanceCount * EstimatedInstanceBytes;
                }
            }

            return bytes;
        }

        private static FoliageWindMetadata CreateWindMetadata(
            int seed,
            Bounds worldBounds,
            float trunkBendWeight,
            float branchFlutterWeight)
        {
            float phase = Mathf.Repeat(seed * 0.61803398875f + worldBounds.center.x * 0.013f + worldBounds.center.z * 0.017f, 1f);
            return new FoliageWindMetadata(phase, trunkBendWeight, branchFlutterWeight, worldBounds.min.y);
        }

        private static FoliageCompilerBuildReport BuildAssemblyReport(
            IReadOnlyList<FoliagePageAsset> pages,
            IReadOnlyList<FoliageAssetGroup> assetGroups,
            int branchTemplateCount,
            int treeCount,
            int pageSplitCount,
            List<string> messages)
        {
            int assetGroupCount = assetGroups.Count;
            int cellCount = 0;
            int packetCount = 0;
            int instanceCount = 0;
            long alwaysResidentBytes = EstimateAssetGroupResidentBytes(assetGroups) +
                                       branchTemplateCount * EstimatedBranchTemplateBytes;
            long nearDetailColdBytes = 0L;
            long maxPageBytes = 0L;
            long maxCellBytes = 0L;
            int shadowCommandUpperBound = 0;
            int maxPacketCountPerPage = 0;
            int maxPacketCountPerCell = 0;
            for (int i = 0; i < pages.Count; i++)
            {
                FoliageCompilerBuildReport? pageReport = pages[i].BuildReport;
                if (pageReport == null)
                {
                    continue;
                }

                cellCount += pageReport.CellCount;
                packetCount += pageReport.PacketCount;
                instanceCount += pageReport.StaticInstanceCount;
                alwaysResidentBytes += pageReport.AlwaysResidentBytes;
                nearDetailColdBytes += pageReport.NearDetailColdBytes;
                maxPageBytes = Math.Max(maxPageBytes, pageReport.MaxPageBytes);
                maxCellBytes = Math.Max(maxCellBytes, pageReport.MaxCellBytes);
                shadowCommandUpperBound += pageReport.ShadowCommandUpperBound;
                maxPacketCountPerPage = Math.Max(maxPacketCountPerPage, pageReport.MaxPacketCountPerPage);
                maxPacketCountPerCell = Math.Max(maxPacketCountPerCell, pageReport.MaxPacketCountPerCell);
            }

            return BuildReport(
                pages.Count,
                cellCount,
                treeCount,
                packetCount,
                assetGroupCount,
                instanceCount,
                alwaysResidentBytes,
                nearDetailColdBytes,
                maxPageBytes,
                maxCellBytes,
                packetCount,
                shadowCommandUpperBound,
                maxPacketCountPerPage,
                maxPacketCountPerCell,
                pageSplitCount,
                messages.Count,
                messages.ToArray());
        }

        private static FoliageCompilerBuildReport BuildReport(
            int pageCount,
            int cellCount,
            int treeCount,
            int packetCount,
            int assetGroupCount,
            int staticInstanceCount,
            long alwaysResidentBytes,
            long nearDetailColdBytes,
            long maxPageBytes,
            long maxCellBytes,
            int colorCommandUpperBound,
            int shadowCommandUpperBound,
            int maxPacketCountPerPage,
            int maxPacketCountPerCell,
            int pageSplitCount,
            int validationFailureCount,
            string[] messages)
        {
            return new FoliageCompilerBuildReport(
                pageCount,
                cellCount,
                treeCount,
                packetCount,
                assetGroupCount,
                staticInstanceCount,
                alwaysResidentBytes,
                nearDetailColdBytes,
                maxPageBytes,
                maxCellBytes,
                colorCommandUpperBound,
                shadowCommandUpperBound,
                maxPacketCountPerPage,
                maxPacketCountPerCell,
                pageSplitCount,
                validationFailureCount,
                messages);
        }

        private static FoliageCompilationResult CreateFailedResult(List<string> failures)
        {
            FoliageCompilerBuildReport report = BuildReport(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                failures.Count,
                failures.ToArray());
            return new FoliageCompilationResult(null, Array.Empty<FoliagePageAsset>(), report, failures.ToArray());
        }

        private static Mesh RequireMesh(Mesh? mesh, string ownerName, string fieldName)
        {
            return mesh != null
                ? mesh
                : throw new InvalidOperationException($"{ownerName} is missing {fieldName}.");
        }

        private static Material RequireMaterial(Material? material, string ownerName, string fieldName)
        {
            return material != null
                ? material
                : throw new InvalidOperationException($"{ownerName} is missing {fieldName}.");
        }

        private static int ComputeWorkCost(Mesh mesh)
        {
            int indexCount = checked((int)Math.Max(1u, mesh.GetIndexCount(0)));
            return Math.Max(1, (indexCount + (IndirectWorkCostIndexQuantum - 1)) / IndirectWorkCostIndexQuantum);
        }

        private static long EstimateAssetGroupResidentBytes(IReadOnlyList<FoliageAssetGroup> assetGroups)
        {
            long total = assetGroups.Count * EstimatedAssetGroupBytes;
            HashSet<int> countedMeshes = new HashSet<int>();
            for (int i = 0; i < assetGroups.Count; i++)
            {
                Mesh mesh = assetGroups[i].Mesh;
                if (countedMeshes.Add(mesh.GetInstanceID()))
                {
                    total += EstimateMeshResidentBytes(mesh);
                }
            }

            return total;
        }

        private static long EstimateMeshResidentBytes(Mesh mesh)
        {
            long indexCount = 0L;
            for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
            {
                indexCount += mesh.GetIndexCount(subMeshIndex);
            }

            long indexBytes = indexCount *
                              (mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16 ? 2L : 4L);
            return mesh.vertexCount * EstimatedMeshVertexBytes + indexBytes;
        }

        private static void GetCellCoordinate(
            Vector3 worldPosition,
            Vector3 gridOrigin,
            Vector3 cellSize,
            out int cellX,
            out int cellY,
            out int cellZ)
        {
            cellX = Mathf.FloorToInt((worldPosition.x - gridOrigin.x) / cellSize.x);
            cellY = Mathf.FloorToInt((worldPosition.y - gridOrigin.y) / cellSize.y);
            cellZ = Mathf.FloorToInt((worldPosition.z - gridOrigin.z) / cellSize.z);
        }

        private static int CompareByCellThenTree(FoliageCompiledTreeRecord left, FoliageCompiledTreeRecord right)
        {
            int compare = left.CellX.CompareTo(right.CellX);
            if (compare != 0)
            {
                return compare;
            }

            compare = left.CellY.CompareTo(right.CellY);
            if (compare != 0)
            {
                return compare;
            }

            compare = left.CellZ.CompareTo(right.CellZ);
            return compare != 0 ? compare : left.SourceTreeIndex.CompareTo(right.SourceTreeIndex);
        }

        private static int ComparePackets(FoliageCompiledPacketDraft left, FoliageCompiledPacketDraft right)
        {
            int compare = left.AssetGroupIndex.CompareTo(right.AssetGroupIndex);
            if (compare != 0)
            {
                return compare;
            }

            compare = left.CellIndex.CompareTo(right.CellIndex);
            if (compare != 0)
            {
                return compare;
            }

            compare = left.RepresentationKind.CompareTo(right.RepresentationKind);
            if (compare != 0)
            {
                return compare;
            }

            compare = left.Residency.CompareTo(right.Residency);
            if (compare != 0)
            {
                return compare;
            }

            compare = left.ShadowMode.CompareTo(right.ShadowMode);
            if (compare != 0)
            {
                return compare;
            }

            compare = left.SourceTreeIndex.CompareTo(right.SourceTreeIndex);
            return compare != 0 ? compare : string.CompareOrdinal(left.DebugLabel, right.DebugLabel);
        }

        private static bool IsSameCell(FoliageCompiledTreeRecord left, FoliageCompiledTreeRecord right)
        {
            return left.CellX == right.CellX &&
                   left.CellY == right.CellY &&
                   left.CellZ == right.CellZ;
        }

        private static long EstimatePageBytes(int cellCount, int treeCount, int packetCount, int instanceCount)
        {
            return cellCount * EstimatedCellBytes +
                   treeCount * EstimatedTreeBytes +
                   packetCount * EstimatedPacketBytes +
                   instanceCount * EstimatedInstanceBytes;
        }

        private static long EstimateMaxCellBytes(FoliagePageCell[] cells, FoliageRepresentationPacket[] packets)
        {
            int maxTrees = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                maxTrees = Math.Max(maxTrees, cells[i].TreeCount);
            }

            return maxTrees * EstimatedTreeBytes +
                   CountMaxPacketBytesPerCell(packets);
        }

        private static long CountMaxPacketBytesPerCell(FoliageRepresentationPacket[] packets)
        {
            Dictionary<int, long> bytesByCell = new Dictionary<int, long>();
            long maxBytes = 0L;
            for (int i = 0; i < packets.Length; i++)
            {
                FoliageRepresentationPacket packet = packets[i];
                int cellIndex = packet.CellIndex;
                bytesByCell.TryGetValue(cellIndex, out long bytes);
                bytes += EstimatedPacketBytes + packet.InstanceCount * EstimatedInstanceBytes;
                bytesByCell[cellIndex] = bytes;
                maxBytes = Math.Max(maxBytes, bytes);
            }

            return maxBytes;
        }

        private static int CountMaxPacketsPerCell(FoliageRepresentationPacket[] packets)
        {
            Dictionary<int, int> countsByCell = new Dictionary<int, int>();
            int maxCount = 0;
            for (int i = 0; i < packets.Length; i++)
            {
                int cellIndex = packets[i].CellIndex;
                countsByCell.TryGetValue(cellIndex, out int count);
                count++;
                countsByCell[cellIndex] = count;
                maxCount = Math.Max(maxCount, count);
            }

            return maxCount;
        }

        private static int CountShadowCompatiblePackets(FoliageRepresentationPacket[] packets)
        {
            int count = 0;
            for (int i = 0; i < packets.Length; i++)
            {
                if (packets[i].ShadowMode != FoliageShadowPacketMode.None)
                {
                    count++;
                }
            }

            return count;
        }

        private static string NormalizeAssetFolder(string outputFolder)
        {
            string normalized = string.IsNullOrWhiteSpace(outputFolder)
                ? "Assets/VoxGeoFol.Generated/Vegetation/CompiledPages"
                : outputFolder.Replace('\\', '/').TrimEnd('/');
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) && normalized != "Assets")
            {
                throw new ArgumentException("Foliage compiler output folder must be under Assets/.", nameof(outputFolder));
            }

            return normalized;
        }

        private static void CreateAssetFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private static T LoadOrCreateAsset<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static FoliageAssetGroup[] BuildPersistentAssetGroups(
            IReadOnlyList<FoliageAssetGroup> source,
            string outputFolder,
            string safeContainerName)
        {
            FoliageAssetGroup[] result = new FoliageAssetGroup[source.Count];
            string meshFolder = $"{outputFolder}/HLODMeshes";
            List<string> currentMeshPaths = new List<string>();
            for (int i = 0; i < source.Count; i++)
            {
                FoliageAssetGroup group = source[i];
                Mesh mesh = group.Mesh;
                if (IsGeneratedHlodMesh(mesh))
                {
                    CreateAssetFolder(meshFolder);
                    string meshPath = $"{meshFolder}/{safeContainerName}_{MakeSafeAssetName(mesh.name)}.asset";
                    currentMeshPaths.Add(meshPath);
                    mesh = PersistGeneratedMesh(mesh, meshPath);
                }

                result[i] = new FoliageAssetGroup(
                    mesh,
                    group.Material,
                    group.MaterialKind,
                    group.ForwardPassIndex,
                    group.DepthPassIndex,
                    group.ShadowPassIndex,
                    group.DebugLabel);
            }

            if (AssetDatabase.IsValidFolder(meshFolder))
            {
                DeleteStaleHlodMeshes(meshFolder, safeContainerName, currentMeshPaths);
                DeleteEmptyAssetFolder(meshFolder);
            }

            return result;
        }

        private static bool IsGeneratedHlodMesh(Mesh mesh)
        {
            return (mesh.hideFlags & HideFlags.DontSave) != 0 &&
                   (mesh.name.Contains(nameof(FoliageRepresentationKind.PageHLOD)) ||
                    mesh.name.Contains(nameof(FoliageRepresentationKind.CellHLOD)));
        }

        private static Mesh PersistGeneratedMesh(Mesh sourceMesh, string path)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = UnityEngine.Object.Instantiate(sourceMesh);
                mesh.name = sourceMesh.name;
                mesh.hideFlags = HideFlags.None;
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            EditorUtility.CopySerialized(sourceMesh, mesh);
            mesh.name = sourceMesh.name;
            mesh.hideFlags = HideFlags.None;
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static void DeleteStaleHlodMeshes(string meshFolder, string safeContainerName, List<string> currentMeshPaths)
        {
            HashSet<string> keepPaths = new HashSet<string>(currentMeshPaths, StringComparer.Ordinal);
            string[] meshGuids = AssetDatabase.FindAssets("t:Mesh", new[] { meshFolder });
            string expectedPrefix = $"{safeContainerName}_";
            for (int i = 0; i < meshGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(meshGuids[i]);
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (fileName.StartsWith(expectedPrefix, StringComparison.Ordinal) && !keepPaths.Contains(path))
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }
        }

        private static void DeleteEmptyAssetFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string[] assetGuids = AssetDatabase.FindAssets(string.Empty, new[] { folder });
            if (assetGuids.Length == 0)
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static void DeleteStalePageAssets(string outputFolder, string safeContainerName, List<string> currentPagePaths)
        {
            HashSet<string> keepPaths = new HashSet<string>(currentPagePaths, StringComparer.Ordinal);
            string[] pageGuids = AssetDatabase.FindAssets("t:FoliagePageAsset", new[] { outputFolder });
            string expectedPrefix = $"{safeContainerName}_Page_";
            for (int i = 0; i < pageGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(pageGuids[i]);
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (fileName.StartsWith(expectedPrefix, StringComparison.Ordinal) && !keepPaths.Contains(path))
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }
        }

        private static string MakeSafeAssetName(string name)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            char[] buffer = name.ToCharArray();
            for (int i = 0; i < buffer.Length; i++)
            {
                for (int invalidIndex = 0; invalidIndex < invalidChars.Length; invalidIndex++)
                {
                    if (buffer[i] == invalidChars[invalidIndex])
                    {
                        buffer[i] = '_';
                        break;
                    }
                }
            }

            string safeName = new string(buffer).Trim();
            return safeName.Length > 0 ? safeName : "VegetationContainer";
        }

        private static T[] CopyToArray<T>(IReadOnlyList<T> source)
        {
            T[] result = new T[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                result[i] = source[i];
            }

            return result;
        }
    }
}
