using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

public static class RuntimePresentationService
{
    public static IReadOnlyList<RuntimePostPresentation> CreatePosts(
        ExperimentConfigurationSnapshot snapshot)
    {
        var settings = snapshot.ManipulationSettings ?? new();
        return snapshot.Stimuli
            .OrderBy(stimulus => stimulus.PresentationOrder)
            .Select(stimulus => new RuntimePostPresentation
            {
                Source = stimulus,
                Likes = ResolveMetric(
                    stimulus.OriginalLikes,
                    settings.ShowEngagementMetrics,
                    settings.LikesMode,
                    settings.LikesFixedValue,
                    settings.LikesMultiplier,
                    settings.LikesRandomMin,
                    settings.LikesRandomMax,
                    snapshot.RandomizationSeed,
                    stimulus.StimulusId,
                    "likes"),
                Comments = ResolveMetric(
                    stimulus.OriginalComments,
                    settings.ShowEngagementMetrics,
                    settings.CommentsMode,
                    settings.CommentsFixedValue,
                    settings.CommentsMultiplier,
                    settings.CommentsRandomMin,
                    settings.CommentsRandomMax,
                    snapshot.RandomizationSeed,
                    stimulus.StimulusId,
                    "comments"),
                Shares = ResolveMetric(
                    stimulus.OriginalShares,
                    settings.ShowEngagementMetrics,
                    settings.SharesMode,
                    settings.SharesFixedValue,
                    settings.SharesMultiplier,
                    settings.SharesRandomMin,
                    settings.SharesRandomMax,
                    snapshot.RandomizationSeed,
                    stimulus.StimulusId,
                    "shares"),
                Saves = ResolveMetric(
                    stimulus.OriginalSaves,
                    settings.ShowEngagementMetrics,
                    settings.SavesMode,
                    settings.SavesFixedValue,
                    settings.SavesMultiplier,
                    settings.SavesRandomMin,
                    settings.SavesRandomMax,
                    snapshot.RandomizationSeed,
                    stimulus.StimulusId,
                    "saves"),
                Views = ResolveMetric(
                    stimulus.OriginalViews,
                    settings.ShowEngagementMetrics,
                    settings.ViewsMode,
                    settings.ViewsFixedValue,
                    settings.ViewsMultiplier,
                    settings.ViewsRandomMin,
                    settings.ViewsRandomMax,
                    snapshot.RandomizationSeed,
                    stimulus.StimulusId,
                    "views"),
                ShowAuthor = settings.ShowAuthor,
                ShowTimestamp = settings.ShowTimestamp,
                ShowPlatformIdentity = settings.ShowPlatformIdentity,
                IsRightToLeftContent = ContainsArabic(
                    $"{stimulus.Title} {stimulus.BodyText} {stimulus.AuthorName}")
            })
            .ToList();
    }

    public static long? ResolveMetric(
        long? original,
        bool showEngagementMetrics,
        MetricManipulationMode mode,
        long? fixedValue,
        double? multiplier,
        long? randomMin,
        long? randomMax,
        int seed,
        string stimulusId,
        string metricName)
    {
        if (!showEngagementMetrics || mode == MetricManipulationMode.Hidden)
        {
            return null;
        }

        return mode switch
        {
            MetricManipulationMode.Fixed => Math.Max(0, fixedValue ?? 0),
            MetricManipulationMode.Multiplier => Multiply(
                original ?? 0,
                multiplier ?? 1),
            MetricManipulationMode.RandomRange => DeterministicRange(
                randomMin ?? 0,
                randomMax ?? randomMin ?? 0,
                seed,
                stimulusId,
                metricName),
            _ => original.HasValue ? Math.Max(0, original.Value) : null
        };
    }

    public static string FormatMetric(long value)
    {
        if (value >= 1_000_000)
        {
            return (value / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "M";
        }
        if (value >= 1_000)
        {
            return (value / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + "K";
        }
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static long Multiply(long value, double multiplier)
    {
        if (!double.IsFinite(multiplier) || multiplier < 0)
        {
            multiplier = 0;
        }
        var result = Math.Round(value * multiplier, MidpointRounding.AwayFromZero);
        return result >= long.MaxValue ? long.MaxValue : Math.Max(0, (long)result);
    }

    private static long DeterministicRange(
        long minimum,
        long maximum,
        int seed,
        string stimulusId,
        string metricName)
    {
        minimum = Math.Max(0, minimum);
        maximum = Math.Max(minimum, maximum);
        if (minimum == maximum)
        {
            return minimum;
        }

        var input = Encoding.UTF8.GetBytes(
            $"{seed}|{stimulusId}|{metricName}|SOCYVIA-METRIC-1");
        var hash = SHA256.HashData(input);
        var value = BinaryPrimitives.ReadUInt64LittleEndian(hash);
        var range = checked((ulong)(maximum - minimum) + 1UL);
        return checked(minimum + (long)(value % range));
    }

    private static bool ContainsArabic(string value)
    {
        foreach (var character in value)
        {
            if (character is >= '\u0600' and <= '\u06FF' or
                >= '\u0750' and <= '\u077F' or
                >= '\u08A0' and <= '\u08FF')
            {
                return true;
            }
        }
        return false;
    }
}
