#nullable enable

using System;
using System.Collections.Generic;
using VoxGeoFol.Features.Vegetation.Rendering;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Editor compiler result for one vegetation runtime container.
    /// </summary>
    public sealed class FoliageCompilationResult
    {
        public FoliageCompilationResult(
            FoliageAssemblyAsset? assemblyAsset,
            FoliagePageAsset[] pageAssets,
            FoliageCompilerBuildReport buildReport,
            string[] failureMessages)
        {
            AssemblyAsset = assemblyAsset;
            PageAssets = pageAssets ?? Array.Empty<FoliagePageAsset>();
            BuildReport = buildReport ?? throw new ArgumentNullException(nameof(buildReport));
            FailureMessages = failureMessages ?? Array.Empty<string>();
        }

        public bool Succeeded => FailureMessages.Count == 0;

        public FoliageAssemblyAsset? AssemblyAsset { get; }

        public IReadOnlyList<FoliagePageAsset> PageAssets { get; }

        public FoliageCompilerBuildReport BuildReport { get; }

        public IReadOnlyList<string> FailureMessages { get; }
    }
}
