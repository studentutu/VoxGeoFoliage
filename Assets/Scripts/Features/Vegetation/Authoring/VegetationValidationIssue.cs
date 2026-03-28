#nullable enable

namespace VoxGeoFol.Features.Vegetation.Authoring
{
    /// <summary>
    /// One explicit authoring validation message.
    /// </summary>
    public readonly struct VegetationValidationIssue
    {
        public VegetationValidationIssue(VegetationValidationSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }

        public VegetationValidationSeverity Severity { get; }

        public string Message { get; }
    }
}
