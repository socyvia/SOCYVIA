namespace SOCYVIA.Models;

public class GroupDeletionResult
{
    public bool WasDeleted { get; init; }
    public bool RequiresDeactivation { get; init; }
    public GroupUsageSummary Usage { get; init; } = new();
}
