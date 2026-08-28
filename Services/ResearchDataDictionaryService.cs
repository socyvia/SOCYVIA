using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public sealed record DataDictionaryEntry(
    string VariableId,
    string DisplayName,
    string Definition,
    string VariableClass,
    string Type,
    string Unit,
    string Source,
    string Stage,
    string MissingValueMeaning,
    string EligibilityNote,
    string? QuestionWording = null,
    string? QuestionId = null,
    string? QuestionnaireVersion = null,
    string? RunTypeProvenance = null);

public static class ResearchDataDictionaryService
{
    public static async Task<IReadOnlyList<DataDictionaryEntry>> ForStudyAsync(Study study, bool arabic)
    {
        var entries = Base(arabic).ToList();
        var questionnaires = await QuestionnaireRepository.GetByStudyAsync(study.Id);
        var assignments = await QuestionnaireRepository.GetAssignmentsAsync(study.Id);
        foreach (var questionnaire in questionnaires.OrderBy(item => item.SortOrder))
        foreach (var version in questionnaire.Versions.OrderBy(item => item.VersionNumber))
        {
            var stages = assignments.Where(item => item.QuestionnaireVersionId == version.Id)
                .Select(item => item.Placement).Distinct(StringComparer.Ordinal).ToArray();
            var stage = stages.Length == 0 ? "Unassigned" : string.Join(" / ", stages);
            foreach (var question in version.Questions.OrderBy(item => item.SortOrder))
            {
                var wording = string.IsNullOrWhiteSpace(question.QuestionText) ? questionnaire.Title : question.QuestionText;
                entries.Add(new DataDictionaryEntry(
                    string.IsNullOrWhiteSpace(question.VariableName) ? $"questionnaire:{question.Id}" : question.VariableName,
                    wording,
                    arabic ? "استجابة لسؤال صاغه الباحث ضمن أداة الاستبيان المحددة." : "Response to the researcher-authored question in the specified questionnaire instrument.",
                    "Questionnaire",
                    question.QuestionType,
                    string.Empty,
                    questionnaire.Title,
                    stage,
                    question.IsRequired
                        ? (arabic ? "مفقود إذا لم تكتمل الاستجابة المطلوبة." : "Missing when the required response was not completed.")
                        : (arabic ? "قد يكون مفقودا لأن السؤال اختياري." : "May be missing because the question is optional."),
                    arabic ? "تتبع العينة التحليلية الافتراضية الجلسات الرئيسية المكتملة والمؤهلة." : "The default analytical sample follows completed eligible Main sessions.",
                    wording,
                    question.Id,
                    version.VersionLabel ?? version.VersionNumber.ToString(),
                    "Main / Pilot retained; Main is the default analytical sample."));
            }
        }
        return entries.OrderBy(item => item.VariableClass).ThenBy(item => item.Source).ThenBy(item => item.VariableId, StringComparer.Ordinal).ToArray();
    }

    public static string Csv(IEnumerable<DataDictionaryEntry> entries)
    {
        var builder = new StringBuilder("Variable ID,Display Name,Definition,Variable Class,Type,Unit,Source,Stage,Missing Value Meaning,Derivation or Eligibility,Question Wording,Question ID,Questionnaire Version,RunType Provenance\n");
        foreach (var entry in entries)
            builder.AppendLine(string.Join(',', new[]
            {
                entry.VariableId, entry.DisplayName, entry.Definition, entry.VariableClass,
                entry.Type, entry.Unit, entry.Source, entry.Stage, entry.MissingValueMeaning,
                entry.EligibilityNote, entry.QuestionWording ?? string.Empty, entry.QuestionId ?? string.Empty,
                entry.QuestionnaireVersion ?? string.Empty, entry.RunTypeProvenance ?? string.Empty
            }.Select(Escape)));
        return builder.ToString();
    }

    public static string Json(IEnumerable<DataDictionaryEntry> entries) =>
        JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });

    private static IEnumerable<DataDictionaryEntry> Base(bool arabic)
    {
        yield return Entry("session_duration_ms", arabic ? "مدة الجلسة" : "Session duration", arabic ? "مدة الجلسة المؤهلة للمشارك." : "Eligible participant session duration.", "Derived", "Numeric", "ms", arabic ? "الجلسة والسلوك" : "Session and behavioral telemetry", arabic ? "الجلسة" : "Session", arabic ? "لا توجد جلسة مكتملة." : "No completed session.");
        yield return Entry("qualified_impressions", arabic ? "مرات التعرض المؤهل" : "Qualified exposures", arabic ? "مرات التعرض التي تستوفي قاعدة التعرض المؤهل." : "Exposures meeting the qualified-exposure rule.", "Derived", "Count", "count", arabic ? "القياس السلوكي" : "Behavioral telemetry", arabic ? "الخلاصة" : "Feed", arabic ? "لا توجد أحداث مؤهلة." : "No qualified events.");
        yield return Entry("mean_dwell_time_ms", arabic ? "متوسط مدة البقاء" : "Mean dwell time", arabic ? "متوسط زمن البقاء المسجل؛ لا يمثل الفهم." : "Mean recorded dwell time; not comprehension.", "Derived", "Numeric", "ms", arabic ? "القياس السلوكي" : "Behavioral telemetry", arabic ? "الخلاصة" : "Feed", arabic ? "لا توجد فترات بقاء." : "No dwell intervals.");
        yield return Entry("likes", arabic ? "الإعجابات" : "Likes", arabic ? "عدد أحداث الإعجاب المسجلة؛ لا يمثل الاتفاق." : "Recorded like events; not agreement.", "Raw", "Count", "count", arabic ? "القياس السلوكي" : "Behavioral telemetry", arabic ? "الخلاصة" : "Feed", arabic ? "لا توجد أحداث." : "No events.");
        yield return Entry("run_type", arabic ? "نوع التشغيل" : "Run type", arabic ? "مصدر الجلسة الرئيسي أو الاستطلاعي." : "Authoritative Main or Pilot session provenance.", "Study Metadata", "Categorical", string.Empty, arabic ? "الجلسة" : "Session", arabic ? "دورة الحياة" : "Lifecycle", arabic ? "تتعامل السجلات القديمة كتشغيل رئيسي." : "Legacy records are interpreted as Main.");
    }

    private static DataDictionaryEntry Entry(string id, string name, string definition, string variableClass, string type, string unit, string source, string stage, string missing) =>
        new(id, name, definition, variableClass, type, unit, source, stage, missing,
            "Completed eligible Main-study sessions by default.", RunTypeProvenance: "Main / Pilot retained; Main is the default analytical sample.");

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
