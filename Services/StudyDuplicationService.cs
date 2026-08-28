using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

/// <summary>Copies study design only. Collected data and deployment identity are never queried or copied.</summary>
public static class StudyDuplicationService
{
    public static async Task<Study> DuplicateAsync(Study source, bool arabic)
    {
        ArgumentNullException.ThrowIfNull(source);
        var now = DateTime.UtcNow;
        var copy = new Study
        {
            Id = Guid.NewGuid().ToString(),
            ResearcherId = source.ResearcherId,
            Title = arabic ? $"نسخة من {source.Title}" : $"Copy of {source.Title}",
            Description = source.Description,
            Status = "Draft",
            StudyType = source.StudyType,
            DesignType = source.DesignType,
            AssignmentMethod = source.AssignmentMethod,
            RandomizeStimuli = source.RandomizeStimuli,
            RandomizationSeed = source.RandomizationSeed,
            UsesStimuli = source.UsesStimuli,
            UsesQuestionnaires = source.UsesQuestionnaires,
            UsesPhysiologicalData = false,
            EegEnabled = false,
            GsrEnabled = false,
            TargetSampleSize = source.TargetSampleSize,
            ExpectedSessionDurationMinutes = source.ExpectedSessionDurationMinutes,
            AllowSessionResume = source.AllowSessionResume,
            RequireParticipantConsent = source.RequireParticipantConsent,
            ConsentText = source.ConsentText,
            ResearchQuestion = source.ResearchQuestion,
            Hypothesis = source.Hypothesis,
            PopulationDescription = source.PopulationDescription,
            InclusionCriteria = source.InclusionCriteria,
            ExclusionCriteria = source.ExclusionCriteria,
            MetadataJson = DesignMetadataOnly(source.MetadataJson),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            StartedAtUtc = null,
            CompletedAtUtc = null,
            IsArchived = false
        };

        await StudyRepository.CreateAsync(copy);
        try
        {
            var groupMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in await GroupRepository.GetByStudyAsync(source.Id))
            {
                var duplicate = new StudyGroup
                {
                    Id = Guid.NewGuid().ToString(), StudyId = copy.Id, Name = group.Name,
                    Description = group.Description, ColorHex = group.ColorHex,
                    IsControlGroup = group.IsControlGroup, SortOrder = group.SortOrder,
                    TargetSampleSize = group.TargetSampleSize, IsActive = group.IsActive,
                    CreatedAtUtc = now, UpdatedAtUtc = now
                };
                await GroupRepository.CreateAsync(duplicate);
                groupMap[group.Id] = duplicate.Id;
            }

            foreach (var condition in await ExperimentalConditionRepository.GetByStudyAsync(source.Id))
                await ExperimentalConditionRepository.CreateAsync(new ExperimentalCondition
                {
                    Id = Guid.NewGuid().ToString(), StudyId = copy.Id,
                    GroupId = condition.GroupId is not null && groupMap.TryGetValue(condition.GroupId, out var groupId) ? groupId : null,
                    Name = condition.Name, Description = condition.Description,
                    ConditionType = condition.ConditionType, SortOrder = condition.SortOrder,
                    IsControlCondition = condition.IsControlCondition, IsActive = condition.IsActive,
                    ManipulationJson = condition.ManipulationJson, CreatedAtUtc = now, UpdatedAtUtc = now
                });

            foreach (var stimulus in await StimulusPostRepository.GetByStudyAsync(source.Id))
                await StimulusPostRepository.CreateAsync(CloneStimulus(stimulus, copy.Id, groupMap, now));

            var versionMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var questionnaire in await QuestionnaireRepository.GetByStudyAsync(source.Id))
            {
                var duplicate = await QuestionnaireRepository.DuplicateToStudyAsync(questionnaire.Id, copy.Id);
                if (duplicate.CurrentVersionId is null) continue;
                foreach (var version in questionnaire.Versions) versionMap[version.Id] = duplicate.CurrentVersionId;
            }

            foreach (var assignment in await QuestionnaireRepository.GetAssignmentsAsync(source.Id))
            {
                if (!versionMap.TryGetValue(assignment.QuestionnaireVersionId, out var versionId)) continue;
                await QuestionnaireRepository.AssignAsync(new QuestionnaireAssignment
                {
                    Id = Guid.NewGuid().ToString(), StudyId = copy.Id, QuestionnaireVersionId = versionId,
                    Placement = assignment.Placement, SortOrder = assignment.SortOrder,
                    IsRequired = assignment.IsRequired, IsActive = assignment.IsActive, CreatedAtUtc = now
                });
            }

            return copy;
        }
        catch
        {
            await StudyRepository.DeleteAsync(copy.Id);
            throw;
        }
    }

    private static StimulusPost CloneStimulus(StimulusPost source, string studyId, IReadOnlyDictionary<string, string> groupMap, DateTime now) => new()
    {
        Id = Guid.NewGuid().ToString(), StudyId = studyId,
        GroupId = source.GroupId is not null && groupMap.TryGetValue(source.GroupId, out var groupId) ? groupId : null,
        ContentItemId = source.ContentItemId, ExperimentalFeedItemId = null, EngagementObservationId = source.EngagementObservationId,
        SourceCapturedAtUtc = source.SourceCapturedAtUtc, SourceMetadataJson = source.SourceMetadataJson,
        ItemManipulationJson = source.ItemManipulationJson, ObservedLikes = source.ObservedLikes,
        ObservedComments = source.ObservedComments, ObservedShares = source.ObservedShares, ObservedSaves = source.ObservedSaves,
        Title = source.Title, BodyText = source.BodyText, ContentType = source.ContentType, Platform = source.Platform,
        SourceName = source.SourceName, AuthorName = source.AuthorName, OriginalUrl = source.OriginalUrl,
        PublishedAtUtc = source.PublishedAtUtc, MediaPath = source.MediaPath, ThumbnailPath = source.ThumbnailPath,
        PublishedMediaUrl = source.PublishedMediaUrl,
        Category = source.Category, Topic = source.Topic, ConditionLabel = source.ConditionLabel,
        ExperimentalTag = source.ExperimentalTag, OriginalLikes = source.OriginalLikes,
        OriginalComments = source.OriginalComments, OriginalShares = source.OriginalShares,
        OriginalSaves = source.OriginalSaves, OriginalViews = source.OriginalViews,
        OrderIndex = source.OrderIndex, IsActive = source.IsActive,
        MinimumExposureMilliseconds = source.MinimumExposureMilliseconds,
        MaximumExposureMilliseconds = source.MaximumExposureMilliseconds,
        AllowRandomization = source.AllowRandomization, CustomMetadataJson = source.CustomMetadataJson,
        ResearcherNotes = source.ResearcherNotes, CreatedAtUtc = now, UpdatedAtUtc = now
    };

    private static string? DesignMetadataOnly(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var node = JsonNode.Parse(json);
            Scrub(node);
            return node?.ToJsonString();
        }
        catch
        {
            // Unstructured metadata cannot be proven free of deployment identity.
            return null;
        }
    }

    private static void Scrub(JsonNode? node)
    {
        if (node is JsonObject value)
        {
            var forbidden = value.Select(item => item.Key).Where(key =>
                key.Contains("deployment", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("publication", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("researchnumber", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("liveLink", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("syncCursor", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("aiConversation", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("IsDemo", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("ReadOnlyGuidedDemo", StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var key in forbidden) value.Remove(key);
            foreach (var child in value.Select(item => item.Value).ToArray()) Scrub(child);
        }
        else if (node is JsonArray array)
            foreach (var child in array) Scrub(child);
    }
}
