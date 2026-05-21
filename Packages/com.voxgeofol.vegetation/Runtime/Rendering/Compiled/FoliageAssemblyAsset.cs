#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Runtime-readable compiled foliage assembly shared by all generated pages for one container.
    /// </summary>
    public sealed class FoliageAssemblyAsset : ScriptableObject
    {
        public const int CompilerVersion = 1;

        [SerializeField] private int compilerVersion = CompilerVersion;
        [SerializeField] private string sourceContainerId = string.Empty;
        [SerializeField] private FoliageAssetGroup[] assetGroups = Array.Empty<FoliageAssetGroup>();
        [SerializeField] private FoliageBranchPlacementTemplate[] branchPlacementTemplates = Array.Empty<FoliageBranchPlacementTemplate>();
        [SerializeField] private FoliageCompilerBuildReport? buildReport;

        public int Version => compilerVersion;

        public string SourceContainerId => sourceContainerId;

        public IReadOnlyList<FoliageAssetGroup> AssetGroups => assetGroups;

        public IReadOnlyList<FoliageBranchPlacementTemplate> BranchPlacementTemplates => branchPlacementTemplates;

        public FoliageCompilerBuildReport? BuildReport => buildReport;

        /// <summary>
        /// [INTEGRATION] Replaces generated compiler payload without preserving obsolete runtime fields.
        /// </summary>
        public void InitializeForCompiler(
            string containerId,
            FoliageAssetGroup[] compiledAssetGroups,
            FoliageBranchPlacementTemplate[] compiledBranchPlacementTemplates,
            FoliageCompilerBuildReport report)
        {
            // Range: arrays are final compiler-owned output. Condition: caller already validated page caps and source authoring. Output: a single runtime-readable assembly asset with no legacy shadow-proxy slots.
            compilerVersion = CompilerVersion;
            sourceContainerId = containerId ?? string.Empty;
            assetGroups = compiledAssetGroups ?? Array.Empty<FoliageAssetGroup>();
            branchPlacementTemplates = compiledBranchPlacementTemplates ?? Array.Empty<FoliageBranchPlacementTemplate>();
            buildReport = report ?? throw new ArgumentNullException(nameof(report));
        }
    }
}
