using System;
using System.Collections.Generic;
using System.Text.Json;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

/// <summary>
/// Central product boundary for SOCYVIA-owned demonstration records.
/// Demo records may be inspected and previewed, but never managed as researcher data.
/// </summary>
public static class DemoAccessPolicy
{
    public const string ProductLabel = "SOCYVIA Guided Demo";

    public static bool IsDemoStudy(Study? study) =>
        study is not null && HasTrueFlag(study.MetadataJson, "IsDemo");

    public static bool IsReadOnlyStudy(Study? study) =>
        study is not null &&
        (IsDemoStudy(study) || HasTrueFlag(study.MetadataJson, "ReadOnlyGuidedDemo"));

    public static bool CanMutate(Study? study) => !IsReadOnlyStudy(study);

    public static IReadOnlyList<Study> RealStudies(IEnumerable<Study> studies)
    {
        var result = new List<Study>();
        foreach (var study in studies)
        {
            if (!IsDemoStudy(study)) result.Add(study);
        }
        return result;
    }

    private static bool HasTrueFlag(string? metadataJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return false;
        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            return document.RootElement.TryGetProperty(propertyName, out var value) &&
                   value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return metadataJson.Contains(
                $"\"{propertyName}\":true",
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
