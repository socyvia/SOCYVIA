using System;
using System.Collections.Generic;
using System.Linq;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

/// <summary>
/// Curated from real navigation, commands, and workflows. Only relevant topics
/// are sent to SOCYVIA.AI/1; this is product documentation, not a marketing FAQ.
/// </summary>
public static class SocyviaAiProductKnowledgeService
{
    private sealed record Topic(string Id, string[] Keywords, string ArSection, string EnSection,
        string ArGuidance, string EnGuidance, string[] ArActions, string[] EnActions);

    private static readonly Topic[] Topics =
    [
        new("studies", ["study", "project", "new", "create", "دراسة", "مشروع", "جديدة", "أنشئ", "كيفاش"],
            "الدراسات", "Studies",
            "افتح الدراسات ثم استخدم إنشاء دراسة. أكمل معلومات الدراسة واحفظها لفتح مساحة العمل الخاصة بها.",
            "Open Studies, then use Create Study. Complete the study information and save it to open its workspace.",
            ["الدراسات", "إنشاء دراسة"], ["Studies", "Create Study"]),
        new("design", ["group", "condition", "assignment", "تعيين", "مجموعة", "شرط", "شروط"],
            "المجموعات والشروط", "Groups and Conditions",
            "استخدم إعداد المجموعات والشروط داخل الدراسة، ثم راجع التعيين قبل التحقق والنشر.",
            "Use Groups and Conditions inside the study, then review assignment before validation and publishing.",
            ["المجموعات والشروط", "التعيين"], ["Groups and Conditions", "Assignment"]),
        new("media", ["media", "image", "video", "audio", "local", "source", "صورة", "فيديو", "صوت", "وسائط", "محلي", "مصدر"],
            "المحتوى والوسائط", "Content and Media",
            "استخدم إضافة محتوى للنص، وإضافة وسائط للملف المحلي المستخدم في المعاينة، وإضافة رابط خارجي لصفحة ويب. تحتاج الوسائط المحلية إلى مصدر وسائط HTTPS عند النشر كي يصل إليها المشاركون.",
            "Use Add Content for text, Add Media for a local preview file, and Add External Link for a web page. Local media needs an HTTPS published media source so participants can reach it.",
            ["إضافة محتوى", "إضافة وسائط", "إضافة رابط خارجي", "مصدر الوسائط عند النشر"],
            ["Add Content", "Add Media", "Add External Link", "Published media source"]),
        new("participants", ["participant", "participants", "مشارك", "مشاركين"],
            "المشاركون", "Participants",
            "افتح قسم المشاركين من مساحة الدراسة لإدارة رموز المشاركين ومراجعة ارتباطهم بالدراسة.",
            "Open Participants from the study workspace to manage participant codes and review their study association.",
            ["المشاركون"], ["Participants"]),
        new("flow", ["flow", "stage", "preview", "معاينة", "تدفق", "مرحلة"],
            "مسار المشارك", "Participant Flow",
            "راجع مراحل مسار المشارك ثم استخدم المعاينة الآمنة للتحقق من الترتيب قبل النشر.",
            "Review Participant Flow stages, then use the safe preview to verify their order before publishing.",
            ["مسار المشارك", "معاينة كمشارك"], ["Participant Flow", "Preview as Participant"]),
        new("questionnaires", ["questionnaire", "survey", "pre", "post", "استبيان", "قبلي", "بعدي"],
            "الاستبيانات", "Questionnaires",
            "أضف الاستبيان القبلي أو البعدي من مرحلة الاستبيانات داخل الدراسة، ثم اربطه بالمرحلة المناسبة في مسار المشارك.",
            "Add a pre- or post-questionnaire from the study questionnaire stage, then associate it with the appropriate Participant Flow phase.",
            ["الاستبيان القبلي", "الاستبيان البعدي", "مسار المشارك"],
            ["Pre-questionnaire", "Post-questionnaire", "Participant Flow"]),
        new("publish", ["publish", "disabled", "button", "ready", "نشر", "الزر", "معطل", "مطفي", "جاهز"],
            "النشر", "Publish",
            "يبقى زر نشر التجربة ظاهرا لكنه غير مفعل عندما لا يتحقق أحد شروط الجاهزية. راجع سبب المنع الحالي المعروض في مساحة النشر، ثم صحح الإعداد المرتبط به وأعد التحقق.",
            "Publish Experiment remains visible but disabled when a readiness condition fails. Use the current blocking reason shown in Publish, correct that configuration, then validate again.",
            ["نشر التجربة", "إعداد روابط الوسائط", "التحقق والمعاينة"],
            ["Publish Experiment", "Set media URLs", "Validation and Preview"]),
        new("cloudflare", ["cloudflare", "cloud", "remote", "سحابة", "بعيد", "ربط"],
            "Cloudflare", "Cloudflare",
            "افتح الإعدادات ثم استخدم ربط Cloudflare. يعيد SOCYVIA استخدام قاعدة البحث وبيئة التجربة المتوافقة؛ لا يحتاج المسار العادي إلى معرفات يدوية.",
            "Open Settings and use Connect Cloudflare. SOCYVIA reuses the compatible research database and experiment runtime; the normal path does not require manual IDs.",
            ["الإعدادات", "ربط Cloudflare", "إعادة المحاولة"], ["Settings", "Connect Cloudflare", "Retry"]),
        new("data", ["session", "data", "where", "جلسة", "جلسات", "بيانات", "فين", "أين"],
            "الجلسات والبيانات", "Sessions and Data",
            "افتح الدراسة ثم الجلسات أو نتائج البحث بعد المزامنة لمراجعة المشاركين والجلسات والأحداث والاستجابات المرتبطة بها.",
            "Open the study, then Sessions or Research Results after synchronization to review linked participants, sessions, events, and responses.",
            ["الجلسات", "مزامنة البيانات البعيدة", "نتائج البحث"], ["Sessions", "Sync Remote Data", "Research Results"]),
        new("analysis", ["analysis", "report", "export", "result", "تحليل", "تقرير", "تصدير", "نتيجة"],
            "التحليل والتقارير", "Analysis and Reports",
            "استخدم التحليل للحسابات الحتمية، ونتائج البحث لمراجعة الأدلة، والتقرير أو التصدير لإخراج النتائج. تظل الحسابات الحتمية مصدر الحقيقة.",
            "Use Analysis for deterministic calculations, Research Results to review evidence, and Report or Exports for outputs. Deterministic calculations remain the source of truth.",
            ["التحليل", "نتائج البحث", "التقرير", "التصدير"], ["Analysis", "Research Results", "Report", "Exports"]),
        new("demo", ["demo", "عرض", "تجريبي"],
            "العرض التجريبي العام", "Public Demo",
            "العرض التجريبي العام اصطناعي وللقراءة فقط، وهو منفصل عن معاينة الدراسة الحالية وعن رابط التجربة المنشورة.",
            "The Public Demo is synthetic and read-only. It is separate from current-study Preview and from the published experiment link.",
            ["العرض التجريبي العام"], ["Public Demo"]),
        new("ai", ["socyvia ai", "assistant", "help", "ساعد", "مساعد", "شرح", "شنو"],
            "SOCYVIA AI", "SOCYVIA AI",
            "يمكنك كتابة سؤال حر عن SOCYVIA أو عن الدراسة الحالية. يستخدم المساعد حالة التطبيق الآمنة والأدلة التجميعية ولا يغير بيانات البحث.",
            "Ask a free-text question about SOCYVIA or the current study. The assistant uses safe application state and aggregate evidence and never changes research data.",
            ["محادثة جديدة", "مسح المحادثة", "إرسال"], ["New Conversation", "Clear Conversation", "Send"])
    ];

