using System;
using System.Collections.Generic;
using System.Linq;

namespace SOCYVIA.Services;

/// <summary>
/// Authoritative bilingual UI copy used by the two SOCYVIA AI workspaces.
/// This is presentation copy only; it does not participate in prompts or inference.
/// </summary>
public static class SocyviaAiUiCopy
{
    public const string ArabicSuggestedQuestionsTitle = "أسئلة مقترحة";

    public static IReadOnlyList<(string Arabic, string English)> ProductHelpPrompts { get; } =
    [
        ("كيف أنشئ دراسة جديدة؟", "How do I create a new study?"),
        ("لماذا زر النشر غير مفعل؟", "Why is Publish disabled?"),
        ("ما الفرق بين الملف المحلي ومصدر الوسائط عند النشر؟", "What is the difference between a local file and a published media source?"),
        ("كيف أربط Cloudflare؟", "How do I connect Cloudflare?"),
        ("أين أجد بيانات الجلسات؟", "Where do I find session data?"),
        ("ماذا علي أن أفعل الآن؟", "What should I do next?")
    ];

    public static IReadOnlyList<(string Arabic, string English)> StudyPrompts { get; } =
    [
        ("ماذا علي أن أفعل الآن؟", "What should I do next?"),
        ("لماذا زر النشر غير مفعل؟", "Why is Publish disabled?"),
        ("كيف أضيف صورة أو فيديو؟", "How do I add an image or video?"),
        ("فسر هذه النتيجة", "Interpret this result"),
        ("قارن بين الشروط", "Compare the conditions"),
        ("اشرح حجم الأثر", "Explain the effect size"),
        ("حدد القيود", "Identify limitations"),
        ("اكتب فقرة نتائج", "Draft a Results paragraph"),
        ("اكتب فقرة مناقشة", "Draft a Discussion paragraph"),
        ("اشرح مشكلات جودة البيانات", "Explain data quality issues")
    ];

    public static IReadOnlyList<string> AllArabicRuntimeStrings { get; } =
    [
        ArabicSuggestedQuestionsTitle,
        .. ProductHelpPrompts.Select(item => item.Arabic),
        .. StudyPrompts.Select(item => item.Arabic)
    ];

    public static bool ContainsMalformedArabic(string value) =>
        value.Contains("اسي" + "لة", StringComparison.Ordinal) ||
        value.Contains("اسئ" + "له", StringComparison.Ordinal) ||
        value.Contains("نتا" + "يج", StringComparison.Ordinal) ||
        value.Contains("الوسا" + "يط", StringComparison.Ordinal);
}
