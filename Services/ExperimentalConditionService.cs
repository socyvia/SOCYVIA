using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class ExperimentalConditionService
{
    public static async Task<ExperimentalCondition> CreateConditionAsync(
        string studyId,
        string name,
        string conditionType = "Custom",
        string? groupId = null,
        string? description = null,
        bool isControlCondition = false,
        string? manipulationJson = null,
        bool isActive = true)
    {
        ValidateStudyId(studyId);

        var normalizedName =
            ValidateAndNormalizeName(name);

        var normalizedConditionType =
            ValidateAndNormalizeConditionType(conditionType);

        await ValidateGroupLinkAsync(
            studyId,
            groupId);

        var existingConditions =
            await ExperimentalConditionRepository
                .GetByStudyAsync(studyId);

        EnsureUniqueName(
            existingConditions,
            normalizedName);

        var now =
            DateTime.UtcNow;

        var condition =
            new ExperimentalCondition
            {
                Id = Guid.NewGuid().ToString(),
                StudyId = studyId,
                GroupId = NormalizeOptional(groupId),
                Name = normalizedName,
                Description = NormalizeOptional(description),
                ConditionType = normalizedConditionType,
                SortOrder = existingConditions.Count == 0
                    ? 0
                    : existingConditions.Max(item => item.SortOrder) + 1,
                IsControlCondition =
                    isControlCondition && isActive,
                IsActive = isActive,
                ManipulationJson = NormalizeOptional(manipulationJson),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

        await ExperimentalConditionRepository
            .CreateAsync(condition);

        if (condition.IsControlCondition)
        {
            await ExperimentalConditionRepository
                .UnsetOtherControlConditionsAsync(
                    condition.StudyId,
                    condition.Id);
        }

        return condition;
    }


    public static async Task<List<ExperimentalCondition>>
        GetStudyConditionsAsync(
            string studyId)
    {
        ValidateStudyId(studyId);

        return await ExperimentalConditionRepository
            .GetByStudyAsync(studyId);
    }


    public static async Task<List<ExperimentalCondition>>
        GetActiveStudyConditionsAsync(
            string studyId)
    {
        ValidateStudyId(studyId);

        return await ExperimentalConditionRepository
            .GetActiveByStudyAsync(studyId);
    }


    public static async Task<ExperimentalCondition?>
        GetConditionAsync(
            string conditionId)
    {
        if (string.IsNullOrWhiteSpace(conditionId))
        {
            throw new ArgumentException(
                "Condition ID is required.",
                nameof(conditionId));
        }

        return await ExperimentalConditionRepository
            .GetByIdAsync(conditionId);
    }


    public static async Task UpdateConditionAsync(
        ExperimentalCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        if (string.IsNullOrWhiteSpace(condition.Id))
        {
            throw new ArgumentException(
                "Condition ID is required.",
                nameof(condition));
        }

        ValidateStudyId(condition.StudyId);

        var existing =
            await ExperimentalConditionRepository
                .GetByIdAsync(condition.Id)
            ?? throw new InvalidOperationException(
                "The experimental condition does not exist.");

        if (!string.Equals(
                existing.StudyId,
                condition.StudyId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "An experimental condition cannot be moved to another study.");
        }

        condition.Name =
            ValidateAndNormalizeName(condition.Name);

        var studyConditions =
            await ExperimentalConditionRepository
                .GetByStudyAsync(condition.StudyId);

        EnsureUniqueName(
            studyConditions,
            condition.Name,
            condition.Id);

        condition.ConditionType =
            ValidateAndNormalizeConditionType(
                condition.ConditionType);

        condition.GroupId =
            NormalizeOptional(condition.GroupId);

        condition.Description =
            NormalizeOptional(condition.Description);

        condition.ManipulationJson =
            NormalizeOptional(condition.ManipulationJson);

        condition.CreatedAtUtc =
            existing.CreatedAtUtc;

        if (!condition.IsActive)
        {
            condition.IsControlCondition = false;
        }

        await ValidateGroupLinkAsync(
            condition.StudyId,
            condition.GroupId);

        await ExperimentalConditionRepository
            .UpdateAsync(condition);

        if (condition.IsControlCondition)
        {
            await ExperimentalConditionRepository
                .UnsetOtherControlConditionsAsync(
                    condition.StudyId,
                    condition.Id);
        }
    }


    public static async Task DeleteConditionAsync(
        string conditionId)
    {
        var condition =
            await GetConditionAsync(conditionId);

        if (condition is null)
        {
            return;
        }

        var assignmentCount =
            await ParticipantConditionAssignmentRepository
                .CountByConditionAsync(conditionId);

        if (assignmentCount > 0)
        {
            throw new InvalidOperationException(
                "A condition with participant assignment history cannot be deleted. Deactivate it instead.");
        }

        await ExperimentalConditionRepository
            .DeleteAsync(conditionId);

        await NormalizeSortOrderAsync(
            condition.StudyId);
    }


    public static async Task NormalizeSortOrderAsync(
        string studyId)
    {
        ValidateStudyId(studyId);

        var conditions =
            await ExperimentalConditionRepository
                .GetByStudyAsync(studyId);

        for (var index = 0;
             index < conditions.Count;
             index++)
        {
            var condition =
                conditions[index];

            if (condition.SortOrder == index)
            {
                continue;
            }

            condition.SortOrder = index;

            await ExperimentalConditionRepository
                .UpdateAsync(condition);
        }
    }


    public static async Task MoveConditionAsync(
        string studyId,
        string conditionId,
        int direction)
    {
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction));
        }

        var conditions =
            await GetStudyConditionsAsync(studyId);

        var currentIndex =
            conditions.FindIndex(item =>
                string.Equals(
                    item.Id,
                    conditionId,
                    StringComparison.Ordinal));

        var targetIndex =
            currentIndex + direction;

        if (currentIndex < 0 ||
            targetIndex < 0 ||
            targetIndex >= conditions.Count)
        {
            return;
        }

        (conditions[currentIndex], conditions[targetIndex]) =
            (conditions[targetIndex], conditions[currentIndex]);

        for (var index = 0;
             index < conditions.Count;
             index++)
        {
            var condition =
                conditions[index];

            if (condition.SortOrder == index)
            {
                continue;
            }

            condition.SortOrder = index;

            await ExperimentalConditionRepository
                .UpdateAsync(condition);
        }
    }


    public static async Task<List<ExperimentalCondition>>
        CreateMissingDefaultConditionsAsync(
            string studyId)
    {
        ValidateStudyId(studyId);

        var groups =
            await GroupRepository
                .GetByStudyAsync(studyId);

        var existingConditions =
            await ExperimentalConditionRepository
                .GetByStudyAsync(studyId);

        var linkedGroupIds =
            existingConditions
                .Where(item => !string.IsNullOrWhiteSpace(item.GroupId))
                .Select(item => item.GroupId!)
                .ToHashSet(StringComparer.Ordinal);

        var hasControlCondition =
            existingConditions.Any(item => item.IsControlCondition);

        var createdConditions =
            new List<ExperimentalCondition>();

        for (var index = 0;
             index < groups.Count;
             index++)
        {
            var group =
                groups[index];

            if (linkedGroupIds.Contains(group.Id))
            {
                continue;
            }

            var shouldBeControl =
                groups.Count > 1 &&
                group.IsControlGroup &&
                !hasControlCondition;

            var defaultName =
                BuildUniqueDefaultName(
                    existingConditions,
                    createdConditions,
                    $"Condition {index + 1}");

            var condition =
                await CreateConditionAsync(
                    studyId,
                    defaultName,
                    conditionType: "Custom",
                    groupId: group.Id,
                    isControlCondition: shouldBeControl);

            createdConditions.Add(condition);
            linkedGroupIds.Add(group.Id);

            if (shouldBeControl)
            {
                hasControlCondition = true;
            }
        }

        return createdConditions;
    }


    private static string BuildUniqueDefaultName(
        IEnumerable<ExperimentalCondition> existingConditions,
        IEnumerable<ExperimentalCondition> createdConditions,
        string baseName)
    {
        var names =
            existingConditions
                .Concat(createdConditions)
                .Select(condition => condition.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!names.Contains(baseName))
        {
            return baseName;
        }

        for (var suffix = 2;
             ;
             suffix++)
        {
            var candidate =
                $"{baseName} ({suffix})";

            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }


    private static async Task ValidateGroupLinkAsync(
        string studyId,
        string? groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return;
        }

        var groups =
            await GroupRepository
                .GetByStudyAsync(studyId);

        if (groups.All(group =>
                !string.Equals(
                    group.Id,
                    groupId,
                    StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "The linked group does not belong to this study.",
                nameof(groupId));
        }
    }


    private static void ValidateStudyId(
        string studyId)
    {
        if (string.IsNullOrWhiteSpace(studyId))
        {
            throw new ArgumentException(
                "Study ID is required.",
                nameof(studyId));
        }
    }


    private static string ValidateAndNormalizeName(
        string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Condition name is required.",
                nameof(name));
        }

        return name.Trim();
    }


    private static void EnsureUniqueName(
        IEnumerable<ExperimentalCondition> conditions,
        string name,
        string? excludedConditionId = null)
    {
        if (conditions.Any(condition =>
                !string.Equals(
                    condition.Id,
                    excludedConditionId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    condition.Name.Trim(),
                    name,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "A condition with this name already exists.",
                nameof(name));
        }
    }


    private static string ValidateAndNormalizeConditionType(
        string conditionType)
    {
        if (string.IsNullOrWhiteSpace(conditionType))
        {
            throw new ArgumentException(
                "Condition type is required.",
                nameof(conditionType));
        }

        return conditionType.Trim();
    }


    private static string? NormalizeOptional(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
