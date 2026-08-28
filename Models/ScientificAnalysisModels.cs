using System;
using System.Collections.Generic;

namespace SOCYVIA.Models;

public static class ScientificEngineMetadata
{
    public const string Version = "SOCYVIA.SCIENCE/1.0";
}

public static class MeasurementLevels
{
    public const string Nominal = "NOMINAL";
    public const string Ordinal = "ORDINAL";
    public const string Continuous = "CONTINUOUS";
    public const string Count = "COUNT";
    public const string Binary = "BINARY";
}

public static class VariableRoles
{
    public const string Outcome = "OUTCOME";
    public const string Predictor = "PREDICTOR";
    public const string Grouping = "GROUPING";
    public const string Covariate = "COVARIATE";
    public const string Id = "ID";
    public const string Time = "TIME";
    public const string Descriptive = "DESCRIPTIVE";
}

public static class AnalysisStatuses
{
    public const string Ready = "READY";
    public const string Computed = "COMPUTED";
    public const string InsufficientData = "INSUFFICIENT_DATA";
    public const string UnsupportedDesign = "UNSUPPORTED_DESIGN";
    public const string InvalidConfiguration = "INVALID_CONFIGURATION";
    public const string ComputationError = "COMPUTATION_ERROR";
}

public static class AnalysisClassifications
{
    public const string Primary = "PRIMARY";
    public const string Secondary = "SECONDARY";
    public const string Exploratory = "EXPLORATORY";
}

public sealed class AnalysisVariable
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Role { get; init; } = VariableRoles.Descriptive;
    public string DataType { get; init; } = "Double";
    public string MeasurementLevel { get; init; } = MeasurementLevels.Continuous;
    public string? Unit { get; init; }
    public string? MissingnessDefinition { get; init; }
    public string? GroupOrConditionRelation { get; init; }
    public string? Definition { get; init; }
}

