using System;
using System.Threading.Tasks;
using SOCYVIA.Data;

namespace SOCYVIA.Services;

public static class LegacyContentCompatibilityService
{
    public static Task SynchronizeResearcherAsync(string researcherId) =>
        SynchronizeAsync("st.ResearcherId = $scopeId", researcherId);

    public static Task SynchronizeStudyAsync(string studyId) =>
        SynchronizeAsync("s.StudyId = $scopeId", studyId);

    private static async Task SynchronizeAsync(
        string scopePredicate,
        string scopeId)
    {
        if (string.IsNullOrWhiteSpace(scopeId))
        {
            return;
        }

        await using var connection =
            await DatabaseService.OpenConnectionAsync();
        await using var transaction =
            await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
        command.CommandText = $"""
            INSERT OR IGNORE INTO ContentItems
            (
                Id, ResearcherId, LegacyStimulusId, Title, BodyText,
                ContentType, Platform, SourceName, AuthorName, OriginalUrl,
                PublishedAtUtc, CapturedAtUtc, MediaPath, ThumbnailPath, PublishedMediaUrl,
                SourceMetadataJson, Category, Topic, Tags, ResearcherNotes,
                AcquisitionProvider, AcquisitionStatus, IsActive,
                CreatedAtUtc, UpdatedAtUtc
            )
            SELECT
                s.Id, st.ResearcherId, s.Id, s.Name,
                COALESCE(s.ContentText, ''), s.StimulusType,
                COALESCE(s.Platform, 'Generic'), s.SourceName,
                s.AuthorName, s.OriginalUrl, s.PublishedAtUtc,
                COALESCE(s.CreatedAtUtc, CURRENT_TIMESTAMP),
                s.SourcePath, s.ThumbnailPath, s.PublishedMediaUrl, s.MetadataJson,
                s.Category, s.Topic, s.ExperimentalTag,
                s.ResearcherNotes, 'LegacyStimulus', 'Migrated',
                s.IsActive, s.CreatedAtUtc,
                COALESCE(s.UpdatedAtUtc, s.CreatedAtUtc)
            FROM Stimuli s
            JOIN Studies st ON st.Id = s.StudyId
            WHERE {scopePredicate};

            INSERT OR IGNORE INTO EngagementObservations
            (
                Id, ContentItemId, Likes, Comments, Shares, Saves, Views,
                CapturedAtUtc, ObservationSource, SourceMetadataJson
            )
            SELECT
                'legacy-observation-' || s.Id, s.Id,
                s.OriginalLikes, s.OriginalComments, s.OriginalShares,
                s.OriginalSaves, s.OriginalViews,
                COALESCE(s.CreatedAtUtc, CURRENT_TIMESTAMP),
                'LegacyStimulus', s.MetadataJson
            FROM Stimuli s
            JOIN Studies st ON st.Id = s.StudyId
            JOIN ContentItems c ON c.Id = s.Id
            WHERE {scopePredicate};

            INSERT OR IGNORE INTO ExperimentalFeeds
            (
                Id, StudyId, GroupId, ConditionId, Name, SortOrder,
                IsActive, PresentationJson, CreatedAtUtc, UpdatedAtUtc
            )
            SELECT
                'legacy-feed-' || s.StudyId || '-' || COALESCE(s.GroupId, 'all'),
                s.StudyId, s.GroupId, NULL,
                CASE WHEN s.GroupId IS NULL THEN 'All participants'
                    ELSE COALESCE(g.Name, 'Study group') END,
                0, 1, NULL, MIN(s.CreatedAtUtc),
                MAX(COALESCE(s.UpdatedAtUtc, s.CreatedAtUtc))
            FROM Stimuli s
            JOIN Studies st ON st.Id = s.StudyId
            LEFT JOIN Groups g ON g.Id = s.GroupId
            WHERE {scopePredicate}
            GROUP BY s.StudyId, s.GroupId;

            INSERT OR IGNORE INTO ExperimentalFeedItems
            (
                Id, FeedId, ContentItemId, LegacyStimulusId,
                SortOrder, IsActive, ItemManipulationJson,
                PresentationJson, CreatedAtUtc, UpdatedAtUtc
            )
            SELECT
                'legacy-feed-item-' || s.Id,
                'legacy-feed-' || s.StudyId || '-' || COALESCE(s.GroupId, 'all'),
                s.Id, s.Id, s.SortOrder, s.IsActive, NULL, NULL,
                s.CreatedAtUtc, COALESCE(s.UpdatedAtUtc, s.CreatedAtUtc)
            FROM Stimuli s
            JOIN Studies st ON st.Id = s.StudyId
            JOIN ContentItems c ON c.Id = s.Id
            WHERE {scopePredicate};
            """;
        command.Parameters.AddWithValue("$scopeId", scopeId);

        try
        {
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
