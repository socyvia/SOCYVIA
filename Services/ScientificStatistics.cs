using System;
using System.Collections.Generic;
using System.Linq;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

/// <summary>
/// Transparent managed implementations used by the SOCYVIA scientific engine.
/// They are deterministic and intentionally limited to the documented methods.
/// </summary>
public static class ScientificStatistics
{
    private const double Epsilon = 1e-14;

    public static NumericDescriptiveResult Describe(
        IEnumerable<double?> source,
        double confidenceLevel = 0.95)
    {
        var all = source.ToArray();
        var values = all.Where(value => value.HasValue && double.IsFinite(value.Value))
            .Select(value => value!.Value).Order().ToArray();
        if (values.Length == 0)
            return new NumericDescriptiveResult { Missing = all.Length };

        var mean = values.Average();
        var variance = values.Length > 1
            ? values.Sum(value => Square(value - mean)) / (values.Length - 1)
            : 0;
        var sd = Math.Sqrt(variance);
        ConfidenceInterval? interval = null;
        if (values.Length > 1 && sd > 0)
        {
            var alpha = 1 - confidenceLevel;
            var critical = InverseStudentT(1 - alpha / 2, values.Length - 1);
            var margin = critical * sd / Math.Sqrt(values.Length);
            interval = new ConfidenceInterval(mean - margin, mean + margin,
                confidenceLevel, "Student t interval for the arithmetic mean");
        }

        var q1 = Quantile(values, 0.25);
        var q3 = Quantile(values, 0.75);
        return new NumericDescriptiveResult
        {
            N = values.Length,
            Missing = all.Length - values.Length,
            Mean = mean,
            StandardDeviation = sd,
            Median = Quantile(values, 0.5),
            Minimum = values[0],
            Maximum = values[^1],
            Q1 = q1,
            Q3 = q3,
            Iqr = q3 - q1,
            MeanConfidenceInterval = interval
        };
    }

    public static IReadOnlyList<CategoryFrequency> Frequencies(IEnumerable<string?> source)
    {
        var values = source.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray();
        if (values.Length == 0) return [];
        return values.GroupBy(value => value, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new CategoryFrequency(group.Key, group.Count(),
                100.0 * group.Count() / values.Length)).ToArray();
    }

    public static StatisticalResult IndependentTTest(
        IReadOnlyList<double> first,
        IReadOnlyList<double> second,
        double confidenceLevel = 0.95)
    {
        var validation = ValidateTwoSamples(first, second, 2);
        if (validation is not null)
            return Failure("INDEPENDENT_T", AnalysisStatuses.InsufficientData, validation);
        var a = Moments(first); var b = Moments(second);
        var df = first.Count + second.Count - 2;
        var pooledVariance = ((first.Count - 1) * a.Variance + (second.Count - 1) * b.Variance) / df;
        if (pooledVariance <= Epsilon) return Failure("INDEPENDENT_T", AnalysisStatuses.InsufficientData,
            "Both groups have zero variance.");
        var standardError = Math.Sqrt(pooledVariance * (1.0 / first.Count + 1.0 / second.Count));
        var difference = a.Mean - b.Mean;
        var statistic = difference / standardError;
        var p = TwoSidedStudentTP(statistic, df);
        var pooledSd = Math.Sqrt(pooledVariance);
        var d = difference / pooledSd;
        var correction = 1 - 3.0 / (4 * df - 1);
        var critical = InverseStudentT(1 - (1 - confidenceLevel) / 2, df);
        return new StatisticalResult
        {
            Method = "INDEPENDENT_T",
            N = first.Count + second.Count,
            GroupNs = Groups(first.Count, second.Count),
            Estimate = difference,
            Statistic = statistic,
            DegreesOfFreedom = df,
            PValue = p,
            ConfidenceInterval = new ConfidenceInterval(difference - critical * standardError,
                difference + critical * standardError, confidenceLevel, "Pooled-variance t interval"),
            EffectSize = new EffectSizeEstimate(d, "COHEN_D_POOLED",
                "Mean difference divided by the pooled within-group standard deviation"),
            ResultData = new Dictionary<string, double>
            {
                ["MeanGroup1"] = a.Mean, ["MeanGroup2"] = b.Mean,
                ["HedgesG"] = d * correction, ["PooledStandardDeviation"] = pooledSd
            },
            Diagnostics = VarianceDiagnostics(a.Variance, b.Variance),
            CanonicalSummary = "Independent-samples pooled-variance t-test; report the mean difference, confidence interval, p value, and Cohen's d."
        };
    }

