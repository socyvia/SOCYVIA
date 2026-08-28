using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SOCYVIA.Data;
using SOCYVIA.Models;

namespace SOCYVIA.Repositories;

public static class ArchivedStudyRepository
{
    // =========================================================
    // GET ARCHIVED STUDIES
    // =========================================================

    public static async Task<List<Study>>
        GetByResearcherAsync(
            string researcherId)
    {
        var studies =
            new List<Study>();


        await using var connection =
            await DatabaseService
                .OpenConnectionAsync();


        await using var command =
            connection.CreateCommand();


        command.CommandText = """
            SELECT Id
            FROM Studies
            WHERE ResearcherId = $researcherId
              AND IsArchived = 1
            ORDER BY UpdatedAtUtc DESC;
            """;


        command.Parameters.AddWithValue(
            "$researcherId",
            researcherId);


        var ids =
            new List<string>();


        await using (
            var reader =
                await command
                    .ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                ids.Add(
                    reader.GetString(0));
            }
        }


        foreach (var id in ids)
        {
            var study =
                await StudyRepository
                    .GetByIdAsync(
                        id);


            if (study is not null)
            {
                studies.Add(
                    study);
            }
        }


        return studies;
    }


    // =========================================================
    // RESTORE STUDY
    // =========================================================

    public static async Task RestoreAsync(
        string studyId)
    {
        await using var connection =
            await DatabaseService
                .OpenConnectionAsync();


        await using var command =
            connection.CreateCommand();


        command.CommandText = """
            UPDATE Studies
            SET
                IsArchived = 0,
                Status = 'Draft',
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
}