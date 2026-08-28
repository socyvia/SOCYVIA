using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

/// <summary>Normalized CSV boundary. Exports preserve deployment, condition, participant, and session provenance.</summary>
public static class RemoteResearchExportService
{
    public static async Task<string> ExportSessionsCsvAsync(string? conditionId = null, string? studyId = null)
    {
        var rows = await RemoteResearchRepository.GetSessionsAsync(conditionId, studyId: studyId);
        var csv = new StringBuilder("participant_id,session_id,deployment_id,condition_id,run_type,started_at_utc,feed_end_at_utc,post_completed_at_utc,completed_at_utc,completion_state,lifecycle_state\n");
        foreach (var item in rows) csv.AppendLine(string.Join(',', Escape(item.ParticipantId), Escape(item.SessionId), Escape(item.DeploymentId), Escape(item.ConditionId), Escape(item.RunType), Escape(item.StartedAtUtc), Escape(item.FeedEndedAtUtc), Escape(item.PostQuestionnaireCompletedAtUtc), Escape(item.CompletedAtUtc), Escape(item.CompletionState), Escape(item.LifecycleState)));
        return csv.ToString();
    }

    /// <summary>Exports the retained attrition/quality-control sample separately from the eligible analytical dataset.</summary>
    public static async Task<string> ExportIncompleteSessionsCsvAsync(string? conditionId = null, string? studyId = null)
    {
        var rows = (await RemoteResearchRepository.GetSessionsAsync(conditionId, studyId: studyId))
            .Where(item => item.CompletionState != RemoteParticipantCompletionState.CompletedEligible);
        var csv = new StringBuilder("participant_id,session_id,deployment_id,condition_id,started_at_utc,feed_end_at_utc,post_completed_at_utc,completed_at_utc,completion_state,lifecycle_state\n");
        foreach (var item in rows) csv.AppendLine(string.Join(',', Escape(item.ParticipantId), Escape(item.SessionId), Escape(item.DeploymentId), Escape(item.ConditionId), Escape(item.StartedAtUtc), Escape(item.FeedEndedAtUtc), Escape(item.PostQuestionnaireCompletedAtUtc), Escape(item.CompletedAtUtc), Escape(item.CompletionState), Escape(item.LifecycleState)));
        return csv.ToString();
    }

    public static async Task<string> ExportCompletedAnalyticalDatasetCsvAsync(string? conditionId = null, string? studyId = null)
    {
        var rows = (await RemoteResearchRepository.GetSessionsAsync(conditionId, true, studyId)).Where(item => item.RunType == ExperimentRunType.Main);
        var csv = new StringBuilder("participant_id,session_id,deployment_id,condition_id,started_at_utc,feed_end_at_utc,post_completed_at_utc,completed_at_utc,duration_seconds,completion_status\n");
        foreach (var item in rows)
        {
            var duration = item.StartedAtUtc.HasValue && item.CompletedAtUtc.HasValue ? Math.Round((item.CompletedAtUtc.Value - item.StartedAtUtc.Value).TotalSeconds, 3) : (double?)null;
            csv.AppendLine(string.Join(',', Escape(item.ParticipantId), Escape(item.SessionId), Escape(item.DeploymentId), Escape(item.ConditionId), Escape(item.StartedAtUtc), Escape(item.FeedEndedAtUtc), Escape(item.PostQuestionnaireCompletedAtUtc), Escape(item.CompletedAtUtc), Escape(duration), Escape(item.CompletionState)));
        }
        return csv.ToString();
    }

    public static async Task<string> ExportQuestionnaireCsvAsync(QuestionnaireStage stage, string? conditionId = null, bool completedOnly = false, string? studyId = null)
    {
        var rows = await RemoteResearchRepository.GetQuestionnaireResponsesAsync(stage, conditionId, completedOnly, studyId);
        var csv = new StringBuilder("participant_id,session_id,deployment_id,condition_id,run_type,questionnaire_id,questionnaire_version_id,stage,question_id,response,submitted_at_utc\n");
        foreach (var row in rows)
        {
            using var document = JsonDocument.Parse(row.ResponseJson);
            foreach (var answer in document.RootElement.EnumerateObject()) csv.AppendLine(string.Join(',', Escape(row.ParticipantId), Escape(row.SessionId), Escape(row.DeploymentId), Escape(row.ConditionId), Escape(row.RunType), Escape(row.QuestionnaireId), Escape(row.QuestionnaireVersionId), Escape(row.Stage), Escape(answer.Name), Escape(answer.Value.ToString()), Escape(row.SubmittedAtUtc)));
        }
        return csv.ToString();
    }

    public static async Task<string> ExportBehavioralEventsCsvAsync(string? conditionId = null, bool completedOnly = false, string? studyId = null)
    {
        var rows = await RemoteResearchRepository.GetEventsAsync(conditionId, completedOnly, studyId);
        var csv = new StringBuilder("participant_id,session_id,deployment_id,condition_id,run_type,content_id,event_type,relative_time_ms,event_schema_version,payload_json\n");
        foreach (var item in rows) csv.AppendLine(string.Join(',', Escape(item.ParticipantId), Escape(item.SessionId), Escape(item.DeploymentId), Escape(item.ConditionId), Escape(item.RunType), Escape(item.ContentId), Escape(item.EventType), Escape(item.ClientRelativeMilliseconds), Escape(item.SchemaVersion), Escape(item.PayloadJson)));
        return csv.ToString();
    }

    private static string Escape(object? value)
    {
        var text = Convert.ToString(value) ?? string.Empty;
        return text.IndexOfAny([',','"','\r','\n']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }
}