    public static StatisticalResult WelchTTest(
        IReadOnlyList<double> first,
        IReadOnlyList<double> second,
        double confidenceLevel = 0.95)
    {
        var validation = ValidateTwoSamples(first, second, 2);
        if (validation is not null)
            return Failure("WELCH_T", AnalysisStatuses.InsufficientData, validation);
        var a = Moments(first); var b = Moments(second);
        var va = a.Variance / first.Count; var vb = b.Variance / second.Count;
        var standardErrorSquared = va + vb;
        if (standardErrorSquared <= Epsilon)
            return Failure("WELCH_T", AnalysisStatuses.InsufficientData, "Both groups have zero variance.");
        var df = Square(standardErrorSquared) /
                 (Square(va) / (first.Count - 1) + Square(vb) / (second.Count - 1));
        var difference = a.Mean - b.Mean;
        var statistic = difference / Math.Sqrt(standardErrorSquared);
        var critical = InverseStudentT(1 - (1 - confidenceLevel) / 2, df);
        var pooledSd = Math.Sqrt(((first.Count - 1) * a.Variance + (second.Count - 1) * b.Variance) /
                                 (first.Count + second.Count - 2));
        var d = pooledSd <= Epsilon ? double.NaN : difference / pooledSd;
        return new StatisticalResult
        {
            Method = "WELCH_T", N = first.Count + second.Count,
            GroupNs = Groups(first.Count, second.Count), Estimate = difference,
            Statistic = statistic, DegreesOfFreedom = df, PValue = TwoSidedStudentTP(statistic, df),
            ConfidenceInterval = new ConfidenceInterval(difference - critical * Math.Sqrt(standardErrorSquared),
                difference + critical * Math.Sqrt(standardErrorSquared), confidenceLevel,
                "Welch-Satterthwaite t interval"),
            EffectSize = double.IsFinite(d) ? new EffectSizeEstimate(d, "COHEN_D_POOLED",
                "Mean difference divided by pooled within-group standard deviation; inferential test uses Welch variance correction") : null,
            Diagnostics = VarianceDiagnostics(a.Variance, b.Variance),
            CanonicalSummary = "Welch independent-samples t-test allowing unequal variances."
        };
    }

    public static StatisticalResult PairedTTest(
        IReadOnlyList<double> first,
        IReadOnlyList<double> second,
        double confidenceLevel = 0.95)
    {
        if (first.Count != second.Count || first.Count < 2)
            return Failure("PAIRED_T", AnalysisStatuses.InvalidConfiguration,
                "Paired samples must have equal length and at least two pairs.");
        var differences = first.Zip(second, (a, b) => a - b).ToArray();
        var moments = Moments(differences);
        if (moments.Variance <= Epsilon)
            return Failure("PAIRED_T", AnalysisStatuses.InsufficientData, "Paired differences have zero variance.");
        var standardError = Math.Sqrt(moments.Variance / differences.Length);
        var statistic = moments.Mean / standardError;
        var df = differences.Length - 1;
        var critical = InverseStudentT(1 - (1 - confidenceLevel) / 2, df);
        return new StatisticalResult
        {
            Method = "PAIRED_T", N = differences.Length, Estimate = moments.Mean,
            Statistic = statistic, DegreesOfFreedom = df, PValue = TwoSidedStudentTP(statistic, df),
            ConfidenceInterval = new ConfidenceInterval(moments.Mean - critical * standardError,
                moments.Mean + critical * standardError, confidenceLevel, "Paired t interval"),
            EffectSize = new EffectSizeEstimate(moments.Mean / Math.Sqrt(moments.Variance),
                "COHEN_DZ", "Mean paired difference divided by the standard deviation of paired differences"),
            CanonicalSummary = "Paired-samples t-test on within-pair differences."
        };
    }

