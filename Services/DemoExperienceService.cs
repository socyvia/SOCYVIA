using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Platform;
using Microsoft.Data.Sqlite;
using SOCYVIA.Data;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class DemoExperienceService
{
    public const int CurrentDemoVersion = 3;
    private const string DemoMarker = "SOCYVIA.DEMO/3";

    public static async Task<DemoExperienceStatus> GetStatusAsync(string researcherId)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DemoStudyId, DemoVersion
            FROM DemoInstallations
            WHERE ResearcherId = $researcherId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$researcherId", researcherId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return new DemoExperienceStatus();
        return new DemoExperienceStatus
        {
            IsInstalled = true,
            StudyId = reader.GetString(0),
            DemoVersion = reader.GetInt32(1)
        };
    }

    public static async Task<Study> InstallAsync(string researcherId)
    {
        var status = await GetStatusAsync(researcherId);
        if (status is { IsInstalled: true, DemoVersion: CurrentDemoVersion, StudyId: not null })
        {
            var existing = await StudyRepository.GetByIdAsync(status.StudyId);
            if (existing is not null)
            {
                await DemoScientificDataService.EnsureAsync(existing.Id);
                return existing;
            }
        }

        if (status.IsInstalled)
            await RemoveAsync(researcherId);

        var ids = DemoIds.For(researcherId);
        var stagedAssets = await StageDemoAssetsAsync(researcherId);
        try
        {
            await InsertDemoAsync(researcherId, ids, stagedAssets);
        }
        catch
        {
            foreach (var asset in stagedAssets.Values)
                DeleteManagedFileSafely(asset);
            throw;
        }

        var study = (await StudyRepository.GetByIdAsync(ids.StudyId))
                    ?? throw new InvalidOperationException("The SOCYVIA demo study was not created.");
        await DemoScientificDataService.EnsureAsync(study.Id);
        return study;
    }

    public static async Task<Study> ResetAsync(string researcherId)
    {
        await RemoveAsync(researcherId);
        return await InstallAsync(researcherId);
    }

    public static async Task RemoveAsync(string researcherId)
    {
        var status = await GetStatusAsync(researcherId);
        if (!status.IsInstalled || status.StudyId is null) return;

        var mediaPaths = new List<string>();
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using (var paths = connection.CreateCommand())
        {
            paths.CommandText = """
                SELECT RelativePath FROM ManagedMediaAssets
                WHERE ResearcherId = $researcherId AND IsDemo = 1;
                """;
            paths.Parameters.AddWithValue("$researcherId", researcherId);
            await using var reader = await paths.ExecuteReaderAsync();
            while (await reader.ReadAsync()) mediaPaths.Add(reader.GetString(0));
        }

        await using var transaction = connection.BeginTransaction();
        try
        {
            await ExecuteAsync(connection, transaction,
                "DELETE FROM AnalysisExecutions WHERE StudyId = $studyId;",
                parameters => parameters.AddWithValue("$studyId", status.StudyId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM AnalysisSpecifications WHERE StudyId = $studyId;",
                parameters => parameters.AddWithValue("$studyId", status.StudyId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM QuestionnaireResponseSets WHERE StudyId = $studyId;",
                parameters => parameters.AddWithValue("$studyId", status.StudyId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM QuestionnaireAssignments WHERE StudyId = $studyId;",
                parameters => parameters.AddWithValue("$studyId", status.StudyId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM ResearchDataClassifications WHERE StudyId = $studyId;",
                parameters => parameters.AddWithValue("$studyId", status.StudyId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM Events WHERE StudyId = $studyId;",
                parameters => parameters.AddWithValue("$studyId", status.StudyId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM Responses WHERE SessionId IN " +
                "(SELECT Id FROM Sessions WHERE StudyId = $studyId);",
                parameters => parameters.AddWithValue("$studyId", status.StudyId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM ExperimentConfigurationSnapshots WHERE StudyId = $studyId;",
                parameters => parameters.AddWithValue("$studyId", status.StudyId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM Sessions WHERE StudyId = $studyId;",
                parameters => parameters.AddWithValue("$studyId", status.StudyId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM ParticipantConditionAssignments WHERE StudyId = $studyId;",
                parameters => parameters.AddWithValue("$studyId", status.StudyId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM ParticipantAssignments WHERE StudyId = $studyId;",
                parameters => parameters.AddWithValue("$studyId", status.StudyId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM ManagedMediaAssets WHERE ResearcherId = $researcherId AND IsDemo = 1;",
                parameters => parameters.AddWithValue("$researcherId", researcherId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM Studies WHERE Id = $studyId AND ResearcherId = $researcherId;",
                parameters =>
                {
                    parameters.AddWithValue("$studyId", status.StudyId);
                    parameters.AddWithValue("$researcherId", researcherId);
                });
            await ExecuteAsync(connection, transaction,
                "DELETE FROM ContentItems WHERE ResearcherId = $researcherId AND IsDemo = 1;",
                parameters => parameters.AddWithValue("$researcherId", researcherId));
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        foreach (var relativePath in mediaPaths)
        {
            try
            {
                var path = ManagedMediaService.ResolveAbsolutePath(researcherId, relativePath);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception exception)
            {
                ApplicationDiagnosticsService.LogException(exception, "Remove SOCYVIA demo media");
            }
        }
    }

    private static async Task<Dictionary<string, ManagedMediaAsset>> StageDemoAssetsAsync(
        string researcherId)
    {
        var resources = new Dictionary<string, string>
        {
            ["collective"] = "collective-signal.png",
            ["civic"] = "civic-rhythm.png",
            ["current"] = "information-current.png",
            ["horizon"] = "shared-horizon.png"
        };
        var assets = new Dictionary<string, ManagedMediaAsset>();
        foreach (var (key, fileName) in resources)
        {
            await using var stream = AssetLoader.Open(
                new Uri($"avares://SOCYVIA/Assets/Demo/{fileName}"));
            assets[key] = await ManagedMediaService.StageStreamAsync(
                researcherId, stream, fileName, "Image", true);
        }
        return assets;
    }

    private static async Task InsertDemoAsync(
        string researcherId,
        DemoIds ids,
        IReadOnlyDictionary<string, ManagedMediaAsset> media)
    {
        var now = DateTime.UtcNow;
        var sourceDates = new[]
        {
            new DateTime(2026, 8, 12, 9, 20, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 12, 11, 5, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 13, 8, 40, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 13, 14, 15, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 14, 10, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 14, 16, 10, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 15, 9, 55, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 15, 12, 45, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 15, 17, 25, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 16, 8, 15, 0, DateTimeKind.Utc)
        };
        var items = BuildContent(ids, media, sourceDates);
        var observations = BuildObservations(ids, sourceDates);
        var originalSettings = ConditionManipulationService.Serialize(new ConditionManipulationSettings
        {
            ShowEngagementMetrics = true,
            ContentOrderMode = ContentOrderMode.Original,
            ShowAuthor = true,
            ShowTimestamp = true,
            ShowPlatformIdentity = true
        });
        var hiddenSettings = ConditionManipulationService.Serialize(new ConditionManipulationSettings
        {
            ShowEngagementMetrics = false,
            LikesMode = MetricManipulationMode.Hidden,
            CommentsMode = MetricManipulationMode.Hidden,
            SharesMode = MetricManipulationMode.Hidden,
            SavesMode = MetricManipulationMode.Hidden,
            ViewsMode = MetricManipulationMode.Hidden,
            ContentOrderMode = ContentOrderMode.Original,
            ShowAuthor = true,
            ShowTimestamp = true,
            ShowPlatformIdentity = true
        });
        var modifiedSettings = ConditionManipulationService.Serialize(new ConditionManipulationSettings
        {
            ShowEngagementMetrics = true,
            LikesMode = MetricManipulationMode.Multiplier,
            LikesMultiplier = 1.75,
            CommentsMode = MetricManipulationMode.Multiplier,
            CommentsMultiplier = 1.75,
            SharesMode = MetricManipulationMode.Multiplier,
            SharesMultiplier = 1.75,
            SavesMode = MetricManipulationMode.Multiplier,
            SavesMultiplier = 1.75,
            ViewsMode = MetricManipulationMode.Multiplier,
            ViewsMultiplier = 1.75,
            ContentOrderMode = ContentOrderMode.Original,
            ShowAuthor = true,
            ShowTimestamp = true,
            ShowPlatformIdentity = true
        });

        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await InsertStudyAsync(connection, transaction, researcherId, ids, now);
            await InsertGroupAsync(connection, transaction, ids.GroupAId, ids.StudyId,
                "Observed engagement", "Control feed with captured engagement visible.", "#2563EB", true, 0, now);
            await InsertGroupAsync(connection, transaction, ids.GroupBId, ids.StudyId,
                "Hidden engagement", "Experimental feed with engagement signals removed.", "#64748B", false, 1, now);
            await InsertGroupAsync(connection, transaction, ids.GroupCId, ids.StudyId,
                "Amplified engagement", "Experimental feed with engagement values multiplied for presentation only.", "#7C5CC4", false, 2, now);
            await InsertConditionAsync(connection, transaction, ids.ConditionAId, ids.StudyId,
                ids.GroupAId, "Original engagement visible", "NeutralControl", true, 0, originalSettings, now);
            await InsertConditionAsync(connection, transaction, ids.ConditionBId, ids.StudyId,
                ids.GroupBId, "Engagement metrics hidden", "Custom", false, 1, hiddenSettings, now);
            await InsertConditionAsync(connection, transaction, ids.ConditionCId, ids.StudyId,
                ids.GroupCId, "Engagement metrics amplified", "Custom", false, 2, modifiedSettings, now);

            foreach (var item in items)
                await InsertContentAsync(connection, transaction, researcherId, item, now);
            foreach (var observation in observations)
                await InsertObservationAsync(connection, transaction, observation);
            foreach (var asset in media.Values)
            {
                var contentId = asset.OriginalFileName switch
                {
                    "collective-signal.png" => ids.ContentIds[0],
                    "civic-rhythm.png" => ids.ContentIds[2],
                    "information-current.png" => ids.ContentIds[4],
                    _ => ids.ContentIds[8]
                };
                await InsertMediaAsync(connection, transaction, asset, contentId);
            }

            await InsertFeedAsync(connection, transaction, ids.FeedAId, ids.StudyId,
                ids.GroupAId, ids.ConditionAId, "Observed engagement feed", 0, now);
            await InsertFeedAsync(connection, transaction, ids.FeedBId, ids.StudyId,
                ids.GroupBId, ids.ConditionBId, "Hidden engagement feed", 1, now);
            await InsertFeedAsync(connection, transaction, ids.FeedCId, ids.StudyId,
                ids.GroupCId, ids.ConditionCId, "Amplified engagement feed", 2, now);
            for (var index = 0; index < ids.ContentIds.Length; index++)
            {
                await InsertFeedItemAsync(connection, transaction, ids.FeedItemAIds[index],
                    ids.FeedAId, ids.ContentIds[index], ids.ObservationIds[index], index, now);
                await InsertFeedItemAsync(connection, transaction, ids.FeedItemBIds[index],
                    ids.FeedBId, ids.ContentIds[index], ids.ObservationIds[index], index, now);
                await InsertFeedItemAsync(connection, transaction, ids.FeedItemCIds[index],
                    ids.FeedCId, ids.ContentIds[index], ids.ObservationIds[index], index, now);
            }

            await InsertParticipantAsync(connection, transaction, ids.ParticipantAId,
                ids.StudyId, ids.GroupAId, "DEMO-A01", now);
            await InsertParticipantAsync(connection, transaction, ids.ParticipantBId,
                ids.StudyId, ids.GroupBId, "DEMO-B01", now);
            await InsertParticipantAsync(connection, transaction, ids.ParticipantCId,
                ids.StudyId, ids.GroupCId, "DEMO-C01", now);
            await InsertAssignmentsAsync(connection, transaction, ids, now);
            await InsertClassificationAsync(connection, transaction, "Study", ids.StudyId,
                ids.StudyId, true, false, now);
            foreach (var participantId in new[]
                     {
                         ids.ParticipantAId, ids.ParticipantBId, ids.ParticipantCId
                     })
                await InsertClassificationAsync(connection, transaction, "Participant",
                    participantId, ids.StudyId, true, true, now);

            var manifest = JsonSerializer.Serialize(ids.ContentIds);
            await ExecuteAsync(connection, transaction, """
                INSERT INTO DemoInstallations
                (ResearcherId, DemoStudyId, DemoVersion, ContentManifestJson,
                 InstalledAtUtc, UpdatedAtUtc)
                VALUES ($researcherId, $studyId, $version, $manifest, $now, $now);
                """, parameters =>
            {
                parameters.AddWithValue("$researcherId", researcherId);
                parameters.AddWithValue("$studyId", ids.StudyId);
                parameters.AddWithValue("$version", CurrentDemoVersion);
                parameters.AddWithValue("$manifest", manifest);
                parameters.AddWithValue("$now", now.ToString("O"));
            });
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static DemoContent[] BuildContent(
        DemoIds ids,
        IReadOnlyDictionary<string, ManagedMediaAsset> media,
        DateTime[] dates)
    {
        string PathFor(string key) => ManagedMediaService.ResolveAbsolutePath(media[key]);
        return
        [
            new(ids.ContentIds[0], "Collective signal", "When visible reactions cluster around one message, do readers infer consensus before evaluating the claim itself?", "Image", "Civic Lens", "Mira Studio", PathFor("collective"), PathFor("collective"), "Social influence", "Collective attention", dates[0]),
            new(ids.ContentIds[1], "The first number we notice", "A small design change can move a count from the edge of a post to the center of attention. That shift may change what people read next, even when the underlying content remains identical.", "Text", "Field Notes", "Research Desk", null, null, "Interface cues", "Selective attention", dates[1]),
            new(ids.ContentIds[2], "Civic rhythm", "A public space can reveal collective patterns without revealing individual identities. Digital environments need the same restraint.", "Image", "Urban Commons", "North Quarter Lab", PathFor("civic"), PathFor("civic"), "Public participation", "Collective behavior", dates[2]),
            new(ids.ContentIds[3], "Designing a neutral public feed", "A short methods note on separating familiar interaction patterns from platform-specific branding.", "Link", "Methods Exchange", "Methods Collective", null, PathFor("civic"), "Research methods", "Platform confounds", dates[3], "https://demo.socyvia.invalid/methods/neutral-feed"),
            new(ids.ContentIds[4], "Information current", "Signals travel through a network at different speeds. Visibility, repetition, and order determine which traces become salient.", "Mixed", "Signal Review", "Aster Archive", PathFor("current"), PathFor("current"), "Information diffusion", "Network attention", dates[4]),
            new(ids.ContentIds[5], "Thirty seconds of an information cascade", "A fictional local video research copy illustrating how one item can accumulate attention across a feed.", "Video", "Process Journal", "SOCYVIA Demo Studio", null, PathFor("current"), "Information diffusion", "Temporal exposure", dates[5]),
            new(ids.ContentIds[6], "What disappears when counts disappear?", "Participants may attend more closely to the source and wording when popularity cues are unavailable. The demo experiment compares those two presentations.", "Text", "Behavior Brief", "Nadia Hale", null, null, "Engagement visibility", "Social proof", dates[6]),
            new(ids.ContentIds[7], "Field recording: interface ambience", "A fictional audio stimulus slot showing how managed research media can be represented without relying on an external URL.", "Audio", "Field Archive", "SOCYVIA Demo Studio", null, PathFor("horizon"), "Digital environments", "Audio context", dates[7]),
            new(ids.ContentIds[8], "Shared horizon", "When people see the same issue through different social signals, their expectations about the wider public may diverge.", "Image", "Future Commons", "Leila North", PathFor("horizon"), PathFor("horizon"), "Public opinion", "Pluralistic perception", dates[8]),
            new(ids.ContentIds[9], "Source truth and experimental presentation", "The captured observation remains evidence. The condition changes only what the participant sees; behavior is recorded as a third, separate layer.", "Link", "SOCYVIA Methods", "Demo Research Team", null, PathFor("collective"), "Reproducibility", "Experimental provenance", dates[9], "https://demo.socyvia.invalid/methods/source-truth")
        ];
    }

    private static EngagementObservation[] BuildObservations(DemoIds ids, DateTime[] dates)
    {
        var values = new (long Likes, long Comments, long Shares, long Saves, long Views)[]
        {
            (14231, 684, 1902, 3110, 284500), (843, 96, 170, 422, 19300),
            (6201, 318, 744, 1280, 109800), (1290, 117, 306, 511, 42100),
            (21540, 903, 2711, 4830, 410200), (4780, 241, 633, 940, 87400),
            (381, 54, 72, 190, 9200), (116, 18, 21, 44, 3100),
            (9870, 455, 1012, 1760, 188900), (704, 82, 151, 302, 16400)
        };
        return ids.ContentIds.Select((contentId, index) => new EngagementObservation
        {
            Id = ids.ObservationIds[index], ContentItemId = contentId,
            Likes = values[index].Likes, Comments = values[index].Comments,
            Shares = values[index].Shares, Saves = values[index].Saves,
            Views = values[index].Views, CapturedAtUtc = dates[index].AddHours(3),
            ObservationSource = DemoMarker,
            SourceMetadataJson = JsonSerializer.Serialize(new { Demo = true, Captured = "Observed value at capture time" })
        }).ToArray();
    }

    private static async Task InsertStudyAsync(SqliteConnection connection, SqliteTransaction transaction,
        string researcherId, DemoIds ids, DateTime now)
    {
        await ExecuteAsync(connection, transaction, """
            INSERT INTO Studies
            (Id, ResearcherId, Title, Description, Status, StudyType, DesignType,
             AssignmentMethod, RandomizeStimuli, RandomizationSeed, UsesStimuli,
             UsesQuestionnaires, UsesPhysiologicalData, EegEnabled, GsrEnabled,
             TargetSampleSize, ExpectedSessionDurationMinutes, AllowSessionResume,
             RequireParticipantConsent, ConsentText, ResearchQuestion, Hypothesis,
             PopulationDescription, InclusionCriteria, ExclusionCriteria, MetadataJson,
             CreatedAtUtc, UpdatedAtUtc, StartedAtUtc, CompletedAtUtc, IsArchived)
            VALUES
            ($id, $researcherId, $title, $description, 'Ready', 'Experimental',
             'BetweenSubjects', 'BalancedRandom', 0, 20260816, 1, 0, 0, 0, 0,
             60, 8, 1, 1, $consent, $question, $hypothesis, $population,
             $inclusion, NULL, $metadata, $now, $now, NULL, NULL, 0);
            """, parameters =>
        {
            parameters.AddWithValue("$id", ids.StudyId);
            parameters.AddWithValue("$researcherId", researcherId);
            parameters.AddWithValue("$title", "Digital Engagement Visibility Study — DEMO");
            parameters.AddWithValue("$description", "A removable, synthetic SOCYVIA demonstration comparing identical content with observed engagement visible, hidden, or amplified.");
            parameters.AddWithValue("$consent", "This is fictional demonstration data. No real participant data is collected in preview mode.");
            parameters.AddWithValue("$question", "How does the visibility of engagement metrics influence attention to otherwise identical digital content?");
            parameters.AddWithValue("$hypothesis", "Removing engagement metrics will change exposure and interaction patterns without changing source content.");
            parameters.AddWithValue("$population", "Fictional adult digital-media users used only for product demonstration.");
            parameters.AddWithValue("$inclusion", "Demo participants are eligible and consent-documented fictional records.");
            parameters.AddWithValue("$metadata", JsonSerializer.Serialize(new { IsDemo = true, IsSynthetic = true, ReadOnlyGuidedDemo = true, DemoVersion = CurrentDemoVersion, Marker = DemoMarker }));
            parameters.AddWithValue("$now", now.ToString("O"));
        });
    }

    private static Task InsertGroupAsync(SqliteConnection c, SqliteTransaction t, string id,
        string studyId, string name, string description, string color, bool control, int order, DateTime now) =>
        ExecuteAsync(c, t, """
            INSERT INTO Groups
            (Id, StudyId, Name, Description, ColorHex, IsControlGroup, SortOrder,
             TargetSampleSize, IsActive, CreatedAtUtc, UpdatedAtUtc)
            VALUES ($id, $studyId, $name, $description, $color, $control, $order,
                    20, 1, $now, $now);
            """, p =>
        {
            p.AddWithValue("$id", id); p.AddWithValue("$studyId", studyId);
            p.AddWithValue("$name", name); p.AddWithValue("$description", description);
            p.AddWithValue("$color", color); p.AddWithValue("$control", control ? 1 : 0);
            p.AddWithValue("$order", order); p.AddWithValue("$now", now.ToString("O"));
        });

    private static Task InsertConditionAsync(SqliteConnection c, SqliteTransaction t, string id,
        string studyId, string groupId, string name, string type, bool control, int order,
        string settings, DateTime now) => ExecuteAsync(c, t, """
            INSERT INTO ExperimentalConditions
            (Id, StudyId, GroupId, Name, Description, ConditionType, SortOrder,
             IsControlCondition, IsActive, ManipulationJson, CreatedAtUtc, UpdatedAtUtc)
            VALUES ($id, $studyId, $groupId, $name, $description, $type, $order,
                    $control, 1, $settings, $now, $now);
            """, p =>
        {
            p.AddWithValue("$id", id); p.AddWithValue("$studyId", studyId);
            p.AddWithValue("$groupId", groupId); p.AddWithValue("$name", name);
            p.AddWithValue("$description", control
                ? "Captured engagement observations are presented unchanged."
                : name.Contains("hidden", StringComparison.OrdinalIgnoreCase)
                    ? "All engagement metrics are hidden while content remains identical."
                    : "Engagement metrics are amplified for presentation only; source observations remain unchanged.");
            p.AddWithValue("$type", type); p.AddWithValue("$order", order);
            p.AddWithValue("$control", control ? 1 : 0); p.AddWithValue("$settings", settings);
            p.AddWithValue("$now", now.ToString("O"));
        });

    private static Task InsertContentAsync(SqliteConnection c, SqliteTransaction t,
        string researcherId, DemoContent item, DateTime now) => ExecuteAsync(c, t, """
            INSERT INTO ContentItems
            (Id, ResearcherId, LegacyStimulusId, Title, BodyText, ContentType,
             Platform, SourceName, AuthorName, OriginalUrl, PublishedAtUtc,
             CapturedAtUtc, MediaPath, ThumbnailPath, SourceMetadataJson,
             Category, Topic, Tags, ResearcherNotes, AcquisitionProvider,
             AcquisitionStatus, IsDemo, IsActive, CreatedAtUtc, UpdatedAtUtc)
            VALUES ($id, $researcherId, NULL, $title, $body, $type, $platform,
                    $sourceName, $author, $url, $published, $captured, $media,
                    $thumbnail, $sourceMetadata, $category, $topic, $tags,
                    $notes, $provider, 'Demo', 1, 1, $now, $now);
            """, p =>
        {
            p.AddWithValue("$id", item.Id); p.AddWithValue("$researcherId", researcherId);
            p.AddWithValue("$title", item.Title); p.AddWithValue("$body", item.Body);
            p.AddWithValue("$type", item.Type); p.AddWithValue("$platform", item.Platform);
            p.AddWithValue("$sourceName", item.Platform); p.AddWithValue("$author", item.Author);
            p.AddWithValue("$url", Db(item.Url)); p.AddWithValue("$published", item.Published.ToString("O"));
            p.AddWithValue("$captured", item.Published.AddHours(3).ToString("O"));
            p.AddWithValue("$media", Db(item.Media)); p.AddWithValue("$thumbnail", Db(item.Thumbnail));
            p.AddWithValue("$sourceMetadata", JsonSerializer.Serialize(new
            {
                IsDemo = true,
                IsSynthetic = true,
                Provenance = "Fictional SOCYVIA-owned demonstration content",
                SourceTruth = true,
                DemoComments = DemoCommentsFor(item.Title)
            }));
            p.AddWithValue("$category", item.Category); p.AddWithValue("$topic", item.Topic);
            p.AddWithValue("$tags", "demo, computational social science");
            p.AddWithValue("$notes", "DEMO data — removable without affecting real research records.");
            p.AddWithValue("$provider", DemoMarker); p.AddWithValue("$now", now.ToString("O"));
        });

    private static Task InsertObservationAsync(SqliteConnection c, SqliteTransaction t,
        EngagementObservation item) => ExecuteAsync(c, t, """
            INSERT INTO EngagementObservations
            (Id, ContentItemId, Likes, Comments, Shares, Saves, Views,
             CapturedAtUtc, ObservationSource, SourceMetadataJson)
            VALUES ($id, $content, $likes, $comments, $shares, $saves, $views,
                    $captured, $source, $metadata);
            """, p =>
        {
            p.AddWithValue("$id", item.Id); p.AddWithValue("$content", item.ContentItemId);
            p.AddWithValue("$likes", Db(item.Likes)); p.AddWithValue("$comments", Db(item.Comments));
            p.AddWithValue("$shares", Db(item.Shares)); p.AddWithValue("$saves", Db(item.Saves));
            p.AddWithValue("$views", Db(item.Views)); p.AddWithValue("$captured", item.CapturedAtUtc.ToString("O"));
            p.AddWithValue("$source", item.ObservationSource); p.AddWithValue("$metadata", Db(item.SourceMetadataJson));
        });

    private static Task InsertMediaAsync(SqliteConnection c, SqliteTransaction t,
        ManagedMediaAsset asset, string contentId) => ExecuteAsync(c, t, """
            INSERT INTO ManagedMediaAssets
            (Id, ResearcherId, ContentItemId, MediaKind, OriginalFileName,
             RelativePath, MimeType, ByteLength, Sha256, MetadataJson, IsDemo, CreatedAtUtc)
            VALUES ($id, $researcher, $content, $kind, $file, $path, $mime,
                    $bytes, $hash, $metadata, 1, $created);
            """, p =>
        {
            p.AddWithValue("$id", asset.Id); p.AddWithValue("$researcher", asset.ResearcherId);
            p.AddWithValue("$content", contentId); p.AddWithValue("$kind", asset.MediaKind);
            p.AddWithValue("$file", asset.OriginalFileName); p.AddWithValue("$path", asset.RelativePath);
            p.AddWithValue("$mime", Db(asset.MimeType)); p.AddWithValue("$bytes", asset.ByteLength);
            p.AddWithValue("$hash", asset.Sha256); p.AddWithValue("$metadata", Db(asset.MetadataJson));
            p.AddWithValue("$created", asset.CreatedAtUtc.ToString("O"));
        });

    private static Task InsertFeedAsync(SqliteConnection c, SqliteTransaction t, string id,
        string studyId, string groupId, string conditionId, string name, int order, DateTime now) =>
        ExecuteAsync(c, t, """
            INSERT INTO ExperimentalFeeds
            (Id, StudyId, GroupId, ConditionId, Name, SortOrder, IsActive,
             PresentationJson, CreatedAtUtc, UpdatedAtUtc)
            VALUES ($id, $study, $group, $condition, $name, $order, 1,
                    $presentation, $now, $now);
            """, p =>
        {
            p.AddWithValue("$id", id); p.AddWithValue("$study", studyId);
            p.AddWithValue("$group", groupId); p.AddWithValue("$condition", conditionId);
            p.AddWithValue("$name", name); p.AddWithValue("$order", order);
            p.AddWithValue("$presentation", JsonSerializer.Serialize(new { Mode = "Feed", IsDemo = true }));
            p.AddWithValue("$now", now.ToString("O"));
        });

    private static Task InsertFeedItemAsync(SqliteConnection c, SqliteTransaction t,
        string id, string feedId, string contentId, string observationId, int order, DateTime now) =>
        ExecuteAsync(c, t, """
            INSERT INTO ExperimentalFeedItems
            (Id, FeedId, ContentItemId, LegacyStimulusId, EngagementObservationId,
             SortOrder, IsActive, ItemManipulationJson, PresentationJson,
             CreatedAtUtc, UpdatedAtUtc)
            VALUES ($id, $feed, $content, NULL, $observation, $order, 1,
                    NULL, NULL, $now, $now);
            """, p =>
        {
            p.AddWithValue("$id", id); p.AddWithValue("$feed", feedId);
            p.AddWithValue("$content", contentId); p.AddWithValue("$observation", observationId);
            p.AddWithValue("$order", order); p.AddWithValue("$now", now.ToString("O"));
        });

    private static Task InsertParticipantAsync(SqliteConnection c, SqliteTransaction t,
        string id, string studyId, string groupId, string code, DateTime now) =>
        ExecuteAsync(c, t, """
            INSERT INTO Participants
            (Id, StudyId, GroupId, ParticipantCode, Status, Age, Gender,
             EducationLevel, Occupation, IsEligible, EligibilityNotes,
             ConsentAccepted, ConsentAcceptedAtUtc, HasStartedStudy,
             HasCompletedStudy, StudyStartedAtUtc, StudyCompletedAtUtc,
             IsExcluded, ExclusionReason, HasWithdrawn, WithdrawalReason,
             ResearcherNotes, MetadataJson, CreatedAtUtc, UpdatedAtUtc)
            VALUES ($id, $study, $group, $code, 'Active', NULL, NULL, NULL, NULL,
                    1, NULL, 1, $now, 0, 0, NULL, NULL, 0, NULL, 0, NULL,
                    'Fictional DEMO participant — SYNTHETIC DATA', $metadata, $now, $now);
            """, p =>
        {
            p.AddWithValue("$id", id); p.AddWithValue("$study", studyId);
            p.AddWithValue("$group", groupId); p.AddWithValue("$code", code);
            p.AddWithValue("$metadata", JsonSerializer.Serialize(new { IsDemo = true, IsSynthetic = true }));
            p.AddWithValue("$now", now.ToString("O"));
        });

    private static async Task InsertAssignmentsAsync(SqliteConnection c, SqliteTransaction t,
        DemoIds ids, DateTime now)
    {
        foreach (var tuple in new[]
                 {
                     (ids.ParticipantAId, ids.GroupAId, ids.ConditionAId, 1),
                     (ids.ParticipantBId, ids.GroupBId, ids.ConditionBId, 2),
                     (ids.ParticipantCId, ids.GroupCId, ids.ConditionCId, 3)
                 })
        {
            var participantAssignmentId = StableId(ids.StudyId, "participant-group", tuple.Item1);
            var conditionAssignmentId = StableId(ids.StudyId, "participant-condition", tuple.Item1);
            await ExecuteAsync(c, t, """
                INSERT INTO ParticipantAssignments
                (Id, StudyId, ParticipantId, GroupId, AssignmentMethod,
                 RandomizationSeed, AssignmentOrder, IsActive, AssignedAtUtc, Notes)
                VALUES ($id, $study, $participant, $group, 'Demo', 20260816,
                        $order, 1, $now, 'Fictional DEMO assignment');
                """, p =>
            {
                p.AddWithValue("$id", participantAssignmentId); p.AddWithValue("$study", ids.StudyId);
                p.AddWithValue("$participant", tuple.Item1); p.AddWithValue("$group", tuple.Item2);
                p.AddWithValue("$order", tuple.Item4); p.AddWithValue("$now", now.ToString("O"));
            });
            await ExecuteAsync(c, t, """
                INSERT INTO ParticipantConditionAssignments
                (Id, StudyId, ParticipantId, ConditionId, AssignmentMethod,
                 RandomizationSeed, AssignmentMetadataJson, AssignedAtUtc, IsActive)
                VALUES ($id, $study, $participant, $condition, 'Manual', 20260816,
                        $metadata, $now, 1);
                """, p =>
            {
                p.AddWithValue("$id", conditionAssignmentId); p.AddWithValue("$study", ids.StudyId);
                p.AddWithValue("$participant", tuple.Item1); p.AddWithValue("$condition", tuple.Item3);
                p.AddWithValue("$metadata", JsonSerializer.Serialize(new { IsDemo = true }));
                p.AddWithValue("$now", now.ToString("O"));
            });
        }
    }

    private static Task InsertClassificationAsync(
        SqliteConnection c,
        SqliteTransaction t,
        string entityType,
        string entityId,
        string studyId,
        bool isDemo,
        bool isSynthetic,
        DateTime now) => ExecuteAsync(c, t, """
            INSERT INTO ResearchDataClassifications
            (EntityType, EntityId, StudyId, IsDemo, IsSynthetic,
             ClassificationSource, CreatedAtUtc)
            VALUES ($type, $id, $study, $demo, $synthetic, $source, $created);
            """, p =>
        {
            p.AddWithValue("$type", entityType);
            p.AddWithValue("$id", entityId);
            p.AddWithValue("$study", studyId);
            p.AddWithValue("$demo", isDemo ? 1 : 0);
            p.AddWithValue("$synthetic", isSynthetic ? 1 : 0);
            p.AddWithValue("$source", DemoMarker);
            p.AddWithValue("$created", now.ToString("O"));
        });

    private static object[] DemoCommentsFor(string title) =>
    [
        new { Author = "Demo Reader 01", Text = $"A synthetic reaction to {title.ToLowerInvariant()} for focused-view testing." },
        new { Author = "Demo Reader 02", Text = "This fictional comment is configured content, not a real participant response." }
    ];

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction,
        string sql, Action<SqliteParameterCollection> addParameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        addParameters(command.Parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static object Db(object? value) => value ?? DBNull.Value;

    private static string StableId(params string[] parts)
    {
        var hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join("|", parts)))).ToLowerInvariant();
        return "demo-" + hash[..28];
    }

    private static void DeleteManagedFileSafely(ManagedMediaAsset asset)
    {
        try
        {
            var path = ManagedMediaService.ResolveAbsolutePath(asset);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Clean incomplete SOCYVIA demo media");
        }
    }

    private sealed record DemoContent(string Id, string Title, string Body, string Type,
        string Platform, string Author, string? Media, string? Thumbnail,
        string Category, string Topic, DateTime Published, string? Url = null);

    private sealed class DemoIds
    {
        public required string StudyId { get; init; }
        public required string GroupAId { get; init; }
        public required string GroupBId { get; init; }
        public required string GroupCId { get; init; }
        public required string ConditionAId { get; init; }
        public required string ConditionBId { get; init; }
        public required string ConditionCId { get; init; }
        public required string FeedAId { get; init; }
        public required string FeedBId { get; init; }
        public required string FeedCId { get; init; }
        public required string ParticipantAId { get; init; }
        public required string ParticipantBId { get; init; }
        public required string ParticipantCId { get; init; }
        public required string[] ContentIds { get; init; }
        public required string[] ObservationIds { get; init; }
        public required string[] FeedItemAIds { get; init; }
        public required string[] FeedItemBIds { get; init; }
        public required string[] FeedItemCIds { get; init; }

        public static DemoIds For(string researcherId)
        {
            var root = StableId(researcherId, DemoMarker);
            var content = Enumerable.Range(1, 10)
                .Select(index => $"DEMO-P{index:000}-{root[^8..]}").ToArray();
            return new DemoIds
            {
                StudyId = StableId(root, "study"), GroupAId = StableId(root, "group-a"),
                GroupBId = StableId(root, "group-b"), ConditionAId = StableId(root, "condition-a"),
                GroupCId = StableId(root, "group-c"),
                ConditionBId = StableId(root, "condition-b"), ConditionCId = StableId(root, "condition-c"),
                FeedAId = StableId(root, "feed-a"), FeedBId = StableId(root, "feed-b"),
                FeedCId = StableId(root, "feed-c"), ParticipantAId = StableId(root, "participant-a"),
                ParticipantBId = StableId(root, "participant-b"), ParticipantCId = StableId(root, "participant-c"), ContentIds = content,
                ObservationIds = content.Select(id => StableId(root, "observation", id)).ToArray(),
                FeedItemAIds = content.Select(id => StableId(root, "feed-a", id)).ToArray(),
                FeedItemBIds = content.Select(id => StableId(root, "feed-b", id)).ToArray(),
                FeedItemCIds = content.Select(id => StableId(root, "feed-c", id)).ToArray()
            };
        }
    }
}
