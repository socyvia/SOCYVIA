using SOCYVIA.Models;

namespace SOCYVIA.Services;

/// <summary>Localized labels for SOCYVIA-owned condition presentation settings.</summary>
public static class ConditionPresentationTextService
{
    public static string EngagementMode(ConditionManipulationSettings settings) =>
        !settings.ShowEngagementMetrics || settings.LikesMode == MetricManipulationMode.Hidden
            ? T("إخفاء مؤشرات التفاعل", "Engagement metrics hidden")
            : settings.LikesMode switch
            {
                MetricManipulationMode.Original => T("إظهار مؤشرات التفاعل الأصلية", "Original engagement visible"),
                MetricManipulationMode.Multiplier => T("تضخيم مؤشرات التفاعل", "Engagement metrics amplified"),
                MetricManipulationMode.Fixed or MetricManipulationMode.RandomRange => T("مخصص", "Custom"),
                _ => T("مخصص", "Custom")
            };

    private static string T(string arabic, string english) => UiTextService.Localized(arabic, english);
}
