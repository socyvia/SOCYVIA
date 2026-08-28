using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

public sealed record ResearchReportDocument(string StudyId, string DatasetHash, DateTime GeneratedAtUtc, IReadOnlyList<string> Sections, string Markdown);

/// <summary>Reusable report composition over computed outputs. It creates a portable Markdown document; no fake PDF writer is used.</summary>
public static class ResearchReportService
{
    public static ResearchReportDocument Build(Study study, AnalysisDataset dataset, DataQualityResult quality, IReadOnlyList<AnalysisExecution> analyses, IReadOnlyList<string> sections)
    {
        var builder = new StringBuilder($"# {study.Title}\n\nGenerated: {DateTime.UtcNow:O}\n\nDataset hash: `{dataset.DatasetHash}`\n\n");
        if (sections.Contains("Study Overview")) builder.AppendLine("## Study Overview\n\nThis report is generated from synchronized, completed eligible participant records.");
        if (sections.Contains("Sample / Participation")) builder.AppendLine($"## Sample / Participation\n\nEligible analytical N: {dataset.Rows.Count}. Excluded records: {quality.ExcludedN}.");
        if (sections.Contains("Data Quality")) builder.AppendLine($"## Data Quality\n\nMissing values are retained as missing; no automatic imputation was performed. Incomplete sessions are excluded from the analytical sample.");
        if (sections.Contains("Statistical Analyses")) foreach (var execution in analyses.Where(item => item.Result is not null)) builder.AppendLine($"## {execution.Result!.Method}\n\nN={execution.Result.N}; statistic={execution.Result.Statistic}; p={execution.Result.PValue}. {execution.Result.CanonicalSummary}\n");
        builder.AppendLine("## Methodological Notes\n\nQualified exposure is an observable visibility-duration rule, not a direct measurement of attention. Deterministic results remain the source of truth.");
        return new ResearchReportDocument(study.Id, dataset.DatasetHash, DateTime.UtcNow, sections, builder.ToString());
    }
}
