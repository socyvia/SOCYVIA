using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

public sealed record ResearchFigure(string Id, string Title, string Kind, string Svg, string CsvData, string DatasetHash, DateTime GeneratedAtUtc);

/// <summary>Deterministic, data-derived SVG figures. SVG is exported honestly; PNG/PDF require a future renderer.</summary>
public static class ResearchFigureService
{
    public static ResearchFigure CreateGroupedMeanFigure(AnalysisDataset dataset, string variableId, string groupingVariable = "condition")
    {
        var variable = dataset.Variables.Single(item => item.Id == variableId);
        var groups = dataset.Rows.Select(row => new { Name = groupingVariable == "group" ? row.GroupName : row.ConditionName, Value = row.NumericValues.GetValueOrDefault(variableId) })
            .Where(item => item.Name is not null && item.Value.HasValue && double.IsFinite(item.Value.Value))
            .GroupBy(item => item.Name!, StringComparer.Ordinal).OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new { item.Key, N = item.Count(), Mean = item.Average(value => value.Value!.Value) }).ToArray();
        var width = 960d; var height = 560d; var left = 100d; var bottom = 90d; var top = 72d; var max = Math.Max(1, groups.Select(item => item.Mean).DefaultIfEmpty(1).Max()); var plotHeight = height - top - bottom;
        var svg = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\"><rect width=\"100%\" height=\"100%\" fill=\"white\"/><text x=\"{width / 2}\" y=\"34\" text-anchor=\"middle\" font-family=\"IBM Plex Sans, sans-serif\" font-size=\"20\" fill=\"#1e304a\">{Escape(variable.Name)} by {Escape(groupingVariable)}</text><line x1=\"{left}\" y1=\"{height - bottom}\" x2=\"{width - 40}\" y2=\"{height - bottom}\" stroke=\"#64748b\"/><line x1=\"{left}\" y1=\"{top}\" x2=\"{left}\" y2=\"{height - bottom}\" stroke=\"#64748b\"/>");
        for (var index = 0; index < groups.Length; index++) { var item = groups[index]; var barWidth = Math.Min(110, (width - left - 80) / Math.Max(1, groups.Length) * .64); var x = left + 40 + index * ((width - left - 80) / Math.Max(1, groups.Length)); var barHeight = item.Mean / max * plotHeight; var y = height - bottom - barHeight; svg.Append($"<rect x=\"{x.ToString(CultureInfo.InvariantCulture)}\" y=\"{y.ToString(CultureInfo.InvariantCulture)}\" width=\"{barWidth.ToString(CultureInfo.InvariantCulture)}\" height=\"{barHeight.ToString(CultureInfo.InvariantCulture)}\" fill=\"{Color(item.Key)}\"/><text x=\"{x + barWidth / 2}\" y=\"{height - bottom + 24}\" text-anchor=\"middle\" font-family=\"IBM Plex Sans, sans-serif\" font-size=\"12\" fill=\"#334155\">{Escape(item.Key)}</text><text x=\"{x + barWidth / 2}\" y=\"{y - 8}\" text-anchor=\"middle\" font-family=\"IBM Plex Sans, sans-serif\" font-size=\"12\" fill=\"#334155\">{item.Mean.ToString("0.##", CultureInfo.InvariantCulture)} (n={item.N})</text>"); }
        svg.Append($"<text x=\"22\" y=\"{height / 2}\" transform=\"rotate(-90 22 {height / 2})\" text-anchor=\"middle\" font-family=\"IBM Plex Sans, sans-serif\" font-size=\"14\" fill=\"#334155\">{Escape(variable.Unit ?? variable.Name)}</text></svg>");
        var csv = new StringBuilder("group,n,mean\n"); foreach (var item in groups) csv.AppendLine($"{Csv(item.Key)},{item.N},{item.Mean.ToString(CultureInfo.InvariantCulture)}");
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataset.DatasetHash + variableId + groupingVariable))).ToLowerInvariant()[..16];
        return new ResearchFigure(id, $"{variable.Name} by {groupingVariable}", "GROUPED_MEAN", svg.ToString(), csv.ToString(), dataset.DatasetHash, DateTime.UtcNow);
    }
    private static string Color(string value) { var colors = new[] { "#2563eb", "#0f766e", "#b45309", "#7c3aed", "#be123c", "#475569" }; var index = SHA256.HashData(Encoding.UTF8.GetBytes(value))[0] % colors.Length; return colors[index]; }
    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
    private static string Csv(string value) => value.IndexOfAny([',','"','\r','\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
