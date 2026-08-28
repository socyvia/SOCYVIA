using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class ConditionAssignmentService
{
    public static async Task<List<ExperimentalCondition>>
        GetEligibleConditionsAsync(
            Participant participant)
    {
        var conditions =
            await ExperimentalConditionRepository
                .GetActiveByStudyAsync(participant.StudyId);

        return conditions
            .Where(condition =>
                IsCompatible(condition, participant.GroupId))
            .ToList();
    }


    public static async Task<ConditionAssignmentResult>
        EnsureAssignedAsync(
            Participant participant,
            ConditionAssignmentStrategy strategy,
            string? manualConditionId = null,
            int? randomizationSeed = null)
    {
        var eligibleConditions =
            await GetEligibleConditionsAsync(participant);

        if (eligibleConditions.Count == 0)
        {
            return Failure(
                "condition.none_eligible",
                "No active experimental condition is compatible with the participant group.",
                participant.Id);
        }

        var activeAssignment =
            await ParticipantConditionAssignmentRepository
                .GetActiveForParticipantAsync(participant.Id);

        if (activeAssignment is not null)
        {
            var activeCondition =
                eligibleConditions.FirstOrDefault(condition =>
                    string.Equals(
                        condition.Id,
                        activeAssignment.ConditionId,
                        StringComparison.Ordinal));

            var manualMatches =
                strategy != ConditionAssignmentStrategy.Manual ||
                string.IsNullOrWhiteSpace(manualConditionId) ||
                string.Equals(
                    manualConditionId,
                    activeAssignment.ConditionId,
                    StringComparison.Ordinal);

            if (activeCondition is not null && manualMatches)
            {
                return new ConditionAssignmentResult
                {
                    IsSuccessful = true,
                    WasCreated = false,
                    Assignment = activeAssignment,
                    Condition = activeCondition
                };
            }
        }

        var seed =
            randomizationSeed ??
            DeterministicRandomizationService.CreateSeed();

        ExperimentalCondition selectedCondition;

        switch (strategy)
        {
            case ConditionAssignmentStrategy.Manual:
                if (string.IsNullOrWhiteSpace(manualConditionId))
                {
                    return Failure(
                        "condition.manual_required",
                        "A condition must be selected for manual assignment.",
                        participant.Id);
                }

                selectedCondition =
                    eligibleConditions.FirstOrDefault(condition =>
                        string.Equals(
                            condition.Id,
                            manualConditionId,
                            StringComparison.Ordinal))!;

                if (selectedCondition is null)
                {
                    return Failure(
                        "condition.manual_incompatible",
                        "The selected condition is inactive or incompatible with the participant group.",
                        manualConditionId);
                }

                break;

            case ConditionAssignmentStrategy.Random:
                selectedCondition = eligibleConditions[
                    DeterministicRandomizationService.SelectIndex(
                        eligibleConditions.Count,
                        seed)];
                break;

            case ConditionAssignmentStrategy.BalancedRandom:
                selectedCondition =
                    await SelectBalancedAsync(
                        participant.StudyId,
                        eligibleConditions,
                        seed);
                break;

            default:
                return Failure(
                    "condition.strategy_unknown",
                    "The condition assignment strategy is not supported.",
                    participant.Id);
        }

        var assignment =
            new ParticipantConditionAssignment
            {
                Id = Guid.NewGuid().ToString(),
                StudyId = participant.StudyId,
                ParticipantId = participant.Id,
                ConditionId = selectedCondition.Id,
                AssignmentMethod = strategy.ToString(),
                RandomizationSeed = seed,
                AssignmentMetadataJson = JsonSerializer.Serialize(
                    new
                    {
                        algorithm =
                            DeterministicRandomizationService
                                .AlgorithmVersion,
                        eligibleConditionIds =
                            eligibleConditions.Select(condition =>
                                condition.Id)
                    }),
                AssignedAtUtc = DateTime.UtcNow,
                IsActive = true
            };

        await ParticipantConditionAssignmentRepository
            .CreateReplacingActiveAsync(assignment);

        return new ConditionAssignmentResult
        {
            IsSuccessful = true,
            WasCreated = true,
            Assignment = assignment,
            Condition = selectedCondition
        };
    }


    private static async Task<ExperimentalCondition>
        SelectBalancedAsync(
            string studyId,
            IReadOnlyList<ExperimentalCondition> eligibleConditions,
            int seed)
    {
        var assignments =
            await ParticipantConditionAssignmentRepository
                .GetByStudyAsync(
                    studyId,
                    activeOnly: true);

        var counts =
            eligibleConditions
                .Select(condition =>
                    new
                    {
                        Condition = condition,
                        Count = assignments.Count(assignment =>
                            string.Equals(
                                assignment.ConditionId,
                                condition.Id,
                                StringComparison.Ordinal))
                    })
                .ToList();

        var minimum = counts.Min(item => item.Count);
        var tied =
            counts
                .Where(item => item.Count == minimum)
                .Select(item => item.Condition)
                .OrderBy(condition => condition.SortOrder)
                .ThenBy(condition => condition.Id, StringComparer.Ordinal)
                .ToList();

        return tied[
            DeterministicRandomizationService.SelectIndex(
                tied.Count,
                seed)];
    }


    private static bool IsCompatible(
        ExperimentalCondition condition,
        string? participantGroupId)
    {
        if (string.IsNullOrWhiteSpace(condition.GroupId))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(participantGroupId) &&
               string.Equals(
                   condition.GroupId,
                   participantGroupId,
                   StringComparison.Ordinal);
    }


    private static ConditionAssignmentResult Failure(
        string code,
        string message,
        string? relatedEntityId)
    {
        return new ConditionAssignmentResult
        {
            IsSuccessful = false,
            Failures = new[]
            {
                new ConditionAssignmentFailure
                {
                    Code = code,
                    Message = message,
                    RelatedEntityId = relatedEntityId
                }
            }
        };
    }
}
