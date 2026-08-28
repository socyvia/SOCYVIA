using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public sealed record PreparedRemotePublication(
    ExperimentPackage Package,
    DeploymentEntryConfiguration Entry,
    IReadOnlyList<DeploymentTextContent> Content,
    IReadOnlyList<DeploymentQuestionnaireDefinition> Questionnaires,
    IReadOnlyList<StudyGroup> Groups,
    IReadOnlyList<ExperimentalCondition> Conditions);

/// <summary>Builds one immutable publication input from the current persisted study design.</summary>
public static class RemotePublicationPreparationService
{
    public static async Task<PreparedRemotePublication> PrepareAsync(Study study)
    {
        var groups = (await GroupRepository.GetByStudyAsync(study.Id)).Where(item => item.IsActive)
            .OrderBy(item => item.SortOrder).ToArray();
        var conditions = (await ExperimentalConditionRepository.GetByStudyAsync(study.Id)).Where(item => item.IsActive)
            .OrderBy(item => item.SortOrder).ToArray();
        var snapshots = new List<SnapshotStimulus>();
        var content = new List<DeploymentTextContent>();
        foreach (var condition in conditions)
        {
            var group = groups.FirstOrDefault(item => item.Id == condition.GroupId) ?? groups.FirstOrDefault();
            if (group is null) continue;
            var stimuli = await ExperimentFeedService.ResolveStimuliAsync(study, group, condition);
            var presentation = ExperimentConfigurationSnapshotService.CreatePresentationSnapshot(
                study, group, condition, ConditionManipulationService.Deserialize(condition.ManipulationJson),
                stimuli, study.RandomizationSeed ?? 0);
            snapshots.AddRange(presentation.Stimuli);
            content.AddRange(presentation.Stimuli.Select(item => new DeploymentTextContent
            {
                Id = StablePresentationId(condition.Id, item.StimulusId),
                ContentId = item.ContentItemId ?? item.StimulusId,
                ConditionId = condition.Id,
                SortOrder = item.PresentationOrder,
                Language = LocalizationService.IsArabic ? "ar" : "en",
                Title = item.Title,
                Body = item.BodyText,
                Media = ExternalMedia(item),
                LikeEnabled = false,
                CommentEnabled = false,
                ReadMoreEnabled = !string.IsNullOrWhiteSpace(item.OriginalUrl),
                SaveEnabled = false,
                ShareEnabled = false,
                CollectCommentText = false
            }));
        }

        var assignments = await QuestionnaireRepository.GetAssignmentsAsync(study.Id);
        var questionnaireDefinitions = assignments.Where(item => item.IsActive && item.Version is not null && item.Questionnaire is not null)
            .Select(ToDefinition).ToArray();
        var questionnaireReferences = assignments.Where(item => item.IsActive && item.Version is not null && item.Questionnaire is not null)
            .Select(item => new QuestionnaireVersionReference(
                item.Questionnaire!.Id, item.Version!.Id,
                item.Version.VersionLabel ?? item.Version.VersionNumber.ToString(), item.Version.Language)).ToArray();
        var primaryLanguages = assignments.Select(item => item.Version?.Language).Where(value => value is "en" or "ar")
            .Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
        if (primaryLanguages.Length == 0) primaryLanguages = [LocalizationService.IsArabic ? "ar" : "en"];
        var defaultLanguage = primaryLanguages.Contains(LocalizationService.IsArabic ? "ar" : "en", StringComparer.Ordinal)
            ? (LocalizationService.IsArabic ? "ar" : "en")
            : primaryLanguages[0];

        var snapshot = new ExperimentConfigurationSnapshot
        {
            StudyId = study.Id,
            StudyDesign = study.DesignType,
            AssignmentMethod = study.AssignmentMethod,
            RandomizationSeed = study.RandomizationSeed ?? 0,
            RandomizationAlgorithm = DeterministicRandomizationService.AlgorithmVersion,
            Stimuli = snapshots,
            ConsentRequired = study.RequireParticipantConsent,
            UsesStimuli = study.UsesStimuli,
            QuestionnaireModuleEnabled = study.UsesQuestionnaires,
            AllowSessionResume = study.AllowSessionResume,
            ExpectedSessionDurationMinutes = study.ExpectedSessionDurationMinutes,
            StudyMetadataJson = study.MetadataJson
        };
        var package = RemoteExperimentFoundationService.BuildPackage(snapshot, groups, conditions,
            questionnaireReferences, defaultRuntimeLanguage: defaultLanguage);
        package = package with
        {
            Study = new ExperimentPackageStudyMetadata(study.Title, study.StudyType, study.DesignType,
                study.RequireParticipantConsent, study.ExpectedSessionDurationMinutes, study.MetadataJson),
            ParticipantFlow = new ParticipantFlowContract(
                study.RequireParticipantConsent,
                questionnaireDefinitions.Any(item => item.Stage == QuestionnaireStage.Pre),
                study.UsesStimuli,
                questionnaireDefinitions.Any(item => item.Stage == QuestionnaireStage.Post),
                "StartExperiment")
        };
        package = package with { ConfigurationHash = RemoteExperimentFoundationService.ComputeConfigurationHash(package) };

        var researcher = ResearcherService.GetProfile(study.ResearcherId);
        var entry = new DeploymentEntryConfiguration
        {
            ResearcherName = researcher?.FullName ?? string.Empty,
            ResearcherRole = LocalizationService.IsArabic ? "الباحث" : "Researcher",
            StudyTitle = study.Title,
            StudyDescription = study.Description,
            StudyInformation = study.PopulationDescription,
            ParticipantInstructions = null,
            PrivacyText = null,
            EstimatedDurationMinutes = study.ExpectedSessionDurationMinutes,
            EstimatedDuration = study.ExpectedSessionDurationMinutes is { } minutes ? $"{minutes} min" : null,
            Language = defaultLanguage,
            ParticipantInterfaceLanguages = primaryLanguages,
            DefaultParticipantInterfaceLanguage = defaultLanguage,
            ConsentRequired = study.RequireParticipantConsent,
            ConsentText = study.ConsentText ?? string.Empty,
            PreQuestionnaireConfigured = questionnaireDefinitions.Any(item => item.Stage == QuestionnaireStage.Pre),
            PostQuestionnaireConfigured = questionnaireDefinitions.Any(item => item.Stage == QuestionnaireStage.Post)
        };
        return new PreparedRemotePublication(package, entry, content, questionnaireDefinitions, groups, conditions);
    }

