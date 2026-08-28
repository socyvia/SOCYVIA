using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

public static class DeterministicRandomizationService
{
    public const string AlgorithmVersion = "SOCYVIA.SplitMix64/1";


    public static int CreateSeed()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);

        return BinaryPrimitives.ReadInt32LittleEndian(bytes) & int.MaxValue;
    }


    public static int SelectIndex(
        int count,
        int seed)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var random = new StableRandom(seed);
        return random.NextInt(count);
    }


    public static List<StimulusPost> OrderStimuli(
        IEnumerable<StimulusPost> stimuli,
        ContentOrderMode orderMode,
        int seed,
        string? customPresentationJson = null)
    {
        var original =
            stimuli
                .OrderBy(stimulus => stimulus.OrderIndex)
                .ThenBy(stimulus => stimulus.CreatedAtUtc)
                .ThenBy(stimulus => stimulus.Id, StringComparer.Ordinal)
                .ToList();

        return orderMode switch
        {
            ContentOrderMode.Random =>
                ShuffleRandomizableSlots(original, seed),

            ContentOrderMode.Chronological =>
                original
                    .OrderBy(stimulus =>
                        stimulus.PublishedAtUtc ?? DateTime.MaxValue)
                    .ThenBy(stimulus => stimulus.OrderIndex)
                    .ToList(),

            ContentOrderMode.ReverseChronological =>
                original
                    .OrderByDescending(stimulus =>
                        stimulus.PublishedAtUtc ?? DateTime.MinValue)
                    .ThenBy(stimulus => stimulus.OrderIndex)
                    .ToList(),

            ContentOrderMode.Popularity =>
                original
                    .OrderByDescending(PopularityScore)
                    .ThenBy(stimulus => stimulus.OrderIndex)
                    .ToList(),

            ContentOrderMode.Custom =>
                ApplyCustomOrder(
                    original,
                    customPresentationJson),

            _ => original
        };
    }


    private static List<StimulusPost> ShuffleRandomizableSlots(
        IReadOnlyList<StimulusPost> original,
        int seed)
    {
        var result = original.ToList();
        var positions =
            original
                .Select((stimulus, index) => new { stimulus, index })
                .Where(item => item.stimulus.AllowRandomization)
                .Select(item => item.index)
                .ToList();

        var randomizable =
            positions
                .Select(index => original[index])
                .ToList();

        var random = new StableRandom(seed);

        for (var index = randomizable.Count - 1;
             index > 0;
             index--)
        {
            var swapIndex = random.NextInt(index + 1);
            (randomizable[index], randomizable[swapIndex]) =
                (randomizable[swapIndex], randomizable[index]);
        }

        for (var index = 0;
             index < positions.Count;
             index++)
        {
            result[positions[index]] = randomizable[index];
        }

        return result;
    }


    private static List<StimulusPost> ApplyCustomOrder(
        IReadOnlyList<StimulusPost> original,
        string? customPresentationJson)
    {
        if (string.IsNullOrWhiteSpace(customPresentationJson))
        {
            return original.ToList();
        }

        try
        {
            using var document =
                JsonDocument.Parse(customPresentationJson);

            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(
                    "stimulusOrder",
                    out var orderElement) ||
                orderElement.ValueKind != JsonValueKind.Array)
            {
                return original.ToList();
            }

            var orderIds =
                orderElement
                    .EnumerateArray()
                    .Where(item =>
                        item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(item =>
                        !string.IsNullOrWhiteSpace(item))
                    .ToList();

            var byId =
                original.ToDictionary(
                    stimulus => stimulus.Id,
                    StringComparer.Ordinal);

            var result =
                new List<StimulusPost>();

            foreach (var stimulusId in orderIds)
            {
                if (stimulusId is not null &&
                    byId.Remove(stimulusId, out var stimulus))
                {
                    result.Add(stimulus);
                }
            }

            result.AddRange(
                original.Where(stimulus =>
                    byId.ContainsKey(stimulus.Id)));

            return result;
        }
        catch (JsonException)
        {
            return original.ToList();
        }
    }


    private static decimal PopularityScore(
        StimulusPost stimulus)
    {
        return (stimulus.ObservedLikes ?? stimulus.OriginalLikes ?? 0) +
               (stimulus.ObservedComments ?? stimulus.OriginalComments ?? 0) +
               (stimulus.ObservedShares ?? stimulus.OriginalShares ?? 0) +
               (stimulus.ObservedSaves ?? stimulus.OriginalSaves ?? 0) +
               (stimulus.OriginalViews ?? 0);
    }


    private sealed class StableRandom
    {
        private ulong _state;

        public StableRandom(int seed)
        {
            _state = unchecked((uint)seed);
        }

        public int NextInt(int maximumExclusive)
        {
            return (int)(NextUInt64() % (uint)maximumExclusive);
        }

        private ulong NextUInt64()
        {
            _state += 0x9E3779B97F4A7C15UL;
            var value = _state;
            value = (value ^ (value >> 30)) *
                    0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) *
                    0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