    public static StatisticalResult MannWhitneyU(IReadOnlyList<double> first, IReadOnlyList<double> second)
    {
        var validation = ValidateTwoSamples(first, second, 1);
        if (validation is not null)
            return Failure("MANN_WHITNEY_U", AnalysisStatuses.InsufficientData, validation);
        var combined = first.Select(value => (Value: value, Group: 0))
            .Concat(second.Select(value => (Value: value, Group: 1))).ToArray();
        var ranks = Rank(combined.Select(item => item.Value).ToArray());
        var rankA = combined.Select((item, index) => (item, index)).Where(x => x.item.Group == 0)
            .Sum(x => ranks[x.index]);
        var u1 = rankA - first.Count * (first.Count + 1) / 2.0;
        var u2 = first.Count * second.Count - u1;
        var u = Math.Min(u1, u2);
        var n = combined.Length;
        var tieSum = TieCorrectionSum(combined.Select(item => item.Value));
        var variance = first.Count * second.Count / 12.0 *
                       ((n + 1) - tieSum / (n * (n - 1.0)));
        if (variance <= Epsilon)
            return Failure("MANN_WHITNEY_U", AnalysisStatuses.InsufficientData, "All ranks are tied.");
        var z = (Math.Abs(u1 - first.Count * second.Count / 2.0) - 0.5) / Math.Sqrt(variance);
        z = Math.Max(0, z);
        var rankBiserial = 1 - 2 * u / (first.Count * second.Count);
        return new StatisticalResult
        {
            Method = "MANN_WHITNEY_U", N = n, GroupNs = Groups(first.Count, second.Count),
            Statistic = u, PValue = 2 * (1 - NormalCdf(z)),
            EffectSize = new EffectSizeEstimate(rankBiserial, "RANK_BISERIAL",
                "One minus twice the smaller U divided by the product of group sizes"),
            ResultData = new Dictionary<string, double> { ["U1"] = u1, ["U2"] = u2, ["NormalApproximationZ"] = z },
            Diagnostics = [new AnalysisDiagnostic { Code = "ASYMPTOTIC_P", Severity = "INFO", Message = "Two-sided p value uses a tie-corrected normal approximation with continuity correction." }],
            CanonicalSummary = "Mann-Whitney U comparison with tied-rank correction."
        };
    }

    public static StatisticalResult WilcoxonSignedRank(IReadOnlyList<double> first, IReadOnlyList<double> second)
    {
        if (first.Count != second.Count)
            return Failure("WILCOXON_SIGNED_RANK", AnalysisStatuses.InvalidConfiguration,
                "Paired samples must have equal length.");
        var differences = first.Zip(second, (a, b) => a - b).Where(value => Math.Abs(value) > Epsilon).ToArray();
        if (differences.Length == 0)
            return Failure("WILCOXON_SIGNED_RANK", AnalysisStatuses.InsufficientData, "All paired differences are zero.");
        var ranks = Rank(differences.Select(Math.Abs).ToArray());
        var positive = differences.Select((value, index) => value > 0 ? ranks[index] : 0).Sum();
        var negative = differences.Select((value, index) => value < 0 ? ranks[index] : 0).Sum();
        var statistic = Math.Min(positive, negative);
        var n = differences.Length;
        var mean = n * (n + 1) / 4.0;
        var tieSum = TieCorrectionSum(differences.Select(Math.Abs));
        var variance = n * (n + 1) * (2 * n + 1) / 24.0 - tieSum / 48.0;
        if (variance <= Epsilon)
            return Failure("WILCOXON_SIGNED_RANK", AnalysisStatuses.InsufficientData, "Rank variance is zero.");
        var z = (Math.Abs(positive - mean) - 0.5) / Math.Sqrt(variance);
        z = Math.Max(0, z);
        return new StatisticalResult
        {
            Method = "WILCOXON_SIGNED_RANK", N = n, Statistic = statistic,
            PValue = 2 * (1 - NormalCdf(z)),
            EffectSize = new EffectSizeEstimate(z / Math.Sqrt(n), "R_FROM_Z",
                "Absolute normal-approximation z divided by square root of non-zero pairs"),
            ResultData = new Dictionary<string, double> { ["WPlus"] = positive, ["WMinus"] = negative, ["NormalApproximationZ"] = z },
            Diagnostics = [new AnalysisDiagnostic { Code = "ZERO_DIFFERENCES", Message = $"{first.Count - n} zero differences were excluded." }],
            CanonicalSummary = "Wilcoxon signed-rank comparison of non-zero paired differences."
        };
    }

