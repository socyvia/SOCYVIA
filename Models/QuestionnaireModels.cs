using System;
using System.Collections.Generic;

namespace SOCYVIA.Models;

public static class QuestionnaireQuestionTypes
{
    public const string Likert = "LIKERT";
    public const string SingleChoice = "SINGLE_CHOICE";
    public const string MultipleChoice = "MULTIPLE_CHOICE";
    public const string YesNo = "YES_NO";
    public const string Numeric = "NUMERIC";
    public const string ShortText = "SHORT_TEXT";
    public const string LongText = "LONG_TEXT";

    public static readonly string[] Supported =
    [Likert, SingleChoice, MultipleChoice, YesNo, Numeric, ShortText, LongText];
}

public static class QuestionnairePlacements
{
    public const string PreExperiment = "PRE_EXPERIMENT";
    public const string PostExperiment = "POST_EXPERIMENT";
    public const string MidExperiment = "MID_EXPERIMENT";
}

public static class QuestionnaireLicenseStatuses
{
    public const string BuiltInOpen = "BUILT_IN_OPEN";
    public const string MetadataOnly = "METADATA_ONLY";
    public const string UserProvided = "USER_PROVIDED";
    public const string Custom = "CUSTOM";
    public const string LicenseRequired = "LICENSE_REQUIRED";
}

public sealed class Questionnaire
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public string? CurrentVersionId { get; set; }
    public string InstrumentType { get; set; } = QuestionnaireLicenseStatuses.Custom;
    public string? MetadataJson { get; set; }
    public List<QuestionnaireVersion> Versions { get; set; } = [];
}

public sealed class QuestionnaireVersion
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string QuestionnaireId { get; set; } = string.Empty;
    public int VersionNumber { get; set; } = 1;
    public string? VersionLabel { get; set; }
    public string Status { get; set; } = "Draft";
    public string Language { get; set; } = "en";
    public string InstrumentType { get; set; } = QuestionnaireLicenseStatuses.Custom;
    public string? Construct { get; set; }
    public string? Citation { get; set; }
    public string LicenseStatus { get; set; } = QuestionnaireLicenseStatuses.Custom;
    public string? LicenseReference { get; set; }
    public string RedistributionStatus { get; set; } = QuestionnaireLicenseStatuses.UserProvided;
    public string? ValidationNotes { get; set; }
    public string? TranslationNotes { get; set; }
    public string? ScoringAvailability { get; set; }
    public string? SchemaHash { get; set; }
    public bool IsImmutable { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAtUtc { get; set; }
    public List<QuestionnaireSection> Sections { get; set; } = [];
    public List<Question> Questions { get; set; } = [];
    public List<QuestionnaireScale> Scales { get; set; } = [];
}

public sealed class QuestionnaireSection
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string QuestionnaireVersionId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
}

public sealed class Question
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string QuestionnaireVersionId { get; set; } = string.Empty;
    public string? SectionId { get; set; }
    public string VariableName { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;
    public string QuestionType { get; set; } = QuestionnaireQuestionTypes.Likert;
    public string MeasurementLevel { get; set; } = "ORDINAL";
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public string? ConfigurationJson { get; set; }
    public List<QuestionOption> Options { get; set; } = [];
}

public sealed class QuestionOption
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string QuestionId { get; set; } = string.Empty;
    public string ValueCode { get; set; } = string.Empty;
    public double? NumericCode { get; set; }
    public string DisplayLabel { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class QuestionnaireAssignment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string QuestionnaireVersionId { get; set; } = string.Empty;
    public string Placement { get; set; } = QuestionnairePlacements.PostExperiment;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Questionnaire? Questionnaire { get; set; }
    public QuestionnaireVersion? Version { get; set; }
}

public sealed class QuestionnaireResponse
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AssignmentId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public string ParticipantId { get; set; } = string.Empty;
    public string QuestionnaireId { get; set; } = string.Empty;
    public string QuestionnaireVersionId { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public long? DurationMilliseconds { get; set; }
    public string Status { get; set; } = "Started";
    public bool IsDemo { get; set; }
    public string? MetadataJson { get; set; }
    public List<QuestionResponse> Responses { get; set; } = [];
}

public sealed class QuestionResponse
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ResponseSetId { get; set; } = string.Empty;
    public string QuestionId { get; set; } = string.Empty;
    public string? RawValue { get; set; }
    public double? NumericValue { get; set; }
    public string? SelectedOptionIdsJson { get; set; }
    public DateTime RespondedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class QuestionnaireScale
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string QuestionnaireVersionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string VariableName { get; set; } = string.Empty;
    public string ScoringMethod { get; set; } = "MEAN";
    public string MissingItemRule { get; set; } = "REQUIRE_MINIMUM";
    public int MinimumAnsweredItems { get; set; } = 1;
    public List<QuestionnaireScaleItem> Items { get; set; } = [];
}

public sealed class QuestionnaireScaleItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ScaleId { get; set; } = string.Empty;
    public string QuestionId { get; set; } = string.Empty;
    public bool IsReverseCoded { get; set; }
    public double? ReverseMinimum { get; set; }
    public double? ReverseMaximum { get; set; }
    public double Weight { get; set; } = 1;
}

public sealed record ScaleScoreResult(
    string ScaleId,
    string VariableName,
    double? Score,
    int AnsweredItems,
    int RequiredItems,
    bool IsComplete,
    IReadOnlyList<string> Warnings);

public sealed class QuestionnaireLibraryEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; init; } = string.Empty;
    public string? ShortName { get; init; }
    public string Category { get; init; } = "Custom";
    public string? Construct { get; init; }
    public string? SourceCitation { get; init; }
    public string? Version { get; init; }
    public string Language { get; init; } = "en";
    public string LicenseStatus { get; init; } = QuestionnaireLicenseStatuses.MetadataOnly;
    public string? LicenseReference { get; init; }
    public string RedistributionStatus { get; init; } = QuestionnaireLicenseStatuses.MetadataOnly;
    public string? ScoringAvailability { get; init; }
    public string? ValidationNotes { get; init; }
    public string? TranslationNotes { get; init; }
    public bool IncludesItems { get; init; }
}
