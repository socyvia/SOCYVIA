using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

/// <summary>Real-data dashboard boundary. Consumers select a condition explicitly; records are never pooled by accident.</summary>
public sealed class RemoteResearchDashboardService
{
    public Task<RemoteDashboardMetrics> GetMetricsAsync(string? conditionId = null) => RemoteResearchRepository.GetMetricsAsync(conditionId);

    public async Task<IReadOnlyList<RemoteConditionDashboardMetrics>> GetComparisonAsync(IEnumerable<ExperimentalCondition> conditions)
    {
        var result = new List<RemoteConditionDashboardMetrics>();
        foreach (var condition in conditions.OrderBy(item => item.SortOrder))
            result.Add(new RemoteConditionDashboardMetrics(condition.Id, condition.Name, await RemoteResearchRepository.GetMetricsAsync(condition.Id)));
        return result;
    }

    public async Task<RemoteBehavioralSummary> GetBehavioralSummaryAsync(string? conditionId = null, bool completedOnly = true)
    {
        var events = await RemoteResearchRepository.GetEventsAsync(conditionId, completedOnly);
        return new RemoteBehavioralSummary(
            events.Count(item => item.EventType == "content_impression"),
            events.Count(item => item.EventType == "content_open"),
            events.Count(item => item.EventType == "read_more_open"),
            events.Count(item => item.EventType == "like"),
            events.Count(item => item.EventType == "comment_submit"),
            events.Count(item => item.EventType == "save"),
            events.Count(item => item.EventType == "share"));
    }

    public async Task<IReadOnlyList<RemoteQuestionnaireResponseContract>> GetQuestionnaireResultsAsync(QuestionnaireStage stage, string? conditionId = null) =>
        await RemoteResearchRepository.GetQuestionnaireResponsesAsync(stage, conditionId, true);
}

public sealed record RemoteConditionDashboardMetrics(string ConditionId, string ConditionLabel, RemoteDashboardMetrics Metrics);
public sealed record RemoteBehavioralSummary(int QualifiedImpressions, int ContentOpens, int ReadMoreOpens, int Likes, int Comments, int Saves, int Shares);