    public static StatisticalResult OneWayAnova(IReadOnlyList<IReadOnlyList<double>> groups)
    {
        if (groups.Count < 2 || groups.Any(group => group.Count < 2))
            return Failure("ONE_WAY_ANOVA", AnalysisStatuses.InsufficientData,
                "ANOVA requires at least two groups with two observations each.");
        var all = groups.SelectMany(group => group).ToArray();
        var grandMean = all.Average();
        var between = groups.Sum(group => group.Count * Square(group.Average() - grandMean));
        var within = groups.Sum(group => group.Sum(value => Square(value - group.Average())));
        var dfBetween = groups.Count - 1;
        var dfWithin = all.Length - groups.Count;
        if (within <= Epsilon)
            return Failure("ONE_WAY_ANOVA", AnalysisStatuses.InsufficientData, "Within-group variance is zero.");
        var statistic = (between / dfBetween) / (within / dfWithin);
        var etaSquared = between / (between + within);
        return new StatisticalResult
        {
            Method = "ONE_WAY_ANOVA", N = all.Length,
            GroupNs = groups.Select((group, index) => (index, group.Count)).ToDictionary(x => $"Group{x.index + 1}", x => x.Count),
            Statistic = statistic, DegreesOfFreedom = dfBetween, SecondaryDegreesOfFreedom = dfWithin,
            PValue = 1 - FCdf(statistic, dfBetween, dfWithin),
            EffectSize = new EffectSizeEstimate(etaSquared, "ETA_SQUARED",
                "Between-group sum of squares divided by total sum of squares"),
            ResultData = new Dictionary<string, double> { ["BetweenSumSquares"] = between, ["WithinSumSquares"] = within },
            Diagnostics = [new AnalysisDiagnostic { Code = "GROUP_VARIANCES", Message = "Inspect group distributions and variance structure; ANOVA is not selected by a binary normality gate." }],
            CanonicalSummary = "One-way fixed-effects ANOVA with eta-squared."
        };
    }

    public static StatisticalResult KruskalWallis(IReadOnlyList<IReadOnlyList<double>> groups)
    {
        if (groups.Count < 2 || groups.Any(group => group.Count == 0))
            return Failure("KRUSKAL_WALLIS", AnalysisStatuses.InsufficientData,
                "Kruskal-Wallis requires at least two non-empty groups.");
        var combined = groups.SelectMany((group, groupIndex) => group.Select(value => (value, groupIndex))).ToArray();
        var ranks = Rank(combined.Select(item => item.value).ToArray());
        var n = combined.Length;
        var rankSums = Enumerable.Range(0, groups.Count).Select(groupIndex =>
            combined.Select((item, index) => (item, index)).Where(x => x.item.groupIndex == groupIndex)
                .Sum(x => ranks[x.index])).ToArray();
        var h = 12.0 / (n * (n + 1)) * rankSums.Select((sum, index) => Square(sum) / groups[index].Count).Sum() - 3 * (n + 1);
        var correction = 1 - TieCorrectionSum(combined.Select(item => item.value)) /
            (n * (n * n - 1.0));
        if (correction <= Epsilon)
            return Failure("KRUSKAL_WALLIS", AnalysisStatuses.InsufficientData, "All values are tied.");
        h /= correction;
        var epsilonSquared = Math.Max(0, (h - groups.Count + 1) / (n - groups.Count));
        return new StatisticalResult
        {
            Method = "KRUSKAL_WALLIS", N = n, Statistic = h, DegreesOfFreedom = groups.Count - 1,
            PValue = 1 - ChiSquareCdf(h, groups.Count - 1),
            EffectSize = new EffectSizeEstimate(epsilonSquared, "EPSILON_SQUARED",
                "(H - k + 1) divided by (N - k), truncated at zero"),
            CanonicalSummary = "Kruskal-Wallis rank comparison with tie correction."
        };
    }

