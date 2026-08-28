using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SOCYVIA.Data;
using SOCYVIA.Models;

namespace SOCYVIA.Repositories;

public static class StudyRepository
{
    // =========================================================
    // CREATE
    // =========================================================

    public static async Task CreateAsync(
        Study study)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            INSERT INTO Studies
            (
                Id,
                ResearcherId,
                Title,
                Description,
                Status,

                StudyType,
                DesignType,
                AssignmentMethod,

                RandomizeStimuli,
                RandomizationSeed,

                UsesStimuli,
                UsesQuestionnaires,
                UsesPhysiologicalData,

                EegEnabled,
                GsrEnabled,

                TargetSampleSize,
                ExpectedSessionDurationMinutes,
                AllowSessionResume,

                RequireParticipantConsent,
                ConsentText,

                ResearchQuestion,
                Hypothesis,
                PopulationDescription,
                InclusionCriteria,
                ExclusionCriteria,

                MetadataJson,

                CreatedAtUtc,
                UpdatedAtUtc,
                StartedAtUtc,
                CompletedAtUtc,

                IsArchived
            )
            VALUES
            (
                $id,
                $researcherId,
                $title,
                $description,
                $status,

                $studyType,
                $designType,
                $assignmentMethod,

                $randomizeStimuli,
                $randomizationSeed,

                $usesStimuli,
                $usesQuestionnaires,
                $usesPhysiologicalData,

                $eegEnabled,
                $gsrEnabled,

                $targetSampleSize,
                $expectedSessionDurationMinutes,
                $allowSessionResume,

                $requireParticipantConsent,
                $consentText,

                $researchQuestion,
                $hypothesis,
                $populationDescription,
                $inclusionCriteria,
                $exclusionCriteria,

                $metadataJson,

                $createdAtUtc,
                $updatedAtUtc,
                $startedAtUtc,
                $completedAtUtc,

                $isArchived
            );
            """;

        AddParameters(
            command,
            study);

        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // GET BY RESEARCHER
    // =========================================================

    public static async Task<List<Study>>
        GetByResearcherAsync(
            string researcherId)
    {
        var studies =
            new List<Study>();

        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT
                Id,
                ResearcherId,
                Title,
                Description,
                Status,

                StudyType,
                DesignType,
                AssignmentMethod,

                RandomizeStimuli,
                RandomizationSeed,

                UsesStimuli,
                UsesQuestionnaires,
                UsesPhysiologicalData,

                EegEnabled,
                GsrEnabled,

                TargetSampleSize,
                ExpectedSessionDurationMinutes,
                AllowSessionResume,

                RequireParticipantConsent,
                ConsentText,

                ResearchQuestion,
                Hypothesis,
                PopulationDescription,
                InclusionCriteria,
                ExclusionCriteria,

                MetadataJson,

                CreatedAtUtc,
                UpdatedAtUtc,
                StartedAtUtc,
                CompletedAtUtc,

                IsArchived

            FROM Studies

            WHERE ResearcherId = $researcherId
              AND IsArchived = 0

            ORDER BY UpdatedAtUtc DESC;
            """;

