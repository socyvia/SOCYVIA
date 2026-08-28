using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace SOCYVIA.Services;

/// <summary>
/// UTF-8 persistence contract for SOCYVIA-owned localized product copy. It is
/// deliberately separate from researcher-authored stimulus/response text.
/// </summary>
public static class ProductTextPersistenceService
{
    public static async Task<string> RoundTripProductCopyAsync(string value)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE ProductCopy (Value TEXT NOT NULL);";
            await create.ExecuteNonQueryAsync();
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO ProductCopy(Value) VALUES ($value);";
            insert.Parameters.AddWithValue("$value", value);
            await insert.ExecuteNonQueryAsync();
        }

        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT Value FROM ProductCopy LIMIT 1;";
        return Convert.ToString(await read.ExecuteScalarAsync()) ?? string.Empty;
    }
}