    public static StatisticalResult ChiSquare(long[,] observed)
    {
        var rows = observed.GetLength(0); var columns = observed.GetLength(1);
        if (rows < 2 || columns < 2)
            return Failure("CHI_SQUARE", AnalysisStatuses.InvalidConfiguration, "A contingency table requires at least 2 rows and 2 columns.");
        var rowTotals = new double[rows]; var columnTotals = new double[columns]; double total = 0;
        for (var row = 0; row < rows; row++) for (var column = 0; column < columns; column++)
        {
            if (observed[row, column] < 0) return Failure("CHI_SQUARE", AnalysisStatuses.InvalidConfiguration, "Counts cannot be negative.");
            rowTotals[row] += observed[row, column]; columnTotals[column] += observed[row, column]; total += observed[row, column];
        }
        if (total <= 0 || rowTotals.Any(value => value == 0) || columnTotals.Any(value => value == 0))
            return Failure("CHI_SQUARE", AnalysisStatuses.InsufficientData, "Marginal totals must be positive.");
        double statistic = 0; var smallExpected = 0;
        for (var row = 0; row < rows; row++) for (var column = 0; column < columns; column++)
        {
            var expected = rowTotals[row] * columnTotals[column] / total;
            if (expected < 5) smallExpected++;
            statistic += Square(observed[row, column] - expected) / expected;
        }
        var df = (rows - 1) * (columns - 1);
        var cramersV = Math.Sqrt(statistic / (total * Math.Min(rows - 1, columns - 1)));
        return new StatisticalResult
        {
            Method = "CHI_SQUARE", N = (int)total, Statistic = statistic, DegreesOfFreedom = df,
            PValue = 1 - ChiSquareCdf(statistic, df),
            EffectSize = new EffectSizeEstimate(cramersV, "CRAMERS_V",
                "Square root of chi-square divided by N times the smaller table dimension minus one"),
            Diagnostics = [new AnalysisDiagnostic { Code = "EXPECTED_COUNTS", Severity = smallExpected > 0 ? "WARNING" : "INFO", IsSatisfied = smallExpected == 0, Message = $"{smallExpected} cells have expected count below 5." }],
            CanonicalSummary = "Pearson chi-square association test with Cramer's V."
        };
    }

    public static StatisticalResult FisherExact2X2(long a, long b, long c, long d)
    {
        if (a < 0 || b < 0 || c < 0 || d < 0)
            return Failure("FISHER_EXACT", AnalysisStatuses.InvalidConfiguration, "Counts cannot be negative.");
        var n = a + b + c + d;
        if (n == 0) return Failure("FISHER_EXACT", AnalysisStatuses.InsufficientData, "The table is empty.");
        var row1 = a + b; var column1 = a + c; var lower = Math.Max(0, row1 - (n - column1)); var upper = Math.Min(row1, column1);
        var observedProbability = HypergeometricProbability(a, row1, column1, n);
        double p = 0;
        for (var value = lower; value <= upper; value++)
        {
            var probability = HypergeometricProbability(value, row1, column1, n);
            if (probability <= observedProbability + 1e-12) p += probability;
        }
        var oddsRatio = b * c == 0 ? (a * d == 0 ? double.NaN : double.PositiveInfinity) : (double)a * d / (b * c);
        return new StatisticalResult
        {
            Method = "FISHER_EXACT", N = (int)n, PValue = Math.Min(1, p),
            Estimate = double.IsFinite(oddsRatio) ? oddsRatio : null,
            CanonicalSummary = "Two-sided Fisher exact test using the probability-ordering definition."
        };
    }

