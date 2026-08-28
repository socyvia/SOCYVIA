using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SOCYVIA.Services;

namespace SOCYVIA.Data;

public static class DatabaseService
{
    // =========================================================
    // CONNECTION STRING
    // =========================================================

    private static string ConnectionString
    {
        get
        {
            var builder =
                new SqliteConnectionStringBuilder
                {
                    DataSource =
                        StorageService.DatabaseFile,

                    Mode =
                        SqliteOpenMode.ReadWriteCreate,

                    Cache =
                        SqliteCacheMode.Shared
                };

            return builder.ToString();
        }
    }


    // =========================================================
    // CREATE CONNECTION
    // =========================================================

    public static SqliteConnection CreateConnection()
    {
        StorageService.Initialize();

        return new SqliteConnection(
            ConnectionString);
    }


    // =========================================================
    // OPEN CONNECTION
    // =========================================================

    public static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection =
            CreateConnection();

        await connection.OpenAsync();

        await ConfigureConnectionAsync(
            connection);

        return connection;
    }


    // =========================================================
    // CONNECTION CONFIGURATION
    // =========================================================

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection)
    {
        // Enable foreign-key constraints.
        await ExecutePragmaAsync(
            connection,
            "PRAGMA foreign_keys = ON;");


        // WAL gives us a better base for a research app
        // that may later write events frequently.
        await ExecutePragmaAsync(
            connection,
            "PRAGMA journal_mode = WAL;");


        // Good durability/performance compromise.
        await ExecutePragmaAsync(
            connection,
            "PRAGMA synchronous = NORMAL;");


        // Wait briefly instead of failing immediately
        // if the database is temporarily busy.
        await ExecutePragmaAsync(
            connection,
            "PRAGMA busy_timeout = 5000;");
    }


    // =========================================================
    // PRAGMA HELPER
    // =========================================================

    private static async Task ExecutePragmaAsync(
        SqliteConnection connection,
        string commandText)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText =
            commandText;

        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // SIMPLE HEALTH CHECK
    // =========================================================

    public static async Task<bool> TestConnectionAsync()
    {
        try
        {
            await using var connection =
                await OpenConnectionAsync();

            await using var command =
                connection.CreateCommand();

            command.CommandText =
                "SELECT 1;";

            var result =
                await command.ExecuteScalarAsync();

            return Convert.ToInt32(result) == 1;
        }
        catch
        {
            return false;
        }
    }
}