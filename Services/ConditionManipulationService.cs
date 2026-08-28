using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

public static class ConditionManipulationService
{
    private static readonly JsonSerializerOptions JsonOptions =
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
        ConditionManipulationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Validate(settings);

        return JsonSerializer.Serialize(
            settings,
            JsonOptions);
    }


    public static ConditionManipulationSettings Deserialize(
        string? manipulationJson)
    {
        if (string.IsNullOrWhiteSpace(manipulationJson))
        {
            return new ConditionManipulationSettings();
        }

        try
        {
            var settings =
                JsonSerializer.Deserialize<ConditionManipulationSettings>(
                    manipulationJson,
                    JsonOptions)
                ?? new ConditionManipulationSettings();

            Validate(settings);

            return settings;
        }
        catch (JsonException)
        {
            return new ConditionManipulationSettings
            {
                CustomPresentationJson = manipulationJson
            };
        }
        catch (ArgumentException)
        {
            return new ConditionManipulationSettings
            {
                CustomPresentationJson = manipulationJson
            };
        }
    }


    public static void Validate(
        ConditionManipulationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        ValidateMetric(
            settings.LikesMode,
            settings.LikesFixedValue,
            settings.LikesMultiplier,
            settings.LikesRandomMin,
            settings.LikesRandomMax,
            "likes");

        ValidateMetric(
            settings.CommentsMode,
            settings.CommentsFixedValue,
            settings.CommentsMultiplier,
            settings.CommentsRandomMin,
            settings.CommentsRandomMax,
            "comments");

        ValidateMetric(
            settings.SharesMode,
            settings.SharesFixedValue,
            settings.SharesMultiplier,
            settings.SharesRandomMin,
            settings.SharesRandomMax,
            "shares");

        ValidateMetric(
            settings.SavesMode,
            settings.SavesFixedValue,
            settings.SavesMultiplier,
            settings.SavesRandomMin,
            settings.SavesRandomMax,
            "saves");

        ValidateMetric(
            settings.ViewsMode,
            settings.ViewsFixedValue,
            settings.ViewsMultiplier,
            settings.ViewsRandomMin,
            settings.ViewsRandomMax,
            "views");

        if (!string.IsNullOrWhiteSpace(
                settings.CustomPresentationJson))
        {
            try
            {
                using var _ =
                    JsonDocument.Parse(
                        settings.CustomPresentationJson);
            }
            catch (JsonException exception)
            {
                throw new ArgumentException(
                    "Custom presentation JSON is invalid.",
                    nameof(settings),
                    exception);
            }
        }
    }


    private static void ValidateMetric(
        MetricManipulationMode mode,
        long? fixedValue,
        double? multiplier,
        long? randomMin,
        long? randomMax,
        string metricName)
    {
        if (fixedValue < 0 ||
            randomMin < 0 ||
            randomMax < 0 ||
            multiplier < 0)
        {
            throw new ArgumentException(
                $"Manipulated {metricName} values cannot be negative.");
        }

        if (mode == MetricManipulationMode.Fixed &&
            fixedValue is null)
        {
            throw new ArgumentException(
                $"A fixed value is required for {metricName}.");
        }

        if (mode == MetricManipulationMode.Multiplier &&
            multiplier is null)
        {
            throw new ArgumentException(
                $"A multiplier is required for {metricName}.");
        }

        if (mode == MetricManipulationMode.RandomRange &&
            (randomMin is null ||
             randomMax is null ||
             randomMin > randomMax))
        {
            throw new ArgumentException(
                $"A valid random range is required for {metricName}.");
        }
    }
}