    public static StatisticalResult PearsonCorrelation(IReadOnlyList<double> x, IReadOnlyList<double> y, double confidenceLevel = 0.95)
    {
        if (x.Count != y.Count || x.Count < 3)
            return Failure("PEARSON_R", AnalysisStatuses.InsufficientData, "Correlation requires at least three paired observations.");
        var mx = x.Average(); var my = y.Average();
        var cross = x.Zip(y, (a, b) => (a - mx) * (b - my)).Sum();
        var sx = Math.Sqrt(x.Sum(value => Square(value - mx))); var sy = Math.Sqrt(y.Sum(value => Square(value - my)));
        if (sx <= Epsilon || sy <= Epsilon)
            return Failure("PEARSON_R", AnalysisStatuses.InsufficientData, "Correlation is undefined for a constant variable.");
        var r = Math.Clamp(cross / (sx * sy), -1, 1);
        var df = x.Count - 2;
        var statistic = Math.Abs(r) >= 1 ? double.PositiveInfinity : r * Math.Sqrt(df / (1 - r * r));
        ConfidenceInterval? interval = null;
        if (x.Count > 3 && Math.Abs(r) < 1)
        {
            var z = Atanh(r); var critical = InverseNormalCdf(1 - (1 - confidenceLevel) / 2);
            var margin = critical / Math.Sqrt(x.Count - 3);
            interval = new ConfidenceInterval(Math.Tanh(z - margin), Math.Tanh(z + margin), confidenceLevel, "Fisher z transformation");
        }
        return new StatisticalResult
        {
            Method = "PEARSON_R", N = x.Count, Estimate = r, Statistic = statistic,
            DegreesOfFreedom = df, PValue = double.IsInfinity(statistic) ? 0 : TwoSidedStudentTP(statistic, df),
            EffectSize = new EffectSizeEstimate(r, "PEARSON_R", "Product-moment correlation coefficient"),
            ConfidenceInterval = interval,
            CanonicalSummary = "Pearson product-moment correlation with a Fisher-z confidence interval."
        };
    }

    public static StatisticalResult SpearmanCorrelation(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        if (x.Count != y.Count || x.Count < 3)
            return Failure("SPEARMAN_RHO", AnalysisStatuses.InsufficientData, "Correlation requires at least three paired observations.");
        var rankedX = Rank(x); var rankedY = Rank(y);
        var result = PearsonCorrelation(rankedX, rankedY);
        return new StatisticalResult
        {
            Method = "SPEARMAN_RHO", Status = result.Status, N = result.N,
            Estimate = result.Estimate, Statistic = result.Statistic, DegreesOfFreedom = result.DegreesOfFreedom,
            PValue = result.PValue,
            EffectSize = result.Estimate.HasValue ? new EffectSizeEstimate(result.Estimate.Value, "SPEARMAN_RHO", "Pearson correlation of average ranks") : null,
            Diagnostics = [new AnalysisDiagnostic { Code = "ASYMPTOTIC_P", Message = "The p value uses the t approximation applied to ranked data." }],
            CanonicalSummary = "Spearman rank correlation using average ranks for ties."
        };
    }

