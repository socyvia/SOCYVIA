using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

/// <summary>Legacy synthetic-snapshot helper retained only for compatibility tests. It is not routed from researcher UI.</summary>
[Obsolete("Researcher preview opens the official SOCYVIA Demo. This helper is compatibility-only.")]
public static class ExperimentPreviewService
{
    public static async Task<ExperimentConfigurationSnapshot> CreateAsync(
        Study study,
        StudyGroup group,
        ExperimentalCondition condition)
    {
        var settings = ConditionManipulationService.Deserialize(condition.ManipulationJson);
        var seed = study.RandomizationSeed ?? StableSeed(study.Id, group.Id, condition.Id);
        var stimuli = await ExperimentFeedService.ResolveStimuliAsync(study, group, condition);
        var mode = settings.ContentOrderMode;
        if (mode == ContentOrderMode.Original && study.RandomizeStimuli)
            mode = ContentOrderMode.Random;
        var ordered = DeterministicRandomizationService.OrderStimuli(
            stimuli, mode, seed, settings.CustomPresentationJson);
        var participant = new Participant
        {
            Id = "preview-participant",
            StudyId = study.Id,
            GroupId = group.Id,
            ParticipantCode = "PREVIEW"
        };
        var session = new ExperimentSession
        {
            Id = "preview-session",
            StudyId = study.Id,
            ParticipantId = participant.Id,
            GroupId = group.Id,
            ConditionId = condition.Id
        };
        var assignment = new ParticipantConditionAssignment
        {
            Id = "preview-assignment",
            StudyId = study.Id,
            ParticipantId = participant.Id,
            ConditionId = condition.Id,
            AssignmentMethod = "Preview",
            RandomizationSeed = seed
        };
        return ExperimentConfigurationSnapshotService.Create(
            study, participant, group, condition, assignment, session,
            settings, ordered, seed);
    }

    private static int StableSeed(params string[] values)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", values)));
        return BitConverter.ToInt32(hash, 0) & int.MaxValue;
    }
}
