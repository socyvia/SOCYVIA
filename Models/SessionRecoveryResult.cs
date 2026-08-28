namespace SOCYVIA.Models;

public sealed class SessionRecoveryResult
{
    public string StudyId { get; init; } = string.Empty;
    public string ParticipantId { get; init; } = string.Empty;
    public int CancelledIncompleteSessionCount { get; init; }
    public bool HasRecoveredSessions =>
        CancelledIncompleteSessionCount > 0;
}
