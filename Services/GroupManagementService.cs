using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class GroupManagementService
{
    private static readonly string[] DefaultColors =
    {
        "#6259EA",
        "#29A98B",
        "#E49A37",
        "#D85C78",
        "#3D8BD9",
        "#8B6FC0"
    };


    public static async Task<List<StudyGroup>> GetGroupsAsync(
        string studyId)
    {
        ValidateStudyId(studyId);

        return await GroupRepository
            .GetByStudyAsync(studyId);
    }


    public static async Task<StudyGroup> CreateGroupAsync(
        string studyId,
        string name,
        string? description = null,
        int? targetSampleSize = null,
        bool isControlGroup = false,
        bool isActive = true)
    {
        ValidateStudyId(studyId);
        ValidateTargetSampleSize(targetSampleSize);

        var normalizedName =
            ValidateAndNormalizeName(name);

        var groups =
            await GroupRepository
                .GetByStudyAsync(studyId);

        EnsureUniqueName(
            groups,
            normalizedName);

        var now =
            DateTime.UtcNow;

        var group =
            new StudyGroup
            {
                Id = Guid.NewGuid().ToString(),
                StudyId = studyId,
                Name = normalizedName,
                Description = NormalizeOptional(description),
                ColorHex = DefaultColors[
                    groups.Count % DefaultColors.Length],
                IsControlGroup = isControlGroup && isActive,
                SortOrder = groups.Count == 0
                    ? 0
                    : groups.Max(item => item.SortOrder) + 1,
                TargetSampleSize = targetSampleSize,
                IsActive = isActive,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

        await GroupRepository.CreateAsync(group);

        if (group.IsControlGroup)
        {
            await GroupRepository
                .UnsetOtherControlGroupsAsync(
                    group.StudyId,
                    group.Id);
        }

        return group;
    }


    public static async Task UpdateGroupAsync(
        StudyGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        ValidateStudyId(group.StudyId);
        ValidateTargetSampleSize(group.TargetSampleSize);

        if (string.IsNullOrWhiteSpace(group.Id))
        {
            throw new ArgumentException(
                "Group ID is required.",
                nameof(group));
        }

        var groups =
            await GroupRepository
                .GetByStudyAsync(group.StudyId);

        var existing =
            groups.FirstOrDefault(item =>
                string.Equals(
                    item.Id,
                    group.Id,
                    StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                "The study group does not exist.");

        group.Name =
            ValidateAndNormalizeName(group.Name);

        EnsureUniqueName(
            groups,
            group.Name,
            group.Id);

        group.Description =
            NormalizeOptional(group.Description);

        group.CreatedAtUtc =
            existing.CreatedAtUtc;

        group.ColorHex =
            NormalizeOptional(group.ColorHex)
            ?? "#6259EA";

        if (!group.IsActive)
        {
            group.IsControlGroup = false;
        }

        await GroupRepository.UpdateAsync(group);

        if (group.IsControlGroup)
        {
            await GroupRepository
                .UnsetOtherControlGroupsAsync(
                    group.StudyId,
                    group.Id);
        }
    }


    public static async Task MoveGroupAsync(
        string studyId,
        string groupId,
        int direction)
    {
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction));
        }

        var groups =
            await GetGroupsAsync(studyId);

        var currentIndex =
            groups.FindIndex(item =>
                string.Equals(
                    item.Id,
                    groupId,
                    StringComparison.Ordinal));

        var targetIndex =
            currentIndex + direction;

        if (currentIndex < 0 ||
            targetIndex < 0 ||
            targetIndex >= groups.Count)
        {
            return;
        }

        (groups[currentIndex], groups[targetIndex]) =
            (groups[targetIndex], groups[currentIndex]);

        await PersistOrderAsync(groups);
    }


    public static async Task<GroupDeletionResult>
        DeleteGroupIfUnusedAsync(
            string groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            throw new ArgumentException(
                "Group ID is required.",
                nameof(groupId));
        }

        var usage =
            await GroupRepository
                .GetUsageAsync(groupId);

        if (usage.HasAnyUsage)
        {
            return new GroupDeletionResult
            {
                WasDeleted = false,
                RequiresDeactivation = true,
                Usage = usage
            };
        }

        var existingGroup =
            await GroupRepository
                .GetByIdAsync(groupId);

        var groups = existingGroup is null
            ? new List<StudyGroup>()
            : await GroupRepository
                .GetByStudyAsync(existingGroup.StudyId);

        var deleted =
            await GroupRepository
                .TryDeleteUnusedAsync(groupId);

        if (deleted && groups.Count > 0)
        {
            groups.RemoveAll(item =>
                string.Equals(
                    item.Id,
                    groupId,
                    StringComparison.Ordinal));

            await PersistOrderAsync(groups);
        }

        return new GroupDeletionResult
        {
            WasDeleted = deleted,
            RequiresDeactivation = !deleted,
            Usage = deleted
                ? usage
                : await GroupRepository.GetUsageAsync(groupId)
        };
    }


    private static async Task PersistOrderAsync(
        IReadOnlyList<StudyGroup> groups)
    {
        for (var index = 0;
             index < groups.Count;
             index++)
        {
            var group =
                groups[index];

            if (group.SortOrder == index)
            {
                continue;
            }

            group.SortOrder = index;

            await GroupRepository.UpdateAsync(group);
        }
    }


    private static void EnsureUniqueName(
        IEnumerable<StudyGroup> groups,
        string name,
        string? excludedGroupId = null)
    {
        if (groups.Any(group =>
                !string.Equals(
                    group.Id,
                    excludedGroupId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    group.Name.Trim(),
                    name,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "A group with this name already exists.",
                nameof(name));
        }
    }


    private static string ValidateAndNormalizeName(
        string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Group name is required.",
                nameof(name));
        }

        return name.Trim();
    }


    private static void ValidateTargetSampleSize(
        int? targetSampleSize)
    {
        if (targetSampleSize < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetSampleSize),
                "Target sample size cannot be negative.");
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


    private static string? NormalizeOptional(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
