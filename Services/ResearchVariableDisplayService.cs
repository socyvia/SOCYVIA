using SOCYVIA.Models;

namespace SOCYVIA.Services;

/// <summary>Maps analysis storage identifiers to researcher-facing scientific labels.</summary>
public static class ResearchVariableDisplayService
{
    public static string Label(AnalysisVariable variable)
    {
        if (variable.Id.StartsWith("question:", System.StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(variable.Name)) return variable.Name;

        return variable.Id.ToLowerInvariant() switch
        {
            "session_duration_ms" => T("مدة الجلسة", "Session duration"),
            "session_duration_seconds" => T("مدة الجلسة", "Session duration"),
            "qualified_impressions" => T("مرات التعرض المؤهل", "Qualified impressions"),
            "qualified_exposure_seconds" => T("مدة التعرض المؤهل", "Qualified exposure duration"),
            "content_exposures" or "meaningful_exposures" => T("مرات التعرض المؤهل", "Qualified exposures"),
            "meaningful_exposure_time_ms" => T("زمن التعرض المؤهل", "Qualified exposure time"),
            "mean_dwell_time_ms" => T("متوسط مدة البقاء", "Mean dwell time"),
            "median_dwell_time_ms" => T("وسيط مدة البقاء", "Median dwell time"),
            "likes" => T("الإعجابات", "Likes"),
            "comments" => T("التعليقات", "Comments"),
            "saves" => T("عمليات الحفظ", "Saves"),
            "shares" => T("عمليات المشاركة", "Shares"),
            "content_opens" => T("فتح المحتوى", "Content opens"),
            "read_more" or "read_more_opens" => T("فتح اقرأ المزيد", "Read More opens"),
            "post_opens" => T("فتح المنشور", "Post opens"),
            "link_opens" => T("فتح الروابط", "Link opens"),
            "maximum_scroll_depth" or "max_scroll_depth" => T("أقصى عمق للتمرير", "Maximum scroll depth"),
            "interaction_count" => T("عدد التفاعلات", "Interaction count"),
            "interaction_rate_per_meaningful_exposure" => T("معدل التفاعل لكل تعرض مؤهل", "Interaction rate per qualified exposure"),
            _ => string.IsNullOrWhiteSpace(variable.Name) ? variable.Id : variable.Name
        };
    }

    public static string WithMeasurementLevel(AnalysisVariable variable) =>
        $"{Label(variable)} · {variable.MeasurementLevel}";

    private static string T(string arabic, string english) => UiTextService.Localized(arabic, english);
}