        command.Parameters.AddWithValue(
            "$researcherId",
            researcherId);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            studies.Add(
                ReadStudy(reader));
        }

        return studies;
    }


    // =========================================================
    // GET BY ID
    // =========================================================

    public static async Task<Study?>
        GetByIdAsync(
            string studyId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT
                Id,
                ResearcherId,
                Title,
                Description,
                Status,

                StudyType,
                DesignType,
                AssignmentMethod,

                RandomizeStimuli,
                RandomizationSeed,

                UsesStimuli,
                UsesQuestionnaires,
                UsesPhysiologicalData,

                EegEnabled,
                GsrEnabled,

                TargetSampleSize,
                ExpectedSessionDurationMinutes,
                AllowSessionResume,

                RequireParticipantConsent,
                ConsentText,

                ResearchQuestion,
                Hypothesis,
                PopulationDescription,
                InclusionCriteria,
                ExclusionCriteria,

                MetadataJson,

                CreatedAtUtc,
                UpdatedAtUtc,
                StartedAtUtc,
                CompletedAtUtc,

                IsArchived

            FROM Studies

            WHERE Id = $id

            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$id",
            studyId);

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return ReadStudy(reader);
    }


    // =========================================================
    // UPDATE
    // =========================================================

    public static async Task UpdateAsync(
        Study study)
    {
        study.UpdatedAtUtc =
            DateTime.UtcNow;

        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            UPDATE Studies
            SET
                Title = $title,
                Description = $description,
                Status = $status,

                StudyType = $studyType,
                DesignType = $designType,
                AssignmentMethod = $assignmentMethod,

                RandomizeStimuli = $randomizeStimuli,
                RandomizationSeed = $randomizationSeed,

                UsesStimuli = $usesStimuli,
                UsesQuestionnaires = $usesQuestionnaires,
                UsesPhysiologicalData = $usesPhysiologicalData,

                EegEnabled = $eegEnabled,
                GsrEnabled = $gsrEnabled,

                TargetSampleSize = $targetSampleSize,
                ExpectedSessionDurationMinutes =
                    $expectedSessionDurationMinutes,

                AllowSessionResume = $allowSessionResume,

                RequireParticipantConsent =
                    $requireParticipantConsent,

                ConsentText = $consentText,

                ResearchQuestion = $researchQuestion,
                Hypothesis = $hypothesis,
                PopulationDescription = $populationDescription,
                InclusionCriteria = $inclusionCriteria,
                ExclusionCriteria = $exclusionCriteria,

                MetadataJson = $metadataJson,

                UpdatedAtUtc = $updatedAtUtc,
                StartedAtUtc = $startedAtUtc,
                CompletedAtUtc = $completedAtUtc,

                IsArchived = $isArchived

            WHERE Id = $id;
            """;

        AddParameters(
            command,
            study);

        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // ARCHIVE
    // =========================================================

    public static async Task ArchiveAsync(
        string studyId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            UPDATE Studies
            SET
                IsArchived = 1,
                Status = 'Archived',
                UpdatedAtUtc = $updatedAtUtc
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            studyId);

        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // DELETE PERMANENTLY
    //
    // Foreign-key cascading deletes all child study data.
    // =========================================================

    public static async Task DeleteAsync(
        string studyId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            DELETE FROM Studies
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            studyId);

        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // COUNT ACTIVE
    // =========================================================

    public static async Task<int>
        CountActiveAsync(
            string researcherId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM Studies
            WHERE ResearcherId = $researcherId
              AND IsArchived = 0;
            """;

        command.Parameters.AddWithValue(
            "$researcherId",
            researcherId);

        var result =
            await command.ExecuteScalarAsync();

        return Convert.ToInt32(result);
    }


    // =========================================================
    // PARAMETERS
    // =========================================================

    private static void AddParameters(
        SqliteCommand command,
        Study study)
    {
        command.Parameters.AddWithValue(
            "$id",
            study.Id);

        command.Parameters.AddWithValue(
            "$researcherId",
            study.ResearcherId);

        command.Parameters.AddWithValue(
            "$title",
            study.Title);

        command.Parameters.AddWithValue(
            "$description",
            DbValue(study.Description));

        command.Parameters.AddWithValue(
            "$status",
            study.Status);

        command.Parameters.AddWithValue(
            "$studyType",
            study.StudyType);

        command.Parameters.AddWithValue(
            "$designType",
            study.DesignType);

        command.Parameters.AddWithValue(
            "$assignmentMethod",
            study.AssignmentMethod);

        command.Parameters.AddWithValue(
            "$randomizeStimuli",
            study.RandomizeStimuli ? 1 : 0);

        command.Parameters.AddWithValue(
            "$randomizationSeed",
            DbValue(study.RandomizationSeed));

        command.Parameters.AddWithValue(
            "$usesStimuli",
            study.UsesStimuli ? 1 : 0);

        command.Parameters.AddWithValue(
            "$usesQuestionnaires",
            study.UsesQuestionnaires ? 1 : 0);

        command.Parameters.AddWithValue(
            "$usesPhysiologicalData",
            study.UsesPhysiologicalData ? 1 : 0);

        command.Parameters.AddWithValue(
            "$eegEnabled",
            study.EegEnabled ? 1 : 0);

        command.Parameters.AddWithValue(
            "$gsrEnabled",
            study.GsrEnabled ? 1 : 0);

        command.Parameters.AddWithValue(
            "$targetSampleSize",
            DbValue(study.TargetSampleSize));

        command.Parameters.AddWithValue(
            "$expectedSessionDurationMinutes",
            DbValue(study.ExpectedSessionDurationMinutes));

        command.Parameters.AddWithValue(
            "$allowSessionResume",
            study.AllowSessionResume ? 1 : 0);

        command.Parameters.AddWithValue(
            "$requireParticipantConsent",
            study.RequireParticipantConsent ? 1 : 0);

        command.Parameters.AddWithValue(
            "$consentText",
            DbValue(study.ConsentText));

        command.Parameters.AddWithValue(
            "$researchQuestion",
            DbValue(study.ResearchQuestion));

        command.Parameters.AddWithValue(
            "$hypothesis",
            DbValue(study.Hypothesis));

        command.Parameters.AddWithValue(
            "$populationDescription",
            DbValue(study.PopulationDescription));

        command.Parameters.AddWithValue(
            "$inclusionCriteria",
            DbValue(study.InclusionCriteria));

        command.Parameters.AddWithValue(
            "$exclusionCriteria",
            DbValue(study.ExclusionCriteria));

        command.Parameters.AddWithValue(
            "$metadataJson",
            DbValue(study.MetadataJson));

        command.Parameters.AddWithValue(
            "$createdAtUtc",
            study.CreatedAtUtc.ToString("O"));

        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            study.UpdatedAtUtc.ToString("O"));

        command.Parameters.AddWithValue(
            "$startedAtUtc",
            DbDate(study.StartedAtUtc));

        command.Parameters.AddWithValue(
            "$completedAtUtc",
            DbDate(study.CompletedAtUtc));

        command.Parameters.AddWithValue(
            "$isArchived",
            study.IsArchived ? 1 : 0);
    }


    // =========================================================
    // READER
    // =========================================================

    private static Study ReadStudy(
        SqliteDataReader reader)
    {
        return new Study
        {
            Id =
                reader.GetString(0),

            ResearcherId =
                reader.GetString(1),

            Title =
                reader.GetString(2),

            Description =
                GetNullableString(
                    reader,
                    3),

            Status =
                reader.GetString(4),

            StudyType =
                reader.GetString(5),

            DesignType =
                reader.GetString(6),

            AssignmentMethod =
                reader.GetString(7),

            RandomizeStimuli =
                reader.GetInt32(8) == 1,

            RandomizationSeed =
                GetNullableInt(
                    reader,
                    9),

            UsesStimuli =
                reader.GetInt32(10) == 1,

            UsesQuestionnaires =
                reader.GetInt32(11) == 1,

            UsesPhysiologicalData =
                reader.GetInt32(12) == 1,

            EegEnabled =
                reader.GetInt32(13) == 1,

            GsrEnabled =
                reader.GetInt32(14) == 1,

            TargetSampleSize =
                GetNullableInt(
                    reader,
                    15),

            ExpectedSessionDurationMinutes =
                GetNullableInt(
                    reader,
                    16),

            AllowSessionResume =
                reader.GetInt32(17) == 1,

            RequireParticipantConsent =
                reader.GetInt32(18) == 1,

            ConsentText =
                GetNullableString(
                    reader,
                    19),

            ResearchQuestion =
                GetNullableString(
                    reader,
                    20),

            Hypothesis =
                GetNullableString(
                    reader,
                    21),

            PopulationDescription =
                GetNullableString(
                    reader,
                    22),

            InclusionCriteria =
                GetNullableString(
                    reader,
                    23),

            ExclusionCriteria =
                GetNullableString(
                    reader,
                    24),

            MetadataJson =
                GetNullableString(
                    reader,
                    25),

            CreatedAtUtc =
                DateTime.Parse(
                    reader.GetString(26)),

            UpdatedAtUtc =
                DateTime.Parse(
                    reader.GetString(27)),

            StartedAtUtc =
                GetNullableDate(
                    reader,
                    28),

            CompletedAtUtc =
                GetNullableDate(
                    reader,
                    29),

            IsArchived =
                reader.GetInt32(30) == 1
        };
    }


    // =========================================================
    // HELPERS
    // =========================================================

    private static object DbValue(
        object? value)
    {
        return value
               ?? DBNull.Value;
    }


    private static object DbDate(
        DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString("O")
            : DBNull.Value;
    }


    private static string? GetNullableString(
        SqliteDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : reader.GetString(index);
    }


    private static int? GetNullableInt(
        SqliteDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : reader.GetInt32(index);
    }


    private static DateTime? GetNullableDate(
        SqliteDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : DateTime.Parse(
                reader.GetString(index));
    }
}