using System;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class SessionRecoveryService
{
    public static async Task<SessionRecoveryResult>
        RecoverIncompleteCreatedSessionsAsync(
            string studyId,
            string participantId)
    {
        if (string.IsNullOrWhiteSpace(studyId))
        {
            throw new ArgumentException("Study ID is required.", nameof(studyId));
        }
        if (string.IsNullOrWhiteSpace(participantId))
        {
            throw new ArgumentException(
                "Participant ID is required.",
                nameof(participantId));
        }

        var count = await ExperimentPreparationRepository
            .RecoverIncompleteCreatedAsync(studyId, participantId);
        return new SessionRecoveryResult
        {
            StudyId = studyId,
            ParticipantId = participantId,
            CancelledIncompleteSessionCount = count
        };
    }
}
