using System;
using System.Collections.Generic;
using System.Linq;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

public static class QuestionnaireScoringService
{
    public static double ReverseScore(double rawValue, double minimum, double maximum)
    {
        if (!double.IsFinite(rawValue) || !double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum <= minimum)
            throw new ArgumentException("Reverse-scoring bounds must be finite and maximum must exceed minimum.");
        if (rawValue < minimum || rawValue > maximum)
            throw new ArgumentOutOfRangeException(nameof(rawValue), "Raw score lies outside the reverse-scoring bounds.");
        return minimum + maximum - rawValue;
    }

    public static ScaleScoreResult Score(
        QuestionnaireScale scale,
        IReadOnlyDictionary<string, double?> rawResponses)
    {
        if (scale.Items.Count == 0)
            return new ScaleScoreResult(scale.Id, scale.VariableName, null, 0, scale.MinimumAnsweredItems,
                false, ["The scale has no configured items."]);

        var scored = new List<double>();
        var warnings = new List<string>();
        foreach (var item in scale.Items)
        {
            if (!rawResponses.TryGetValue(item.QuestionId, out var raw) || !raw.HasValue)
                continue;
            var value = raw.Value;
            if (item.IsReverseCoded)
            {
                if (!item.ReverseMinimum.HasValue || !item.ReverseMaximum.HasValue)
                {
                    warnings.Add($"Reverse-scoring bounds are missing for question {item.QuestionId}.");
                    continue;
                }
                try { value = ReverseScore(value, item.ReverseMinimum.Value, item.ReverseMaximum.Value); }
                catch (Exception exception) { warnings.Add(exception.Message); continue; }
            }
            scored.Add(value * item.Weight);
        }

        var minimum = Math.Clamp(scale.MinimumAnsweredItems, 1, scale.Items.Count);
        if (scored.Count < minimum)
            return new ScaleScoreResult(scale.Id, scale.VariableName, null, scored.Count, minimum, false,
                warnings.Append($"At least {minimum} answered items are required.").ToArray());

        var score = scale.ScoringMethod.ToUpperInvariant() switch
        {
            "SUM" => scored.Sum(),
            "MEAN" => scored.Average(),
            _ => throw new NotSupportedException($"Scoring method '{scale.ScoringMethod}' is not supported.")
        };
        return new ScaleScoreResult(scale.Id, scale.VariableName, score, scored.Count, minimum,
            scored.Count == scale.Items.Count, warnings);
    }
}
