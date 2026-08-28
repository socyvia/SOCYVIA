using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

/// <summary>
/// Converts the synchronized, normalized remote records into a frozen analytical dataset.
/// It deliberately includes completed eligible sessions only and retains raw-event provenance in
/// the underlying cache; this derived layer never replaces the source records.
/// </summary>
public static class RemoteAnalysisDatasetService
{
    /// <summary>Builds a completed-eligible dataset for one authoritative experiment run type. Main remains the default.</summary>
    public static async Task<AnalysisDataset> BuildCompletedEligibleAsync(string studyId, ExperimentRunType runType = ExperimentRunType.Main)
    {
        var sessionsTask = RemoteResearchRepository.GetSessionsAsync(completedOnly: true, studyId: studyId, runType: runType);
        var eventsTask = RemoteResearchRepository.GetEventsAsync(completedOnly: true, studyId: studyId, runType: runType);
        var preTask = RemoteResearchRepository.GetQuestionnaireResponsesAsync(QuestionnaireStage.Pre, completedOnly: true, studyId: studyId, runType: runType);
        var postTask = RemoteResearchRepository.GetQuestionnaireResponsesAsync(QuestionnaireStage.Post, completedOnly: true, studyId: studyId, runType: runType);
        var groupsTask = GroupRepository.GetByStudyAsync(studyId);
        var conditionsTask = ExperimentalConditionRepository.GetByStudyAsync(studyId);
        await Task.WhenAll(sessionsTask, eventsTask, preTask, postTask, groupsTask, conditionsTask);

        var groups = groupsTask.Result.ToDictionary(item => item.Id, item => item.Name, StringComparer.Ordinal);
        var conditions = conditionsTask.Result.ToDictionary(item => item.Id, item => item.Name, StringComparer.Ordinal);
        var eventsBySession = eventsTask.Result.GroupBy(item => item.SessionId).ToDictionary(item => item.Key, item => item.ToArray(), StringComparer.Ordinal);
        var preByParticipant = preTask.Result.GroupBy(item => item.ParticipantId).ToDictionary(item => item.Key, item => item.OrderByDescending(value => value.SubmittedAtUtc).First(), StringComparer.Ordinal);
        var postBySession = postTask.Result.Where(item => !string.IsNullOrWhiteSpace(item.SessionId)).GroupBy(item => item.SessionId!).ToDictionary(item => item.Key, item => item.OrderByDescending(value => value.SubmittedAtUtc).First(), StringComparer.Ordinal);
        var rows = new List<AnalysisRow>();
        var variables = new Dictionary<string, AnalysisVariable>(StringComparer.Ordinal);
        var questionnaireVersions = new Dictionary<string, QuestionnaireVersion?>(StringComparer.Ordinal);
        foreach (var definition in CoreVariables()) variables[definition.Id] = definition;
        var duplicateParticipants = new HashSet<string>(StringComparer.Ordinal);
        var exclusions = new List<AnalysisExclusion>();

        foreach (var grouping in sessionsTask.Result.GroupBy(item => item.ParticipantId, StringComparer.Ordinal))
        {
            var selected = grouping.OrderByDescending(item => item.CompletedAtUtc).ThenBy(item => item.SessionId, StringComparer.Ordinal).First();
            foreach (var superseded in grouping.Skip(1))
            {
                duplicateParticipants.Add(superseded.ParticipantId);
                exclusions.Add(new AnalysisExclusion(superseded.ParticipantId, superseded.SessionId, "DUPLICATE_COMPLETED_SESSION", "The latest completed eligible session is retained for participant-level analysis."));
            }
            var values = CoreValues(selected, eventsBySession.GetValueOrDefault(selected.SessionId, []));
            if (preByParticipant.TryGetValue(selected.ParticipantId, out var pre)) await AddQuestionnaireValuesAsync(values, variables, questionnaireVersions, pre, "pre");
            if (postBySession.TryGetValue(selected.SessionId, out var post)) await AddQuestionnaireValuesAsync(values, variables, questionnaireVersions, post, "post");
            rows.Add(new AnalysisRow
            {
                ParticipantId = selected.ParticipantId,
                ParticipantCode = ShortId(selected.ParticipantId),
                SessionId = selected.SessionId,
                GroupId = selected.GroupId,
                GroupName = selected.GroupId is not null && groups.TryGetValue(selected.GroupId, out var group) ? group : selected.GroupId,
                ConditionId = selected.ConditionId,
                ConditionName = conditions.TryGetValue(selected.ConditionId, out var condition) ? condition : selected.ConditionId,
                SessionCompleted = true,
                NumericValues = values,
                CategoricalValues = new Dictionary<string, string?>(StringComparer.Ordinal)
            });
        }

        var orderedVariables = variables.Values.OrderBy(item => item.Source, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
        return new AnalysisDataset
        {
            StudyId = studyId,
            CreatedAtUtc = DateTime.UtcNow,
            Variables = orderedVariables,
            Rows = rows,
            Exclusions = exclusions,
            DatasetHash = ComputeHash(studyId, rows, orderedVariables)
        };
    }

    private static Dictionary<string, double?> CoreValues(RemoteParticipantSessionContract session, IReadOnlyList<RemoteTelemetryEvent> events)
    {
        var values = new Dictionary<string, double?>(StringComparer.Ordinal)
        {
            ["session_duration_seconds"] = session.StartedAtUtc.HasValue && session.CompletedAtUtc.HasValue ? (session.CompletedAtUtc.Value - session.StartedAtUtc.Value).TotalSeconds : null,
            ["qualified_impressions"] = events.Count(item => item.EventType == "content_impression"),
            ["content_opens"] = events.Count(item => item.EventType == "content_open"),
            ["read_more_opens"] = events.Count(item => item.EventType == "read_more_open"),
            ["likes"] = events.Count(item => item.EventType == "like"),
            ["comments"] = events.Count(item => item.EventType == "comment_submit"),
            ["saves"] = events.Count(item => item.EventType == "save"),
            ["shares"] = events.Count(item => item.EventType == "share"),
            ["feed_end_reached"] = events.Any(item => item.EventType == "experiment_feed_end") ? 1 : 0,
            ["qualified_exposure_seconds"] = events.Where(item => item.EventType == "content_impression").Sum(ReadQualifiedMilliseconds) / 1000d
        };
        return values;
    }

    private static double ReadQualifiedMilliseconds(RemoteTelemetryEvent item)
    {
        if (string.IsNullOrWhiteSpace(item.PayloadJson)) return 0;
        try
        {
            using var document = JsonDocument.Parse(item.PayloadJson);
            return document.RootElement.TryGetProperty("qualifiedVisibleMs", out var value) && value.TryGetDouble(out var milliseconds) && milliseconds >= 0 ? milliseconds : 0;
        }
        catch (JsonException) { return 0; }
    }

    private static async Task AddQuestionnaireValuesAsync(IDictionary<string, double?> values, IDictionary<string, AnalysisVariable> variables, IDictionary<string, QuestionnaireVersion?> questionnaireVersions, RemoteQuestionnaireResponseContract response, string stage)
    {
        try
        {
            if (!questionnaireVersions.TryGetValue(response.QuestionnaireVersionId, out var version))
            {
                version = await QuestionnaireRepository.GetVersionAsync(response.QuestionnaireVersionId);
                questionnaireVersions[response.QuestionnaireVersionId] = version;
            }
            using var document = JsonDocument.Parse(response.ResponseJson);
            foreach (var answer in document.RootElement.EnumerateObject())
            {
                if (!TryNumber(answer.Value, out var number)) continue;
                var id = $"{stage}:{response.QuestionnaireVersionId}:{answer.Name}";
                var question = version?.Questions.FirstOrDefault(item => item.Id == answer.Name);
                values[id] = number;
                variables.TryAdd(id, new AnalysisVariable
                {
                    Id = id,
                    Name = question?.QuestionText ?? $"{stage.ToUpperInvariant()} · {answer.Name}",
                    Source = "REMOTE_QUESTIONNAIRE",
                    Role = VariableRoles.Outcome,
                    DataType = "Double",
                    MeasurementLevel = question?.MeasurementLevel ?? MeasurementLevels.Ordinal,
                    Definition = question?.QuestionText ?? "Researcher-authored questionnaire item. Numeric coding is preserved as synchronized; item wording/version remain in the response export."
                });
            }
        }
        catch (JsonException) { /* Malformed remote response is excluded from numeric analysis, never converted to zero. */ }
    }

    private static bool TryNumber(JsonElement value, out double number)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number)) return double.IsFinite(number);
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return double.IsFinite(number);
        number = default; return false;
    }

    private static IEnumerable<AnalysisVariable> CoreVariables()
    {
        yield return Variable("session_duration_seconds", "Session duration", MeasurementLevels.Continuous, "seconds");
        yield return Variable("qualified_impressions", "Qualified impressions", MeasurementLevels.Count);
        yield return Variable("qualified_exposure_seconds", "Qualified exposure duration", MeasurementLevels.Continuous, "seconds");
        yield return Variable("content_opens", "Content opens", MeasurementLevels.Count);
        yield return Variable("read_more_opens", "Read More opens", MeasurementLevels.Count);
        yield return Variable("likes", "Likes", MeasurementLevels.Count);
        yield return Variable("comments", "Comments", MeasurementLevels.Count);
        yield return Variable("saves", "Saves", MeasurementLevels.Count);
        yield return Variable("shares", "Shares", MeasurementLevels.Count);
        yield return Variable("feed_end_reached", "Feed end reached", MeasurementLevels.Binary);
    }

    private static AnalysisVariable Variable(string id, string name, string level, string? unit = null) => new()
    {
        Id = id, Name = name, Source = "REMOTE_BEHAVIORAL_TELEMETRY", Role = VariableRoles.Outcome, DataType = "Double", MeasurementLevel = level, Unit = unit,
        Definition = id == "qualified_impressions" ? "Content items meeting the configured visibility and duration rule; this is not a direct measure of attention." : null
    };

    private static string ShortId(string value) => value.Length <= 10 ? value : value[..8] + "…";
    private static string ComputeHash(string studyId, IEnumerable<AnalysisRow> rows, IEnumerable<AnalysisVariable> variables)
    {
        var canonical = JsonSerializer.Serialize(new { StudyId = studyId, Engine = ScientificEngineMetadata.Version, Variables = variables.Select(item => new { item.Id, item.Source, item.MeasurementLevel }), Rows = rows.OrderBy(item => item.ParticipantId).Select(item => new { item.ParticipantId, item.SessionId, item.GroupId, item.ConditionId, Numeric = item.NumericValues.OrderBy(pair => pair.Key) }) });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
