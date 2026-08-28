using System;
using System.Collections.Generic;
using System.IO;

namespace SOCYVIA.Services;

public enum AppLanguage
{
    English,
    Arabic
}

public static class LocalizationService
{
    private static AppLanguage _currentLanguage = AppLanguage.English;

    public static AppLanguage CurrentLanguage => _currentLanguage;

    public static bool IsArabic =>
        _currentLanguage == AppLanguage.Arabic;

    public static event EventHandler? LanguageChanged;


    private static readonly Dictionary<string, string> English =
        new()
        {
            ["BrandSubtitle"] =
                "Scientific experimentation · computational social science",

            ["LoginHeroLine1"] =
                "Research without",

            ["LoginHeroLine2"] =
                "losing the human context.",

            ["LoginDescription"] =
                "Organize studies, collect data and transform\ndigital traces into meaningful social insights.",

            ["FeatureResearchDesign"] =
                "Research design & participant management",

            ["FeatureDigitalInteraction"] =
                "Digital interaction & behavioral data",

            ["FeatureAnalysis"] =
                "Analysis, visualization & research reporting",

            ["Language"] =
                "Language",

            ["ResearcherAccess"] =
                "Researcher Access",

            ["ResearcherAccessSubtitle"] =
                "Enter your research workspace.",

            ["ResearcherName"] =
                "Researcher name",

            ["ResearcherNamePlaceholder"] =
                "Enter your full name",

            ["Password"] =
                "Password",

            ["Optional"] =
                "Optional",

            ["PasswordPlaceholder"] =
                "Enter password",

            ["RememberResearcher"] =
                "Remember me on this device",

            ["EnterWorkspace"] =
                "Enter Workspace",

            ["FirstUse"] =
                "Your researcher profile will be created automatically on first use.",

            ["Privacy"] =
                "I agree to the Research Data & Privacy Policy"
        };


    private static readonly Dictionary<string, string> Arabic =
        new()
        {
            ["BrandSubtitle"] =
                "التجريب العلمي · العلوم الاجتماعية الحاسوبية",

            ["LoginHeroLine1"] =
                "من البيانات",

            ["LoginHeroLine2"] =
                "نفهم المجتمع",

            ["LoginDescription"] =
                "نظم دراساتك واجمع بياناتك وحول الآثار الرقمية\nإلى معرفة اجتماعية قابلة للتحليل",

            ["FeatureResearchDesign"] =
                "تصميم الدراسات وإدارة المشاركين",

            ["FeatureDigitalInteraction"] =
                "التفاعل الرقمي والبيانات السلوكية",

            ["FeatureAnalysis"] =
                "التحليل والتصور البصري والتقارير",

            ["Language"] =
                "اللغة",

            ["ResearcherAccess"] =
                "مساحة الباحث",

            ["ResearcherAccessSubtitle"] =
                "انتقل إلى بيئة البحث الخاصة بك",

            ["ResearcherName"] =
                "اسم الباحث",

            ["ResearcherNamePlaceholder"] =
                "أدخل اسمك الكامل",

            ["Password"] =
                "كلمة المرور",

            ["Optional"] =
                "اختياري",

            ["PasswordPlaceholder"] =
                "أدخل كلمة المرور",

            ["RememberResearcher"] =
                "تذكرني على هذا الجهاز",

            ["EnterWorkspace"] =
                "تسجيل الدخول إلى المنصة",

            ["FirstUse"] =
                "سيتم إنشاء ملف الباحث تلقائيا عند أول استخدام",

            ["Privacy"] =
                "أوافق على سياسة البيانات والخصوصية"
        };


    public static string Get(string key)
    {
        var dictionary =
            _currentLanguage == AppLanguage.Arabic
                ? Arabic
                : English;

        return dictionary.TryGetValue(key, out var value)
            ? value
            : key;
    }


    public static void SetLanguage(AppLanguage language)
    {
        if (_currentLanguage == language)
        {
            SaveLanguage();
            return;
        }

        _currentLanguage = language;

        LanguageChanged?.Invoke(
            null,
            EventArgs.Empty);

        SaveLanguage();
    }


    public static void Initialize()
    {
        try
        {
            if (!File.Exists(StorageService.LanguageFile))
            {
                return;
            }

            var value = File.ReadAllText(StorageService.LanguageFile).Trim();
            if (Enum.TryParse<AppLanguage>(value, true, out var language))
            {
                _currentLanguage = language;
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Language preference load error: {exception.Message}");
        }
    }


    private static void SaveLanguage()
    {
        try
        {
            File.WriteAllText(
                StorageService.LanguageFile,
                _currentLanguage.ToString());
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Language preference save error: {exception.Message}");
        }
    }
}
