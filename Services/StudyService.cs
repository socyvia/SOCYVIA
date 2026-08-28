using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class StudyService
{
    // =========================================================
    // DEFAULT GROUP COLORS
    // =========================================================

    private static readonly string[] DefaultGroupColors =
    {
        "#5B5FEF",
        "#14B8A6",
        "#D946EF",
        "#F59E0B",
        "#EF5A6F",
        "#06B6D4",
        "#8B5CF6",
        "#84CC16"
    };


    // =========================================================
    // CREATE STUDY
    // =========================================================

    public static async Task<Study> CreateStudyAsync(
        string researcherId,
        string title,
        string? description = null,
        int numberOfGroups = 3)
    {
        researcherId =
            researcherId.Trim();

        title =
            title.Trim();


        if (string.IsNullOrWhiteSpace(
                researcherId))
        {
            throw new ArgumentException(
                "Researcher ID is required.",
                nameof(researcherId));
        }


        if (string.IsNullOrWhiteSpace(
                title))
        {
            throw new ArgumentException(
                "Study title is required.",
                nameof(title));
        }


        if (numberOfGroups < 1)
        {
            numberOfGroups =
                1;
        }


        var study =
            new Study
            {
                Id =
                    Guid.NewGuid().ToString(),

                ResearcherId =
                    researcherId,

                Title =
                    title,

                Description =
                    string.IsNullOrWhiteSpace(
                        description)
                        ? null
                        : description.Trim(),

                Status =
                    "Draft",

                StudyType =
                    "Experimental",

                DesignType =
                    numberOfGroups > 1
                        ? "BetweenSubjects"
                        : "SingleGroup",

                AssignmentMethod =
                    "Manual",

                RandomizeStimuli =
                    false,

                UsesStimuli =
                    true,

                UsesQuestionnaires =
                    false,

                UsesPhysiologicalData =
                    false,

                EegEnabled =
                    false,

                GsrEnabled =
                    false,

                AllowSessionResume =
                    true,

                RequireParticipantConsent =
                    true,

                CreatedAtUtc =
                    DateTime.UtcNow,

                UpdatedAtUtc =
                    DateTime.UtcNow,

                IsArchived =
                    false
            };


        await StudyRepository.CreateAsync(
            study);


        await CreateDefaultGroupsAsync(
            study.Id,
            numberOfGroups);


        await ExperimentalConditionService
            .CreateMissingDefaultConditionsAsync(
                study.Id);


        return study;
    }


    // =========================================================
    // CREATE DEFAULT GROUPS
    // =========================================================

    private static async Task CreateDefaultGroupsAsync(
        string studyId,
        int numberOfGroups)
    {
        for (var index = 0;
             index < numberOfGroups;
             index++)
        {
            var group =
                new StudyGroup
                {
                    Id =
                        Guid.NewGuid().ToString(),

                    StudyId =
                        studyId,

                    Name =
                        $"Group {index + 1}",

                    Description =
                        null,

                    ColorHex =
                        DefaultGroupColors[
                            index %
                            DefaultGroupColors.Length],

                    IsControlGroup =
                        index == 0 &&
                        numberOfGroups > 1,

                    SortOrder =
                        index,

                    TargetSampleSize =
                        null,

                    IsActive =
                        true,

                    CreatedAtUtc =
                        DateTime.UtcNow,

                    UpdatedAtUtc =
                        DateTime.UtcNow
                };


            await GroupRepository.CreateAsync(
                group);
        }
    }


    // =========================================================
    // GET STUDIES
    // =========================================================

    public static async Task<List<Study>>
        GetStudiesAsync(
            string researcherId)
    {
        return await StudyRepository
            .GetByResearcherAsync(
                researcherId);
    }


    // =========================================================
    // GET STUDY
    // =========================================================

    public static async Task<Study?>
        GetStudyAsync(
            string studyId)
    {
        return await StudyRepository
            .GetByIdAsync(
                studyId);
    }


    // =========================================================
    // UPDATE STUDY
    // =========================================================

    public static async Task UpdateStudyAsync(
        Study study)
    {
        if (string.IsNullOrWhiteSpace(
                study.Title))
        {
            throw new ArgumentException(
                "Study title is required.");
        }


        study.Title =
            study.Title.Trim();


        if (!string.IsNullOrWhiteSpace(
                study.Description))
        {
            study.Description =
                study.Description.Trim();
        }


        await StudyRepository.UpdateAsync(
            study);
    }


    // =========================================================
    // GET GROUPS
    // =========================================================

    public static async Task<List<StudyGroup>>
        GetGroupsAsync(
            string studyId)
    {
        return await GroupRepository
            .GetByStudyAsync(
                studyId);
    }


    // =========================================================
    // ADD GROUP
    // =========================================================

    public static async Task<StudyGroup> AddGroupAsync(
        string studyId,
        string name,
        string? description = null,
        string? colorHex = null,
        int? targetSampleSize = null,
        bool isControlGroup = false)
    {
        name =
            name.Trim();


        if (string.IsNullOrWhiteSpace(
                name))
        {
            throw new ArgumentException(
                "Group name is required.",
                nameof(name));
        }


        var existingGroups =
            await GroupRepository
                .GetByStudyAsync(
                    studyId);


        var sortOrder =
            existingGroups.Count;


        var selectedColor =
            string.IsNullOrWhiteSpace(
                colorHex)
                ? DefaultGroupColors[
                    sortOrder %
                    DefaultGroupColors.Length]
                : colorHex;


        if (isControlGroup)
        {
            foreach (var existingGroup in
                     existingGroups.Where(
                         group =>
                             group.IsControlGroup))
            {
                existingGroup.IsControlGroup =
                    false;


                await GroupRepository.UpdateAsync(
                    existingGroup);
            }
        }


        var group =
            new StudyGroup
            {
                Id =
                    Guid.NewGuid().ToString(),

                StudyId =
                    studyId,

                Name =
                    name,

                Description =
                    string.IsNullOrWhiteSpace(
                        description)
                        ? null
                        : description.Trim(),

                ColorHex =
                    selectedColor,

                IsControlGroup =
                    isControlGroup,

                SortOrder =
                    sortOrder,

                TargetSampleSize =
                    targetSampleSize,

                IsActive =
                    true,

                CreatedAtUtc =
                    DateTime.UtcNow,

                UpdatedAtUtc =
                    DateTime.UtcNow
            };


        await GroupRepository.CreateAsync(
            group);


        return group;
    }


    // =========================================================
    // UPDATE GROUP
    // =========================================================

    public static async Task UpdateGroupAsync(
        StudyGroup group)
    {
        if (string.IsNullOrWhiteSpace(
                group.Name))
        {
            throw new ArgumentException(
                "Group name is required.");
        }


        group.Name =
            group.Name.Trim();


        if (!string.IsNullOrWhiteSpace(
                group.Description))
        {
            group.Description =
                group.Description.Trim();
        }


        if (group.IsControlGroup)
        {
            var groups =
                await GroupRepository
                    .GetByStudyAsync(
                        group.StudyId);


            foreach (var otherGroup in groups)
            {
                if (otherGroup.Id ==
                    group.Id)
                {
                    continue;
                }


                if (!otherGroup.IsControlGroup)
                {
                    continue;
                }


                otherGroup.IsControlGroup =
                    false;


                await GroupRepository
                    .UpdateAsync(
                        otherGroup);
            }
        }


        await GroupRepository.UpdateAsync(
            group);
    }


    // =========================================================
    // ARCHIVE STUDY
    // =========================================================

    public static async Task ArchiveStudyAsync(
        string studyId)
    {
        await StudyRepository.ArchiveAsync(
            studyId);
    }


    // =========================================================
    // DELETE STUDY PERMANENTLY
    // =========================================================

    public static async Task DeleteStudyAsync(
        string studyId)
    {
        if (string.IsNullOrWhiteSpace(
                studyId))
        {
            throw new ArgumentException(
                "Study ID is required.",
                nameof(studyId));
        }


        await StudyRepository.DeleteAsync(
            studyId);
    }
}