    public static IReadOnlyList<double> AdjustPValues(IReadOnlyList<double> pValues, string method)
    {
        if (pValues.Any(value => value is < 0 or > 1 || !double.IsFinite(value)))
            throw new ArgumentOutOfRangeException(nameof(pValues), "P values must be finite and between zero and one.");
        if (method.Equals("BONFERRONI", StringComparison.OrdinalIgnoreCase))
            return pValues.Select(value => Math.Min(1, value * pValues.Count)).ToArray();
        if (!method.Equals("HOLM", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Multiple-comparison method '{method}' is not supported.");
        var ordered = pValues.Select((value, index) => (value, index)).OrderBy(item => item.value).ToArray();
        var output = new double[pValues.Count]; double previous = 0;
        for (var rank = 0; rank < ordered.Length; rank++)
        {
            var adjusted = Math.Min(1, ordered[rank].value * (ordered.Length - rank));
            previous = Math.Max(previous, adjusted);
            output[ordered[rank].index] = previous;
        }
        return output;
    }

    internal static double StudentTCdf(double value, double degreesOfFreedom)
    {
        if (degreesOfFreedom <= 0) throw new ArgumentOutOfRangeException(nameof(degreesOfFreedom));
        if (value == 0) return 0.5;
        if (double.IsPositiveInfinity(value)) return 1;
        if (double.IsNegativeInfinity(value)) return 0;
        var x = degreesOfFreedom / (degreesOfFreedom + value * value);
        var tail = 0.5 * RegularizedBeta(x, degreesOfFreedom / 2, 0.5);
        return value > 0 ? 1 - tail : tail;
    }

    internal static double FCdf(double value, double df1, double df2) =>
        value <= 0 ? 0 : RegularizedBeta(df1 * value / (df1 * value + df2), df1 / 2, df2 / 2);

    internal static double ChiSquareCdf(double value, double degreesOfFreedom) =>
        value <= 0 ? 0 : RegularizedGammaP(degreesOfFreedom / 2, value / 2);

    private static StatisticalResult Failure(string method, string status, string warning) => new()
    {
        Method = method, Status = status, Warnings = [warning], CanonicalSummary = warning
    };

    private static string? ValidateTwoSamples(IReadOnlyList<double> first, IReadOnlyList<double> second, int minimum)
    {
        if (first.Count < minimum || second.Count < minimum)
            return $"Both samples require at least {minimum} observations.";
        if (first.Concat(second).Any(value => !double.IsFinite(value)))
            return "Samples must contain only finite values.";
        return null;
    }

    private static (double Mean, double Variance) Moments(IReadOnlyList<double> values)
    {
        var mean = values.Average();
        var variance = values.Count > 1 ? values.Sum(value => Square(value - mean)) / (values.Count - 1) : 0;
        return (mean, variance);
    }

    private static IReadOnlyDictionary<string, int> Groups(int first, int second) =>
        new Dictionary<string, int> { ["Group1"] = first, ["Group2"] = second };

    private static IReadOnlyList<AnalysisDiagnostic> VarianceDiagnostics(double first, double second)
    {
        var ratio = Math.Min(first, second) <= Epsilon ? double.PositiveInfinity : Math.Max(first, second) / Math.Min(first, second);
        return [new AnalysisDiagnostic
        {
            Code = "VARIANCE_RATIO", Severity = ratio > 4 ? "WARNING" : "INFO",
            IsSatisfied = ratio <= 4, Message = "Variance ratio is descriptive, not an automatic test-selection gate.",
            Values = new Dictionary<string, double> { ["Ratio"] = ratio }
        }];
    }

    private static double[] Rank(IReadOnlyList<double> values)
    {
        var ordered = values.Select((value, index) => (value, index)).OrderBy(item => item.value).ToArray();
        var ranks = new double[values.Count]; var cursor = 0;
        while (cursor < ordered.Length)
        {
            var end = cursor + 1;
            while (end < ordered.Length && Math.Abs(ordered[end].value - ordered[cursor].value) <= Epsilon) end++;
            var rank = (cursor + 1 + end) / 2.0;
            for (var index = cursor; index < end; index++) ranks[ordered[index].index] = rank;
            cursor = end;
        }
        return ranks;
    }

    private static double TieCorrectionSum(IEnumerable<double> values) =>
        values.GroupBy(value => value).Where(group => group.Count() > 1)
            .Sum(group => (double)group.Count() * group.Count() * group.Count() - group.Count());

    private static double Quantile(IReadOnlyList<double> sorted, double probability)
    {
        if (sorted.Count == 1) return sorted[0];
        var position = (sorted.Count - 1) * probability;
        var lower = (int)Math.Floor(position); var upper = (int)Math.Ceiling(position);
        return lower == upper ? sorted[lower] : sorted[lower] + (position - lower) * (sorted[upper] - sorted[lower]);
    }

    private static double TwoSidedStudentTP(double statistic, double df) =>
        Math.Clamp(2 * (1 - StudentTCdf(Math.Abs(statistic), df)), 0, 1);

    private static double InverseStudentT(double probability, double df)
    {
        if (probability <= 0 || probability >= 1) throw new ArgumentOutOfRangeException(nameof(probability));
        var lower = -50.0; var upper = 50.0;
        for (var iteration = 0; iteration < 160; iteration++)
        {
            var middle = (lower + upper) / 2;
            if (StudentTCdf(middle, df) < probability) lower = middle; else upper = middle;
        }
        return (lower + upper) / 2;
    }

    private static double NormalCdf(double value) => 0.5 * (1 + Erf(value / Math.Sqrt(2)));

    private static double InverseNormalCdf(double probability)
    {
        var lower = -9.0; var upper = 9.0;
        for (var iteration = 0; iteration < 120; iteration++)
        {
            var middle = (lower + upper) / 2;
            if (NormalCdf(middle) < probability) lower = middle; else upper = middle;
        }
        return (lower + upper) / 2;
    }

    // Abramowitz-Stegun 7.1.26; maximum error is adequate for UI and inference p-value precision.
    private static double Erf(double value)
    {
        var sign = Math.Sign(value); value = Math.Abs(value);
        var t = 1 / (1 + 0.3275911 * value);
        var polynomial = (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t;
        return sign * (1 - polynomial * Math.Exp(-value * value));
    }

    private static double Atanh(double value) => 0.5 * Math.Log((1 + value) / (1 - value));
    private static double Square(double value) => value * value;

    private static double HypergeometricProbability(long cell, long row1, long column1, long total) =>
        Math.Exp(LogCombination(column1, cell) + LogCombination(total - column1, row1 - cell) - LogCombination(total, row1));

    private static double LogCombination(long n, long k) => k < 0 || k > n
        ? double.NegativeInfinity
        : LogGamma(n + 1) - LogGamma(k + 1) - LogGamma(n - k + 1);

    private static double RegularizedBeta(double x, double a, double b)
    {
        if (x <= 0) return 0; if (x >= 1) return 1;
        var front = Math.Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b) + a * Math.Log(x) + b * Math.Log(1 - x));
        return x < (a + 1) / (a + b + 2)
            ? front * BetaContinuedFraction(x, a, b) / a
            : 1 - front * BetaContinuedFraction(1 - x, b, a) / b;
    }

    private static double BetaContinuedFraction(double x, double a, double b)
    {
        const int maxIterations = 300; const double tiny = 1e-300;
        var qab = a + b; var qap = a + 1; var qam = a - 1;
        var c = 1.0; var d = 1 - qab * x / qap; if (Math.Abs(d) < tiny) d = tiny;
        d = 1 / d; var result = d;
        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            var m2 = 2 * iteration;
            var aa = iteration * (b - iteration) * x / ((qam + m2) * (a + m2));
            d = 1 + aa * d; if (Math.Abs(d) < tiny) d = tiny; c = 1 + aa / c; if (Math.Abs(c) < tiny) c = tiny;
            d = 1 / d; result *= d * c;
            aa = -(a + iteration) * (qab + iteration) * x / ((a + m2) * (qap + m2));
            d = 1 + aa * d; if (Math.Abs(d) < tiny) d = tiny; c = 1 + aa / c; if (Math.Abs(c) < tiny) c = tiny;
            d = 1 / d; var delta = d * c; result *= delta;
            if (Math.Abs(delta - 1) < 3e-14) break;
        }
        return result;
    }

