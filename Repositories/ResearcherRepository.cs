using System.Threading.Tasks;
using SOCYVIA.Data;
using SOCYVIA.Models;

namespace SOCYVIA.Repositories;

public static class ResearcherRepository
{
    // =========================================================
    // ENSURE RESEARCHER EXISTS
    // =========================================================

    public static async Task EnsureExistsAsync(
        ResearcherProfile researcher)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
                              INSERT INTO Researchers
                              (
                                  Id,
                                  FullName,
                                  CreatedAtUtc,
                                  LastAccessAtUtc
                              )
                              VALUES
                              (
                                  $id,
                                  $fullName,
                                  $createdAtUtc,
                                  $lastAccessAtUtc
                              )
                              ON CONFLICT(Id)
                              DO UPDATE SET
                                  FullName = excluded.FullName,
                                  LastAccessAtUtc = excluded.LastAccessAtUtc;
                              """;

        command.Parameters.AddWithValue(
            "$id",
            researcher.Id);

        command.Parameters.AddWithValue(
            "$fullName",
            researcher.FullName);

        command.Parameters.AddWithValue(
            "$createdAtUtc",
            researcher.CreatedAt.ToString("O"));

        command.Parameters.AddWithValue(
            "$lastAccessAtUtc",
            researcher.LastAccessAt.ToString("O"));

        await command.ExecuteNonQueryAsync();
    }
}