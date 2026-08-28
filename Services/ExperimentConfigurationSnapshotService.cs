using System;
using System.Collections.Generic;
using System.Linq;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

public static class ExperimentConfigurationSnapshotService
{
    /// <summary>Presentation-only snapshot for researcher preview; no participant or session identity is created.</summary>
    public static ExperimentConfigurationSnapshot CreatePresentationSnapshot(
        Study study,
        StudyGroup group,
        ExperimentalCondition condition,
        ConditionManipulationSettings manipulationSettings,
        IReadOnlyList<StimulusPost> orderedStimuli,
        int randomizationSeed) => new()
    {
        StudyId = study.Id,
        StudyDesign = study.DesignType,
        GroupId = group.Id,
        GroupName = group.Name,
        ConditionId = condition.Id,
        ConditionName = condition.Name,
        ConditionType = condition.ConditionType,
        ManipulationSettings = manipulationSettings,
        RandomizationSeed = randomizationSeed,
        RandomizationAlgorithm = DeterministicRandomizationService.AlgorithmVersion,
        Stimuli = orderedStimuli.Select((stimulus, index) => Snapshot(stimulus, index, condition.Id)).ToList(),
        ConsentRequired = study.RequireParticipantConsent,
        UsesStimuli = study.UsesStimuli,
        QuestionnaireModuleEnabled = study.UsesQuestionnaires,
        ExpectedSessionDurationMinutes = study.ExpectedSessionDurationMinutes,
        StudyMetadataJson = study.MetadataJson
    };

    public static ExperimentConfigurationSnapshot Create(
        Study study,
        Participant participant,
        StudyGroup group,
        ExperimentalCondition condition,
        ParticipantConditionAssignment assignment,
        ExperimentSession session,
        ConditionManipulationSettings manipulationSettings,
        IReadOnlyList<StimulusPost> orderedStimuli,
        int randomizationSeed)
    {
        var createdAt = DateTime.UtcNow;

        return new ExperimentConfigurationSnapshot
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = session.Id,
            StudyId = study.Id,
            StudyDesign = study.DesignType,
            ParticipantId = participant.Id,
            ParticipantCode = participant.ParticipantCode,
            GroupId = group.Id,
            GroupName = group.Name,
            ConditionId = condition.Id,
            ConditionAssignmentId = assignment.Id,
            AssignmentMethod = assignment.AssignmentMethod,
            ConditionName = condition.Name,
            ConditionType = condition.ConditionType,
            ManipulationSettings = manipulationSettings,
            RandomizationSeed = randomizationSeed,
            RandomizationAlgorithm =
                DeterministicRandomizationService.AlgorithmVersion,
            Stimuli = orderedStimuli
                .Select((stimulus, index) =>
                    Snapshot(stimulus, index, condition.Id))
                .ToList(),
            ConsentRequired = study.RequireParticipantConsent,
            UsesStimuli = study.UsesStimuli,
            QuestionnaireModuleEnabled = study.UsesQuestionnaires,
            PhysiologicalModuleEnabled =
                study.UsesPhysiologicalData,
            EegEnabled = study.EegEnabled,
            GsrEnabled = study.GsrEnabled,
            AllowSessionResume = study.AllowSessionResume,
            ExpectedSessionDurationMinutes =
                study.ExpectedSessionDurationMinutes,
            StudyMetadataJson = study.MetadataJson,
            CreatedAtUtc = createdAt
        };
    }


    private static SnapshotStimulus Snapshot(
        StimulusPost stimulus,
        int presentationOrder,
        string conditionId)
    {
        return new SnapshotStimulus
        {
            StimulusId = stimulus.Id,
            ContentItemId = stimulus.ContentItemId,
            ExperimentalFeedItemId = stimulus.ExperimentalFeedItemId,
            EngagementObservationId = stimulus.EngagementObservationId,
            SourceCapturedAtUtc = stimulus.SourceCapturedAtUtc,
            SourceMetadataJson = stimulus.SourceMetadataJson,
            ItemManipulationJson = stimulus.ItemManipulationJson,
            PresentationOrder = presentationOrder,
            GroupId = stimulus.GroupId,
            ConditionId = conditionId,
            Title = stimulus.Title,
            BodyText = stimulus.BodyText,
            ContentType = stimulus.ContentType,
            Platform = stimulus.Platform,
            SourceName = stimulus.SourceName,
            AuthorName = stimulus.AuthorName,
            OriginalUrl = stimulus.OriginalUrl,
            PublishedAtUtc = stimulus.PublishedAtUtc,
            MediaPath = stimulus.MediaPath,
            ThumbnailPath = stimulus.ThumbnailPath,
            PublishedMediaUrl = stimulus.PublishedMediaUrl,
            Category = stimulus.Category,
            Topic = stimulus.Topic,
            ConditionLabel = stimulus.ConditionLabel,
            ExperimentalTag = stimulus.ExperimentalTag,
            OriginalLikes = stimulus.ObservedLikes ?? stimulus.OriginalLikes,
            OriginalComments = stimulus.ObservedComments ?? stimulus.OriginalComments,
            OriginalShares = stimulus.ObservedShares ?? stimulus.OriginalShares,
            OriginalSaves = stimulus.ObservedSaves ?? stimulus.OriginalSaves,
            OriginalViews = stimulus.OriginalViews,
            MinimumExposureMilliseconds =
                stimulus.MinimumExposureMilliseconds,
            MaximumExposureMilliseconds =
                stimulus.MaximumExposureMilliseconds,
            CustomMetadataJson = stimulus.CustomMetadataJson
        };
    }
}