    private static DeploymentQuestionnaireDefinition ToDefinition(QuestionnaireAssignment assignment)
    {
        var version = assignment.Version!;
        var questionnaire = assignment.Questionnaire!;
        var stage = assignment.Placement == QuestionnairePlacements.PreExperiment ? QuestionnaireStage.Pre : QuestionnaireStage.Post;
        var language = version.Language is "ar" ? "ar" : "en";
        var items = version.Questions.OrderBy(item => item.SortOrder).Select(item =>
        {
            var configuration = Configuration(item);
            return new DeploymentQuestionnaireItem
            {
                Id = item.Id,
                Type = Type(item.QuestionType),
                Question = item.QuestionText,
                Required = item.IsRequired,
                Order = item.SortOrder,
                ConfigurationJson = configuration,
                Localizations = new Dictionary<string, DeploymentQuestionnaireItemLocalization>
                {
                    [language] = new(item.QuestionText, ConfigurationJson: configuration)
                }
            };
        }).ToArray();
        return new DeploymentQuestionnaireDefinition
        {
            Id = questionnaire.Id,
            VersionId = version.Id,
            Stage = stage,
            Title = questionnaire.Title,
            Description = questionnaire.Description,
            Required = assignment.IsRequired,
            Items = items,
            Localizations = new Dictionary<string, DeploymentQuestionnaireLocalization>
            {
                [language] = new(questionnaire.Title, questionnaire.Description)
            }
        };
    }

    private static string Configuration(Question question)
    {
        if (!string.IsNullOrWhiteSpace(question.ConfigurationJson))
        {
            try { using var _ = JsonDocument.Parse(question.ConfigurationJson); return question.ConfigurationJson; }
            catch (JsonException) { }
        }
        return JsonSerializer.Serialize(new
        {
            options = question.Options.OrderBy(item => item.SortOrder).Select(item => new
            {
                id = item.Id, value = item.ValueCode, numeric = item.NumericCode, label = item.DisplayLabel
            })
        });
    }

    private static QuestionnaireItemType Type(string value) => value switch
    {
        QuestionnaireQuestionTypes.Likert => QuestionnaireItemType.Likert,
        QuestionnaireQuestionTypes.SingleChoice => QuestionnaireItemType.SingleChoice,
        QuestionnaireQuestionTypes.MultipleChoice => QuestionnaireItemType.MultipleChoice,
        QuestionnaireQuestionTypes.ShortText => QuestionnaireItemType.ShortText,
        QuestionnaireQuestionTypes.LongText => QuestionnaireItemType.LongText,
        QuestionnaireQuestionTypes.Numeric => QuestionnaireItemType.Number,
        QuestionnaireQuestionTypes.YesNo => QuestionnaireItemType.YesNo,
        _ => QuestionnaireItemType.ShortText
    };

    private static DeploymentContentMedia? ExternalMedia(SnapshotStimulus stimulus)
    {
        if (stimulus.ContentType.Equals("Link", StringComparison.OrdinalIgnoreCase))
        {
            return PublishedMediaUrlValidator.TryValidate(stimulus.OriginalUrl, out var externalPage, out _)
                ? new DeploymentContentMedia("external", externalPage!.AbsoluteUri, stimulus.Title)
                : null;
        }

        var reference = !string.IsNullOrWhiteSpace(stimulus.PublishedMediaUrl)
            ? stimulus.PublishedMediaUrl
            : stimulus.MediaPath ?? stimulus.ThumbnailPath;
        if (!PublishedMediaUrlValidator.TryValidateDirectMedia(reference, out var uri, out _))
            return null;
        return new DeploymentContentMedia(MediaKind(stimulus.ContentType, uri!.AbsolutePath), uri.AbsoluteUri, stimulus.Title);
    }

    private static string MediaKind(string? contentType, string reference)
    {
        var value = $"{contentType} {Path.GetExtension(reference)}".ToLowerInvariant();
        if (value.Contains("video", StringComparison.Ordinal) || value.Contains(".mp4", StringComparison.Ordinal)) return "video";
        if (value.Contains("audio", StringComparison.Ordinal) || value.Contains(".mp3", StringComparison.Ordinal) || value.Contains(".wav", StringComparison.Ordinal)) return "audio";
        if (value.Contains("image", StringComparison.Ordinal) || value.Contains(".jpg", StringComparison.Ordinal) || value.Contains(".jpeg", StringComparison.Ordinal) || value.Contains(".png", StringComparison.Ordinal) || value.Contains(".webp", StringComparison.Ordinal)) return "image";
        return "external";
    }

    private static string StablePresentationId(string conditionId, string stimulusId)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{conditionId}|{stimulusId}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
