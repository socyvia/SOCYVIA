using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class ExperimentLaunchService
{
    public static async Task<ExperimentLaunchResult> PrepareAsync(
        ExperimentLaunchRequest request)
    {
        var failures =
            new List<ExperimentLaunchFailure>();

        var study =
            await StudyRepository.GetByIdAsync(request.StudyId);

        if (study is null)
        {
            return Failure(
                "study.not_found",
                "The study does not exist.",
                request.StudyId);
        }

        var readiness =
            await ExperimentReadinessService
                .EvaluateAsync(study);

        failures.AddRange(
            readiness.Checks
                .Where(check =>
                    !check.IsPassed &&
                    check.Severity ==
                        ExperimentReadinessSeverity.Error)
                .Select(check =>
                    new ExperimentLaunchFailure
                    {
                        Code = check.Code,
                        Message = check.CanonicalMessage,
                        RelatedEntityId = check.RelatedEntityId
                    }));

        var participant =
            await ParticipantRepository
                .GetByIdAsync(request.ParticipantId);

        if (participant is null ||
            !string.Equals(
                participant.StudyId,
                study.Id,
                StringComparison.Ordinal))
        {
            failures.Add(new ExperimentLaunchFailure
            {
                Code = "participant.not_found",
                Message =
                    "The participant does not belong to this study.",
                RelatedEntityId = request.ParticipantId
            });

            return Failed(readiness, failures);
        }

        ValidateParticipant(
            study,
            participant,
            failures);

        var groups =
            await GroupRepository
                .GetByStudyAsync(study.Id);

        var group =
            groups.FirstOrDefault(item =>
                item.IsActive &&
                string.Equals(
                    item.Id,
                    participant.GroupId,
                    StringComparison.Ordinal));

        if (group is null)
        {
            failures.Add(new ExperimentLaunchFailure
            {
                Code = "participant.group_invalid",
                Message =
                    "The participant requires a valid active study group.",
                RelatedEntityId = participant.Id
            });
        }

        if (failures.Count > 0 || group is null)
        {
            return Failed(readiness, failures);
        }

        var assignmentResult =
            await ConditionAssignmentService
                .EnsureAssignedAsync(
                    participant,
                    request.AssignmentStrategy,
                    request.ManualConditionId,
                    request.RandomizationSeed);

        if (!assignmentResult.IsSuccessful ||
            assignmentResult.Assignment is null ||
            assignmentResult.Condition is null)
        {
            failures.AddRange(
                assignmentResult.Failures.Select(failure =>
                    new ExperimentLaunchFailure
                    {
                        Code = failure.Code,
                        Message = failure.Message,
                        RelatedEntityId = failure.RelatedEntityId
                    }));

            return Failed(readiness, failures);
        }

        var condition = assignmentResult.Condition;
        var assignment = assignmentResult.Assignment;

        var stimuli =
            await ExperimentFeedService.ResolveStimuliAsync(
                study,
                group,
                condition);

        if (study.UsesStimuli && stimuli.Count == 0)
        {
            failures.Add(new ExperimentLaunchFailure
            {
                Code = "stimuli.none_for_group",
                Message =
                    "No active stimulus is available for the participant group.",
                RelatedEntityId = group.Id
            });

            return Failed(readiness, failures);
        }

        var manipulationSettings =
            ConditionManipulationService.Deserialize(
                condition.ManipulationJson);

        var presentationSeed =
            request.RandomizationSeed ??
            assignment.RandomizationSeed ??
            DeterministicRandomizationService.CreateSeed();

        var orderMode =
            manipulationSettings.ContentOrderMode;

        if (orderMode == ContentOrderMode.Original &&
            study.RandomizeStimuli)
        {
            orderMode = ContentOrderMode.Random;
        }

        var orderedStimuli =
            DeterministicRandomizationService.OrderStimuli(
                stimuli,
                orderMode,
                presentationSeed,
                manipulationSettings.CustomPresentationJson);

        var now = DateTime.UtcNow;
        var session = new ExperimentSession
        {
            Id = Guid.NewGuid().ToString(),
            StudyId = participant.StudyId,
            ParticipantId = participant.Id,
            GroupId = participant.GroupId,
            ConditionId = condition.Id,
            Status = SessionLifecycleStates.Created,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            LifecycleVersion = 2,
            EegEnabled = study.EegEnabled,
            GsrEnabled = study.GsrEnabled
        };

        var snapshot =
            ExperimentConfigurationSnapshotService.Create(
                study,
                participant,
                group,
                condition,
                assignment,
                session,
                manipulationSettings,
                orderedStimuli,
                presentationSeed);

        try
        {
            session = await ExperimentPreparationRepository
                .CreatePreparedAsync(session, snapshot);
            if (await ResearchDataClassificationService.IsDemoAsync("Study", study.Id))
            {
                await ResearchDataClassificationService.ClassifyAsync(
                    "Session",
                    session.Id,
                    study.Id,
                    isDemo: true,
                    isSynthetic: true,
                    source: ResearchDataClassificationService.DemoSource);
            }
        }
        catch (DuplicateActiveSessionException exception)
        {
            failures.Add(new ExperimentLaunchFailure
            {
                Code = "session.active_exists",
                Message =
                    "A Ready, Running, or Paused session already exists for this participant.",
                RelatedEntityId = exception.SessionId
            });
            return Failed(readiness, failures);
        }

        return new ExperimentLaunchResult
        {
            IsSuccessful = true,
            Readiness = readiness,
            Context = new ExperimentLaunchContext
            {
                Study = study,
                Participant = participant,
                Group = group,
                Condition = condition,
                ConditionAssignment = assignment,
                Session = session,
                Snapshot = snapshot,
                ResolvedStimuli = orderedStimuli,
                ManipulationSettings = manipulationSettings,
                Readiness = readiness
            }
        };
    }


    private static void ValidateParticipant(
        Study study,
        Participant participant,
        ICollection<ExperimentLaunchFailure> failures)
    {
        if (!participant.IsEligible)
        {
            failures.Add(new ExperimentLaunchFailure
            {
                Code = "participant.ineligible",
                Message = "The participant is not eligible.",
                RelatedEntityId = participant.Id
            });
        }

        if (participant.IsExcluded)
        {
            failures.Add(new ExperimentLaunchFailure
            {
                Code = "participant.excluded",
                Message = "The participant is excluded.",
                RelatedEntityId = participant.Id
            });
        }

        if (participant.HasWithdrawn)
        {
            failures.Add(new ExperimentLaunchFailure
            {
                Code = "participant.withdrawn",
                Message = "The participant has withdrawn.",
                RelatedEntityId = participant.Id
            });
        }

        if (study.RequireParticipantConsent &&
            !participant.ConsentAccepted)
        {
            failures.Add(new ExperimentLaunchFailure
            {
                Code = "participant.consent_required",
                Message = "Participant consent is required.",
                RelatedEntityId = participant.Id
            });
        }
    }


    private static ExperimentLaunchResult Failure(
        string code,
        string message,
        string? relatedEntityId)
    {
        return new ExperimentLaunchResult
        {
            IsSuccessful = false,
            Failures = new[]
            {
                new ExperimentLaunchFailure
                {
                    Code = code,
                    Message = message,
                    RelatedEntityId = relatedEntityId
                }
            }
        };
    }


    private static ExperimentLaunchResult Failed(
        ExperimentReadinessResult readiness,
        IReadOnlyList<ExperimentLaunchFailure> failures)
    {
        return new ExperimentLaunchResult
        {
            IsSuccessful = false,
            Readiness = readiness,
            Failures = failures
        };
    }
}
