using System.Text.Json;
using System.Text.Json.Serialization;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

public static class ExperimentSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };


    public static string Serialize(
        ExperimentConfigurationSnapshot snapshot)
    {
        return JsonSerializer.Serialize(snapshot, Options);
    }


    public static ExperimentConfigurationSnapshot Deserialize(
        string json)
    {
        return JsonSerializer.Deserialize<ExperimentConfigurationSnapshot>(
                   json,
                   Options)
               ?? throw new JsonException(
                   "The experiment snapshot is empty.");
    }
}
