#nullable enable

using System.Collections.Generic;

namespace VoxGeoFol.Features.Vegetation.Authoring
{
    /// <summary>
    /// Aggregated validation output for vegetation authoring assets.
    /// </summary>
    public sealed class VegetationValidationResult
    {
        private readonly List<VegetationValidationIssue> issues = new List<VegetationValidationIssue>();

        public IReadOnlyList<VegetationValidationIssue> Issues => issues;

        public bool HasErrors { get; private set; }

        public bool HasWarnings { get; private set; }

        public void AddError(string message)
        {
            issues.Add(new VegetationValidationIssue(VegetationValidationSeverity.Error, message));
            HasErrors = true;
        }

        public void AddWarning(string message)
        {
            issues.Add(new VegetationValidationIssue(VegetationValidationSeverity.Warning, message));
            HasWarnings = true;
        }

        public void Merge(VegetationValidationResult other)
        {
            for (int i = 0; i < other.issues.Count; i++)
            {
                VegetationValidationIssue issue = other.issues[i];
                issues.Add(issue);
                HasErrors |= issue.Severity == VegetationValidationSeverity.Error;
                HasWarnings |= issue.Severity == VegetationValidationSeverity.Warning;
            }
        }
    }
}
