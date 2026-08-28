using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

/// <summary>Resolves the current participant feed presentation without creating any research identity or event.</summary>
public static class ParticipantPreviewService
{
    public static async Task<ParticipantPreviewContext> CreateAsync(Study study, StudyGroup group, ExperimentalCondition condition)
    {
        var settings = ConditionManipulationService.Deserialize(condition.ManipulationJson);
        var seed = study.RandomizationSeed ?? StableSeed(study.Id, group.Id, condition.Id);
        var stimuli = await ExperimentFeedService.ResolveStimuliAsync(study, group, condition);
        var order = settings.ContentOrderMode;
        if (order == ContentOrderMode.Original && study.RandomizeStimuli) order = ContentOrderMode.Random;
        var ordered = DeterministicRandomizationService.OrderStimuli(stimuli, order, seed, settings.CustomPresentationJson);
        var presentation = ExperimentConfigurationSnapshotService.CreatePresentationSnapshot(study, group, condition, settings, ordered, seed);
        return new ParticipantPreviewContext(study.Id, study.Title, group.Name, condition.Name, RuntimePresentationService.CreatePosts(presentation), DateTime.UtcNow);
    }

    private static int StableSeed(params string[] values) => BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", values))), 0) & int.MaxValue;
}