public sealed class AnalysisRow
{
    public string ParticipantId { get; init; } = string.Empty;
    public string ParticipantCode { get; init; } = string.Empty;
    public string? GroupId { get; init; }
    public string? GroupName { get; init; }
    public string? ConditionId { get; init; }
    public string? ConditionName { get; init; }
    public string? SessionId { get; init; }
    public bool SessionCompleted { get; init; }
    public bool IsDemo { get; init; }
    public Dictionary<string, double?> NumericValues { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string?> CategoricalValues { get; init; } = new(StringComparer.Ordinal);
}

public sealed class AnalysisDataset
{
    public string StudyId { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public string DatasetHash { get; init; } = string.Empty;
    public string EngineVersion { get; init; } = ScientificEngineMetadata.Version;
    public IReadOnlyList<AnalysisVariable> Variables { get; init; } = [];
    public IReadOnlyList<AnalysisRow> Rows { get; init; } = [];
    public IReadOnlyList<AnalysisExclusion> Exclusions { get; init; } = [];
    public bool IsDemo { get; init; }
}

public sealed record AnalysisExclusion(
    string? ParticipantId,
    string? SessionId,
    string ReasonCode,
    string ReasonDetail);

public sealed class DataQualityResult
{
    public int TotalN { get; init; }
    public int IncludedN { get; init; }
    public int ExcludedN { get; init; }
    public IReadOnlyDictionary<string, int> MissingByVariable { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<string> ConstantVariables { get; init; } = [];
    public IReadOnlyList<string> InsufficientVariationVariables { get; init; } = [];
    public IReadOnlyList<string> DuplicateParticipantWarnings { get; init; } = [];
    public IReadOnlyList<string> IncompleteQuestionnaireWarnings { get; init; } = [];
    public IReadOnlyList<string> SessionWarnings { get; init; } = [];
    public IReadOnlyList<AnalysisExclusion> Exclusions { get; init; } = [];
}

public sealed record ConfidenceInterval(
    double Lower,
    double Upper,
    double ConfidenceLevel,
    string Method);

public sealed record EffectSizeEstimate(
    double Value,
    string Method,
    string Definition,
    ConfidenceInterval? ConfidenceInterval = null);

public sealed class NumericDescriptiveResult
{
    public int N { get; init; }
    public int Missing { get; init; }
    public double? Mean { get; init; }
    public double? StandardDeviation { get; init; }
    public double? Median { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public double? Q1 { get; init; }
    public double? Q3 { get; init; }
    public double? Iqr { get; init; }
    public ConfidenceInterval? MeanConfidenceInterval { get; init; }
}

public sealed record CategoryFrequency(string Value, int Count, double Percentage);

public sealed class AnalysisDiagnostic
{
    public string Code { get; init; } = string.Empty;
    public string Severity { get; init; } = "INFO";
    public bool? IsSatisfied { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, double> Values { get; init; } = new Dictionary<string, double>();
}

public sealed class StatisticalResult
{
    public string Method { get; init; } = string.Empty;
    public string Status { get; init; } = AnalysisStatuses.Computed;
    public int N { get; init; }
    public IReadOnlyDictionary<string, int> GroupNs { get; init; } = new Dictionary<string, int>();
    public double? Estimate { get; init; }
    public double? Statistic { get; init; }
    public double? DegreesOfFreedom { get; init; }
    public double? SecondaryDegreesOfFreedom { get; init; }
    public double? PValue { get; init; }
    public EffectSizeEstimate? EffectSize { get; init; }
    public ConfidenceInterval? ConfidenceInterval { get; init; }
    public IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string CanonicalSummary { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, double> ResultData { get; init; } = new Dictionary<string, double>();
}

public sealed class AnalysisSpecification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ResearchQuestion { get; set; }
    public string Classification { get; set; } = AnalysisClassifications.Exploratory;
    public string OutcomeVariableId { get; set; } = string.Empty;
    public string? PredictorVariableId { get; set; }
    public IReadOnlyList<string> Covariates { get; set; } = [];
    public string AnalysisFamily { get; set; } = "DESCRIPTIVE";
    public string Method { get; set; } = "DESCRIPTIVE";
    public string AlternativeHypothesis { get; set; } = "TWO_SIDED";
    public double ConfidenceLevel { get; set; } = 0.95;
    public string MissingDataHandling { get; set; } = "COMPLETE_CASE";
    public string MultipleComparisonMethod { get; set; } = "NONE";
    public string? ParametersJson { get; set; }
    public string EngineVersion { get; set; } = ScientificEngineMetadata.Version;
    public bool IsDemo { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AnalysisRecommendation
{
    public string Status { get; init; } = AnalysisStatuses.Ready;
    public string RecommendedFamily { get; init; } = string.Empty;
    public string RecommendedMethod { get; init; } = string.Empty;
    public IReadOnlyList<string> Alternatives { get; init; } = [];
    public IReadOnlyList<string> Rationale { get; init; } = [];
    public IReadOnlyList<string> Requirements { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class AnalysisExecution
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AnalysisSpecificationId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string Status { get; set; } = AnalysisStatuses.Ready;
    public string DatasetHash { get; set; } = string.Empty;
    public string DatasetDescriptorJson { get; set; } = string.Empty;
    public StatisticalResult? Result { get; set; }
    public IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; set; } = [];
    public IReadOnlyList<string> Warnings { get; set; } = [];
    public string? ErrorCode { get; set; }
    public string? ErrorDetail { get; set; }
    public string EngineVersion { get; set; } = ScientificEngineMetadata.Version;
    public DateTime ExecutedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsDemo { get; set; }
}

public sealed class ResearchResultPackage
{
    public string PackageVersion { get; init; } = "SOCYVIA.RESULT/1";
    public string StudyId { get; init; } = string.Empty;
    public string StudyDesign { get; init; } = string.Empty;
    public IReadOnlyList<string> Hypotheses { get; init; } = [];
    public IReadOnlyList<string> Groups { get; init; } = [];
    public IReadOnlyList<string> Conditions { get; init; } = [];
    public IReadOnlyList<AnalysisVariable> Variables { get; init; } = [];
    public DataQualityResult DataQuality { get; init; } = new();
    public AnalysisSpecification Specification { get; init; } = new();
    public AnalysisExecution Execution { get; init; } = new();
    public IReadOnlyList<string> AiSafetyContract { get; init; } =
    [
        "Never invent statistics, sample sizes, or citations.",
        "Never change computed values.",
        "Distinguish numerical results from interpretation.",
        "Distinguish association from causation.",
        "State uncertainty, limitations, and insufficient evidence.",
        "Identify exploratory analyses and never treat non-significance as proof of no effect."
    ];
}
