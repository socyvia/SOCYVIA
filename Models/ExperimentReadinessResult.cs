using System.Collections.Generic;
using System.Linq;

namespace SOCYVIA.Models;

public enum ExperimentReadinessSeverity
{
    Info,
    Warning,
    Error
}


public class ExperimentReadinessCheck
{
    public string Code { get; init; } = string.Empty;
    public ExperimentReadinessSeverity Severity { get; init; }
    public bool IsPassed { get; init; }
    public string MessageKey { get; init; } = string.Empty;
    public string CanonicalMessage { get; init; } = string.Empty;
    public string? RelatedEntityId { get; init; }
}


public class ExperimentReadinessResult
{
    public IReadOnlyList<ExperimentReadinessCheck> Checks { get; init; } =
        new List<ExperimentReadinessCheck>();

    public bool IsReady =>
        Checks.All(check =>
            check.IsPassed ||
            check.Severity != ExperimentReadinessSeverity.Error);

    public int ErrorCount =>
        Checks.Count(check =>
            !check.IsPassed &&
            check.Severity == ExperimentReadinessSeverity.Error);

    public int WarningCount =>
        Checks.Count(check =>
            !check.IsPassed &&
            check.Severity == ExperimentReadinessSeverity.Warning);
}