    public static string DetermineMode(string? prompt, bool studyOpen, bool evidenceAvailable)
    {
        var value = prompt ?? string.Empty;
        if (studyOpen && string.IsNullOrWhiteSpace(value)) return SocyviaAiAssistantModes.ScientificInterpretation;
        if (ContainsScientificIntent(value)) return SocyviaAiAssistantModes.ScientificInterpretation;
        if (!studyOpen) return SocyviaAiAssistantModes.ProductHelp;
        return evidenceAvailable ? SocyviaAiAssistantModes.ContextualGuidance : SocyviaAiAssistantModes.ProductHelp;
    }

    public static bool ContainsScientificIntent(string value) => ContainsAny(value,
        "interpret", "result", "finding", "behavioral pattern", "p-value", "effect size", "significance", "compare", "comparison",
        "limitation", "data quality", "results paragraph", "discussion paragraph",
        "فسر", "النتيجة", "النتائج", "نمط سلوكي", "قيمة p", "حجم الأثر", "الدلالة", "قارن", "مقارنة", "فرق",
        "القيود", "حدود النتيجة", "جودة البيانات", "فقرة نتائج", "فقرة مناقشة");

    public static SocyviaAiProductContext SelectRelevant(string? prompt, bool arabic)
    {
        var value = prompt ?? string.Empty;
        var selected = Topics.Where(topic => topic.Keywords.Any(keyword =>
                value.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .Take(4).ToArray();
        if (selected.Length == 0) selected = Topics.Where(topic => topic.Id is "studies" or "ai" or "publish").ToArray();
        return new("SOCYVIA.ProductHelp/1", selected.Select(topic => new SocyviaAiProductHelpTopic(
            topic.Id, arabic ? topic.ArSection : topic.EnSection,
            arabic ? topic.ArGuidance : topic.EnGuidance,
            arabic ? topic.ArActions : topic.EnActions)).ToArray());
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
