using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

public enum OnboardingOutcome
{
    None,
    Completed,
    Skipped,
    CreateStudy,
    OpenStudy,
    ExploreDemo
}

public partial class OnboardingTourWindow : Window
{
    private readonly IReadOnlyList<TourStep> _steps;
    private int _stepIndex = -1;

    public OnboardingTourWindow()
    {
        InitializeComponent();
        WindowAppearanceService.ApplyAppIcon(this);
        OnboardingWindowChrome.Attach(this, showMinimize: false, showMaximize: false);
        _steps = BuildSteps();
        SkipButton.Click += (_, _) => Close(OnboardingOutcome.Skipped);
        BackButton.Click += (_, _) => ShowStep(_stepIndex - 1);
        NextButton.Click += (_, _) => Advance();
        CreateStudyButton.Click += (_, _) => Close(OnboardingOutcome.CreateStudy);
        OpenStudyButton.Click += (_, _) => Close(OnboardingOutcome.OpenStudy);
        ExploreDemoButton.Click += (_, _) => Close(OnboardingOutcome.ExploreDemo);
        ConfigureWelcome();
    }

    private void ConfigureWelcome()
    {
        var arabic = LocalizationService.IsArabic;
        FlowDirection = arabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        FontFamily = new FontFamily(arabic
            ? "avares://SOCYVIA/Assets/Fonts#IBM Plex Sans Arabic"
            : "avares://SOCYVIA/Assets/Fonts#IBM Plex Sans");
        Title = Text("مرحبا بك في SOCYVIA", "Welcome to SOCYVIA");
        TourContextText.Text = Text(
            SocyviaProductIdentity.ArabicPositioning,
            SocyviaProductIdentity.EnglishPositioning);
        StepCounterText.Text = Text("مرحبا", "WELCOME");
        SpotlightBrandMark.IsVisible = true;
        SpotlightGlyph.IsVisible = false;
        SpotlightGlyph.Text = string.Empty;
        TourTitleText.Text = Text("مرحبا بك في SOCYVIA", "Welcome to SOCYVIA");
        TourBodyText.Text = Text(
            "بيئة علمية لتصميم التجارب الرقمية المضبوطة وتشغيلها ومراقبة بياناتها السلوكية محليا.",
            "A scientific environment for designing, running, and observing controlled digital experiments with local behavioural data.");
        TourHintText.Text = Text(
            "ستشرح الجولة الوظائف المتاحة فعليا.",
            "The tour covers functionality available now.");
        SkipButton.Content = Text("تخطي", "Skip");
        BackButton.Content = Text("رجوع", "Back");
        BackButton.IsVisible = false;
        NextButton.Content = Text("بدء الجولة", "Start tour");
        FirstLaunchActions.IsVisible = true;
        CreateStudyButton.Content = Text("إنشاء دراسة", "Create a Study");
        OpenStudyButton.Content = Text("فتح دراسة موجودة", "Open Existing Study");
        ExploreDemoButton.Content = Text("استكشاف العرض التجريبي", "Explore Public Demo");
    }

    private void Advance()
    {
        if (_stepIndex >= _steps.Count - 1)
        {
            Close(OnboardingOutcome.Completed);
            return;
        }
        ShowStep(_stepIndex + 1);
    }

    private void ShowStep(int index)
    {
        if (index < 0)
        {
            _stepIndex = -1;
            ConfigureWelcome();
            return;
        }

        _stepIndex = Math.Min(index, _steps.Count - 1);
        FirstLaunchActions.IsVisible = false;
        var step = _steps[_stepIndex];
        StepCounterText.Text = LocalizationService.IsArabic
            ? string.Format("{0} من {1}", _stepIndex + 1, _steps.Count)
            : string.Format("{0} OF {1}", _stepIndex + 1, _steps.Count);
        SpotlightBrandMark.IsVisible = false;
        SpotlightGlyph.IsVisible = true;
        SpotlightGlyph.Text = step.Glyph;
        TourTitleText.Text = Text(step.ArabicTitle, step.EnglishTitle);
        TourBodyText.Text = Text(step.ArabicBody, step.EnglishBody);
        TourHintText.Text = Text(step.ArabicHint, step.EnglishHint);
        BackButton.IsVisible = true;
        BackButton.Content = Text("السابق", "Back");
        NextButton.Content = _stepIndex == _steps.Count - 1
            ? Text("إنهاء", "Finish")
            : Text("التالي", "Next");
    }

    private static IReadOnlyList<TourStep> BuildSteps() =>
        new[]
        {
            new TourStep("01", "لوحة المتابعة", "Dashboard",
                "يوضح مركز الأبحاث ما تعمل عليه وما يحتاج إلى انتباه والخطوة البحثية التالية.",
                "The Research Command Center shows current work, attention items, and the next research action.",
                "ابدأ من القرار التالي.", "Start with the next decision."),
            new TourStep("02", "مكتبة المحتوى", "Content Library",
                "اجمع المحتوى الرقمي واحفظ مصدره ووقت التقاطه وقيم التفاعل المرصودة قبل ربطه بأي تجربة.",
                "Capture digital content, provenance, capture time, and observed engagement before assigning it to an experiment.",
                "المحتوى مستقل عن المجموعات.", "Content remains independent from groups."),
            new TourStep("03", "الدراسات", "Studies",
                "أنشئ المشاريع البحثية وافتحها وعدلها وأرشفها دون فقد بياناتها.",
                "Create, open, edit, and archive research projects without losing their data.",
                "الأرشفة تحفظ البيانات.", "Archiving preserves data."),
            new TourStep("04", "تصميم التجربة", "Experiment Design",
                "حدد المجموعات والشروط، ثم اختر مواد المكتبة ورتب الخلاصة التجريبية لكل تصميم.",
                "Define groups and conditions, then select library items and order each experimental feed.",
                "حقيقة المصدر والعرض التجريبي طبقتان منفصلتان.", "Source truth and experimental presentation remain separate."),
            new TourStep("05", "المشاركون", "Participants",
                "راجع الأهلية والموافقة والمجموعة والشرط قبل تحضير الجلسة.",
                "Review eligibility, consent, group, and condition before preparing a session.",
                "اعرض الحد الأدنى من البيانات الحساسة.", "Expose only necessary participant data."),
            new TourStep("06", "المعاينة كمشارك", "Preview as Participant",
                "اختر مجموعة وشرطا ثم افتح البيئة المحايدة لترى المحتوى والترتيب والقيم المعروضة كما يراها المشارك.",
                "Choose a group and condition, then open the neutral environment to inspect the exact content, order, and displayed values.",
                "المعاينة لا تسجل سلوك مشارك حقيقي.", "Preview does not record real participant behavior."),
            new TourStep("07", "الجلسات والبيانات", "Sessions and Data",
                "أنشئ لقطة ثابتة وابدأ جلسة المشارك، ثم راجع التعرض والتفاعل مع الحفاظ على السجل التاريخي.",
                "Prepare an immutable snapshot, run the participant session, and review exposure and interaction while preserving history.",
                "الجلسات التاريخية لا تتغير مع تعديلات الدراسة.", "Historical sessions remain immutable.")
        };

    private static string Text(string arabic, string english) =>
        UiTextService.Localized(arabic, english);

    private sealed record TourStep(
        string Glyph,
        string ArabicTitle,
        string EnglishTitle,
        string ArabicBody,
        string EnglishBody,
        string ArabicHint,
        string EnglishHint);
}