    private static double RegularizedGammaP(double a, double x)
    {
        if (x <= 0) return 0;
        if (x < a + 1)
        {
            var sum = 1 / a; var term = sum;
            for (var n = 1; n < 300; n++)
            {
                term *= x / (a + n); sum += term;
                if (Math.Abs(term) < Math.Abs(sum) * 1e-15) break;
            }
            return sum * Math.Exp(-x + a * Math.Log(x) - LogGamma(a));
        }
        var b = x + 1 - a; var c = 1e300; var d = 1 / b; var h = d;
        for (var i = 1; i < 300; i++)
        {
            var an = -i * (i - a); b += 2; d = an * d + b; if (Math.Abs(d) < 1e-300) d = 1e-300;
            c = b + an / c; if (Math.Abs(c) < 1e-300) c = 1e-300; d = 1 / d;
            var delta = d * c; h *= delta; if (Math.Abs(delta - 1) < 1e-15) break;
        }
        return 1 - Math.Exp(-x + a * Math.Log(x) - LogGamma(a)) * h;
    }

    private static double LogGamma(double value)
    {
        double[] coefficients =
        [
            676.5203681218851, -1259.1392167224028, 771.32342877765313,
            -176.61502916214059, 12.507343278686905, -0.13857109526572012,
            9.9843695780195716e-6, 1.5056327351493116e-7
        ];
        if (value < 0.5) return Math.Log(Math.PI) - Math.Log(Math.Sin(Math.PI * value)) - LogGamma(1 - value);
        value -= 1; var x = 0.99999999999980993;
        for (var index = 0; index < coefficients.Length; index++) x += coefficients[index] / (value + index + 1);
        var t = value + coefficients.Length - 0.5;
        return 0.5 * Math.Log(2 * Math.PI) + (value + 0.5) * Math.Log(t) - t + Math.Log(x);
    }
}
