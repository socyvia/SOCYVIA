using System;
using System.Linq;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class SessionSummaryService
{
    private static readonly string[] InteractionTypes =
    {
        "Click",
        "Reaction",
        "Like",
        "Unlike",
        "ReadMore",
        "CommentIntent"
    };

    public static async Task<ParticipantSessionSummary> CreateAsync(
        string sessionId)
    {
        var session = await ExperimentSessionRepository.GetByIdAsync(sessionId)
            ?? throw new InvalidOperationException("The session does not exist.");
        var snapshot = await ExperimentConfigurationSnapshotRepository
            .GetBySessionAsync(sessionId)
            ?? throw new InvalidOperationException(
                "The session configuration snapshot does not exist.");
        var events = await InteractionEventRepository
            .GetBySessionAsync(sessionId);

        return new ParticipantSessionSummary
        {
            SessionId = session.Id,
            ParticipantCode = string.IsNullOrWhiteSpace(snapshot.ParticipantCode)
                ? snapshot.ParticipantId
                : snapshot.ParticipantCode,
            GroupName = snapshot.GroupName,
            ConditionName = snapshot.ConditionName,
            Status = session.Status,
            DurationMilliseconds = session.DurationMilliseconds ?? 0,
            StimuliExposed = events
                .Where(item => item.EventType == "PostShown")
                .Select(item =>
                    item.SnapshotStimulusId ?? item.StimulusPostId)
                .Where(item => item is not null)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            InteractionCount = events.Count(item =>
                InteractionTypes.Contains(
                    item.EventType,
                    StringComparer.Ordinal)),
            TotalExposureMilliseconds = events
                .Where(item => item.EventType == "TimeSpentPerPost")
                .Sum(item => item.DurationMilliseconds ?? 0)
        };
    }
}
