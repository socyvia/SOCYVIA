using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SOCYVIA.Models;
using SOCYVIA.Repositories;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

public partial class GuidedDemoView : UserControl
{
    private readonly ResearcherProfile? _researcher;
    private Study? _study;
    private IReadOnlyList<ContentItem> _content = Array.Empty<ContentItem>();
    private IReadOnlyList<StudyGroup> _groups = Array.Empty<StudyGroup>();
    private IReadOnlyList<ExperimentalCondition> _conditions = Array.Empty<ExperimentalCondition>();
    private IReadOnlyList<Participant> _participants = Array.Empty<Participant>();
    private IReadOnlyList<ExperimentSession> _sessions = Array.Empty<ExperimentSession>();
    private IReadOnlyList<Questionnaire> _questionnaires = Array.Empty<Questionnaire>();
    private IReadOnlyList<QuestionnaireResponse> _questionnaireResponses = Array.Empty<QuestionnaireResponse>();
    private AnalysisDataset? _analysisDataset;
    private DataQualityResult? _dataQuality;
    private AnalysisExecution? _analysisExecution;
    private StudyGroup? _previewGroup;
    private ExperimentalCondition? _previewCondition;
    private bool _initialized;

    private readonly Button[] _stageButtons;

    public GuidedDemoView()
    {
        InitializeComponent();
        _stageButtons =
        [
            ContentStageButton, DesignStageButton, GroupsStageButton,
            ConditionsStageButton, ParticipantsStageButton, ExperimentStageButton,
            ExperienceStageButton, QuestionnaireStageButton, SessionsStageButton,
            AnalysisStageButton, ReportStageButton, AiStageButton
        ];
        ConfigureLanguage();
        SetupNavigation();
    }

    public GuidedDemoView(ResearcherProfile researcher) : this()
    {
        _researcher = researcher;
        AttachedToVisualTree += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (_initialized || _researcher is null) return;
        _initialized = true;
        try
        {
            StageTitleText.Text = T("تحضير العرض التجريبي", "Preparing guided demo");
            StageDescriptionText.Text = T("يتم تجهيز بيانات اصطناعية منفصلة عن ابحاثك", "Preparing synthetic data isolated from your research");
            _study = await DemoExperienceService.InstallAsync(_researcher.Id);
            var contentTask = ContentLibraryService.GetAsync(_researcher.Id, true);
            var groupsTask = GroupRepository.GetByStudyAsync(_study.Id);
            var conditionsTask = ExperimentalConditionRepository.GetByStudyAsync(_study.Id);
            var participantsTask = ParticipantRepository.GetByStudyAsync(_study.Id);
            var sessionsTask = ExperimentSessionRepository.GetByStudyAsync(_study.Id);
            var questionnaireTask = QuestionnaireRepository.GetByStudyAsync(_study.Id);
            var responsesTask = QuestionnaireRepository.GetResponsesByStudyAsync(_study.Id, true);
            var analysisTask = DemoScientificDataService.EnsureComputedAnalysisAsync(_study.Id);
            await Task.WhenAll(contentTask, groupsTask, conditionsTask, participantsTask, sessionsTask,
                questionnaireTask, responsesTask, analysisTask);
            _content = contentTask.Result.Where(item => item.IsDemo).ToArray();
            _groups = groupsTask.Result.OrderBy(item => item.SortOrder).ToArray();
            _conditions = conditionsTask.Result.OrderBy(item => item.SortOrder).ToArray();
            _participants = participantsTask.Result;
            _sessions = sessionsTask.Result;
            _questionnaires = questionnaireTask.Result;
            _questionnaireResponses = responsesTask.Result;
            (_analysisDataset, _dataQuality, _, _analysisExecution) = analysisTask.Result;
            _previewGroup = _groups.FirstOrDefault();
            _previewCondition = ConditionFor(_previewGroup);
            ShowStage(DemoStage.Content);
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Open permanent guided demo");
            StageTitleText.Text = T("تعذر فتح العرض التجريبي", "Guided demo could not be opened");
            StageDescriptionText.Text = T("تم حفظ تقرير فني محلي ولم تتغير بيانات البحث", "A local diagnostic report was saved and research data was not changed");
        }
    }

    private void SetupNavigation()
    {
        ContentStageButton.Click += (_, _) => ShowStage(DemoStage.Content);
        DesignStageButton.Click += (_, _) => ShowStage(DemoStage.Design);
        GroupsStageButton.Click += (_, _) => ShowStage(DemoStage.Groups);
        ConditionsStageButton.Click += (_, _) => ShowStage(DemoStage.Conditions);
        ParticipantsStageButton.Click += (_, _) => ShowStage(DemoStage.Participants);
        ExperimentStageButton.Click += (_, _) => ShowStage(DemoStage.Experiment);
        ExperienceStageButton.Click += (_, _) => ShowStage(DemoStage.Experience);
        QuestionnaireStageButton.Click += (_, _) => ShowStage(DemoStage.Questionnaire);
        SessionsStageButton.Click += (_, _) => ShowStage(DemoStage.Sessions);
        AnalysisStageButton.Click += (_, _) => ShowStage(DemoStage.Analysis);
        ReportStageButton.Click += (_, _) => ShowStage(DemoStage.Report);
        AiStageButton.Click += (_, _) => ShowStage(DemoStage.Ai);
        PreviewButton.Click += async (_, _) => await OpenPreviewAsync();
    }

    private void ConfigureLanguage()
    {
        var arabic = LocalizationService.IsArabic;
        RootDemo.FontFamily = Font(arabic);
        RootDemo.FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        DemoEyebrowText.Text = T("عرض دائم للمنتج", "PERMANENT PRODUCT EXPERIENCE");
        DemoTitleText.Text = T("العرض التجريبي لـ SOCYVIA", "SOCYVIA Guided Demo");
        DemoSubtitleText.Text = T(
            "استكشف رحلة بحث كاملة من المحتوى إلى تصميم التجربة وتجربة المشارك والبيانات",
            "Explore a complete research journey from source content to experiment design, participant experience, and evidence");
        ReadOnlyText.Text = T("للقراءة فقط", "READ-ONLY");
        SyntheticText.Text = T("بيانات اصطناعية", "SYNTHETIC DATA");
        JourneyLabel.Text = T("مسار البحث", "RESEARCH JOURNEY");
        SetButton(ContentStageButton, T("المحتوى", "Content"));
        SetButton(DesignStageButton, T("تصميم الدراسة", "Study design"));
        SetButton(GroupsStageButton, T("المجموعات", "Groups"));
        SetButton(ConditionsStageButton, T("الشروط", "Conditions"));
        SetButton(ParticipantsStageButton, T("المشاركون", "Participants"));
        SetButton(ExperimentStageButton, T("التجربة", "Experiment"));
        SetButton(ExperienceStageButton, T("تجربة المشارك", "Participant experience"));
        SetButton(QuestionnaireStageButton, T("الاستبيان", "Questionnaire"));
        SetButton(SessionsStageButton, T("الجلسات والبيانات", "Sessions and data"));
        SetButton(AnalysisStageButton, T("التحليل", "Analysis"));
        SetButton(ReportStageButton, T("التقرير", "Report"));
        SetButton(AiStageButton, "SOCYVIA AI");
        PreviewButtonText.Text = T("معاينة كمشارك", "Preview as Participant");
        DemoHeroGrid.ColumnDefinitions = arabic ? new ColumnDefinitions("Auto,24,*") : new ColumnDefinitions("*,24,Auto");
        Grid.SetColumn(DemoHeadingPanel, arabic ? 2 : 0);
        Grid.SetColumn(DemoBadgePanel, arabic ? 0 : 2);
        StageHeaderGrid.ColumnDefinitions = arabic ? new ColumnDefinitions("Auto,16,*") : new ColumnDefinitions("*,16,Auto");
        Grid.SetColumn(StageHeadingPanel, arabic ? 2 : 0);
        Grid.SetColumn(PreviewButton, arabic ? 0 : 2);
        DemoHeadingPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        StageHeadingPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        DemoHeadingPanel.FlowDirection = StageHeadingPanel.FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        StageEyebrowText.FlowDirection = FlowDirection.LeftToRight;
        DemoTitleText.TextAlignment = StageTitleText.TextAlignment = arabic ? TextAlignment.Right : TextAlignment.Left;
        DemoSubtitleText.TextAlignment = StageDescriptionText.TextAlignment = arabic ? TextAlignment.Right : TextAlignment.Left;
    }

    private void ShowStage(DemoStage stage)
    {
        if (_study is null && stage != DemoStage.Content) return;
        for (var index = 0; index < _stageButtons.Length; index++)
        {
            _stageButtons[index].Classes.Remove("selected");
        }
        _stageButtons[(int)stage].Classes.Add("selected");
        PreviewButton.IsVisible = stage is DemoStage.Experiment or DemoStage.Experience;
        StageEyebrowText.Text = $"{(int)stage + 1:00} / 12";
        (StageTitleText.Text, StageDescriptionText.Text, StageContent.Content) = stage switch
        {
            DemoStage.Content => (T("محتوى المصدر", "Source content"), T("عشر مواد فريدة يعاد استخدامها عبر الشروط دون تكرار حقيقة المصدر", "Ten unique items are reused across conditions without duplicating source truth"), ContentStage()),
            DemoStage.Design => (T("تصميم مضبوط بين المجموعات", "Controlled between-groups design"), T("يتغير عرض التفاعل فقط بينما يبقى المحتوى المرصود ثابتا", "Only engagement presentation varies while the observed content remains fixed"), DesignStage()),
            DemoStage.Groups => (T("ثلاث مجموعات بحثية", "Three research groups"), T("المجموعة تنظم العينة بينما يحدد الشرط ما يراه المشارك", "Groups organize the sample while conditions define participant presentation"), GroupStage()),
            DemoStage.Conditions => (T("شروط عرض مستقلة", "Independent presentation conditions"), T("القيم الأصلية لا تتغير، وتطبق المعالجة عند العرض فقط", "Original observations never change; manipulation is applied only at presentation"), ConditionStage()),
            DemoStage.Participants => (T("مشاركون اصطناعيون", "Synthetic participants"), T("رموز خيالية توضح التعيين دون بيانات شخصية حقيقية", "Fictional participant codes demonstrate assignment without real personal information"), ParticipantStage()),
            DemoStage.Experiment => (T("منشئ التجربة", "Experiment builder"), T("اختر مجموعة وشرطا، ثم افحص العرض المحدد قبل تشغيل أي جلسة", "Choose a group and condition, then inspect the resolved presentation before any session runs"), ExperimentStage()),
            DemoStage.Experience => (T("منصة المشارك", "Participant experience"), T("معاينة معزولة لا تنشئ جلسة ولا تسجل بيانات أو قياسا سلوكيا", "An isolated preview creates no session and records no participant or behavioral data"), ExperienceStage()),
            DemoStage.Questionnaire => (T("استبيان SOCYVIA التجريبي", "SOCYVIA Demo Questionnaire"), T("أداة توضيحية من خمسة بنود، وليست أداة سيكومترية معتمدة، وتستخدم هنا مع استجابات اصطناعية", "A functional five-item illustrative instrument; not validated; used here with synthetic responses"), QuestionnaireStage()),
            DemoStage.Sessions => (T("من التجربة إلى البيانات", "From experience to data"), T("تفصل SOCYVIA بين حقيقة المصدر وتكوين التجربة والسلوك المرصود", "SOCYVIA separates source truth, experiment configuration, and observed behavior"), SessionsStage()),
            DemoStage.Analysis => (T("تحليل علمي محسوب", "Computed scientific analysis"), T("تحسب النتائج فعليا من السجلات الاصطناعية باستخدام محرك SOCYVIA الحتمي", "Results are computed from synthetic records by the deterministic SOCYVIA engine"), AnalysisStage()),
            DemoStage.Report => (T("معاينة التقارير", "Reporting preview"), T("تقرير توضيحي مبني على نتائج اصطناعية محسوبة وقابلة للتتبع", "An illustrative report built from computed, traceable synthetic results"), ReportPreviewStage()),
            _ => ("SOCYVIA AI", T("معاينة توضيحية لـ SOCYVIA AI", "SOCYVIA AI Illustrative Preview"), AiIllustrativeStage())
        };
    }

    private Control ContentStage()
    {
        var panel = Stack(8);
        panel.Children.Add(MetricStrip(
            (T("مواد فريدة", "Unique items"), _content.Count.ToString()),
            (T("عروض شرطية", "Condition presentations"), (_content.Count * _conditions.Count).ToString()),
            (T("انواع المحتوى", "Content types"), _content.Select(item => item.ContentType).Distinct().Count().ToString())));
        foreach (var item in _content.Take(10))
        {
            panel.Children.Add(Row(item.Title, $"{item.ContentType}  •  {item.Platform}  •  {item.AuthorName}", item.ContentType));
        }
        return panel;
    }

    private Control DesignStage()
    {
        var panel = Stack(10);
        panel.Children.Add(Row(_study?.Title ?? T("دراسة العرض", "Demo study"), T("تصميم بين المجموعات • تعيين متوازن • عينة مستهدفة 60", "Between-groups • balanced assignment • target sample 60"), T("تصميم", "DESIGN")));
        panel.Children.Add(ThreeLayerEvidence());
        panel.Children.Add(PhysiologyReadiness());
        return panel;
    }

    private Control GroupStage()
    {
        var panel = Stack(8);
        foreach (var group in _groups)
        {
            var condition = ConditionFor(group);
            panel.Children.Add(Row(group.Name, $"{condition?.Name ?? T("بلا شرط", "No condition")}  •  {T("هدف", "Target")} {group.TargetSampleSize ?? 0}", group.IsControlGroup ? T("ضابطة", "CONTROL") : T("نشطة", "ACTIVE")));
        }
        return panel;
    }

    private Control ConditionStage()
    {
        var panel = Stack(8);
        foreach (var condition in _conditions)
        {
            var settings = ConditionManipulationService.Deserialize(condition.ManipulationJson);
            var summary = ConditionPresentationTextService.EngagementMode(settings);
            panel.Children.Add(Row(condition.Name, summary, condition.IsControlCondition ? T("ضابط", "CONTROL") : condition.ConditionType));
        }
        return panel;
    }

    private Control ParticipantStage()
    {
        var panel = Stack(8);
        foreach (var participant in _participants)
        {
            var group = _groups.FirstOrDefault(item => item.Id == participant.GroupId);
            panel.Children.Add(Row(participant.ParticipantCode, $"{group?.Name}  •  {T("مؤهل وموافقته موثقة", "Eligible • consent documented")}", T("اصطناعي", "SYNTHETIC")));
        }
        return panel;
    }

    private Control ExperimentStage()
    {
        var panel = Stack(12);
        var selectors = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var selectedGroupIndex = _previewGroup is null
            ? 0
            : Array.FindIndex(_groups.ToArray(), item => item.Id == _previewGroup.Id);
        var groupBox = new ComboBox
        {
            ItemsSource = _groups.Select(item => item.Name).ToArray(),
            SelectedIndex = Math.Max(0, selectedGroupIndex)
        };
        var conditionBox = new ComboBox();
        void RefreshConditions()
        {
            _previewGroup = groupBox.SelectedIndex >= 0 && groupBox.SelectedIndex < _groups.Count ? _groups[groupBox.SelectedIndex] : _groups.FirstOrDefault();
            var compatible = _conditions.Where(item => item.GroupId is null || item.GroupId == _previewGroup?.Id).ToArray();
            conditionBox.ItemsSource = compatible.Select(item => item.Name).ToArray();
            conditionBox.SelectedIndex = 0;
            _previewCondition = compatible.FirstOrDefault();
        }
        groupBox.SelectionChanged += (_, _) => RefreshConditions();
        conditionBox.SelectionChanged += (_, _) =>
        {
            var compatible = _conditions.Where(item => item.GroupId is null || item.GroupId == _previewGroup?.Id).ToArray();
            if (conditionBox.SelectedIndex >= 0 && conditionBox.SelectedIndex < compatible.Length) _previewCondition = compatible[conditionBox.SelectedIndex];
        };
        selectors.Children.Add(Labeled(T("المجموعة", "Group"), groupBox));
        var conditionField = Labeled(T("الشرط", "Condition"), conditionBox);
        Grid.SetColumn(conditionField, 2);
        selectors.Children.Add(conditionField);
        panel.Children.Add(selectors);
        panel.Children.Add(Row(T("تكوين العرض", "Resolved presentation"), T("10 مواد فريدة • ترتيب حتمي • معالجة شرطية لا تغير المصدر", "10 unique items • deterministic order • presentation-only manipulation"), T("جاهز للمعاينة", "READY TO PREVIEW")));
        RefreshConditions();
        return panel;
    }

    private Control ExperienceStage()
    {
        var panel = Stack(10);
        panel.Children.Add(Status(T("معاينة الباحث معزولة بالكامل", "Researcher preview is fully isolated"), "#177A5B"));
        panel.Children.Add(Row(T("لا تسجل بيانات", "No data recorded"), T("لا جلسة • لا مشارك • لا احداث • لا تغيير في قاعدة البيانات", "No session • no participant • no events • no database mutation"), "PREVIEW"));
        panel.Children.Add(Row(T("بيئة اجتماعية محايدة", "Neutral social environment"), T("محتوى وترتيب وقيم عرض مطابقة لتكوين المجموعة والشرط", "Content, order, and displayed values match the selected group and condition"), T("تجربة مضبوطة", "CONTROLLED")));
        panel.Children.Add(ThreeLayerEvidence());
        return panel;
    }

    private Control QuestionnaireStage()
    {
        var panel = Stack(8);
        panel.Children.Add(Status(T("أداة توضيحية غير معتمدة، واستجاباتها اصطناعية بالكامل", "Illustrative, non-validated instrument with entirely synthetic responses"), "#9A650E"));
        panel.Children.Add(MetricStrip(
            (T("الاستبيانات", "Questionnaires"), _questionnaires.Count.ToString()),
            (T("البنود", "Items"), (_questionnaires.FirstOrDefault()?.Versions.LastOrDefault()?.Questions.Count ?? 0).ToString()),
            (T("الاستجابات المكتملة", "Completed responses"), _questionnaireResponses.Count(item => item.Status == "Completed").ToString())));
        var version = _questionnaires.FirstOrDefault()?.Versions.LastOrDefault();
        if (version is not null)
            foreach (var item in version.Questions.OrderBy(item => item.SortOrder))
                panel.Children.Add(Row(item.QuestionText,
                    T("ليكرت مرتب من 1 إلى 5، وتحفظ الإجابة الخام", "Ordered Likert 1–5; raw response preserved"),
                    item.QuestionType));
        if (version?.Scales.FirstOrDefault() is { } scale)
            panel.Children.Add(Row(scale.Name,
                T($"متوسط حتمي بحد أدنى قدره {scale.MinimumAnsweredItems} من البنود، مع حفظ البند المعكوس كقاعدة", $"Deterministic mean; minimum {scale.MinimumAnsweredItems} items; reverse-coded item retained as metadata"),
                "COMPOSITE"));
        return panel;
    }

    private Control AnalysisStage()
    {
        var panel = Stack(9);
        panel.Children.Add(Status(T("عرض اصطناعي للحساب وليس دليلا تجريبيا", "SYNTHETIC DEMONSTRATION — not empirical evidence"), "#2563EB"));
        if (_analysisDataset is null || _dataQuality is null || _analysisExecution?.Result is null)
        {
            panel.Children.Add(Row(T("حالة المحرك", "Engine status"),
                T("لا توجد نتيجة محسوبة متاحة", "No computed result is available"),
                _analysisExecution?.Status ?? AnalysisStatuses.InsufficientData));
            return panel;
        }
        var result = _analysisExecution.Result;
        panel.Children.Add(MetricStrip(
            (T("العدد الكلي", "Total N"), _dataQuality.TotalN.ToString()),
            (T("المضمن", "Included"), _dataQuality.IncludedN.ToString()),
            (T("المستبعد", "Excluded"), _dataQuality.ExcludedN.ToString()),
            (T("المجموعات", "Groups"), result.GroupNs.Count.ToString())));
        panel.Children.Add(Row(T("جودة البيانات", "Data quality"),
            T($"قيم مفقودة { _dataQuality.MissingByVariable.Values.Sum() } • جلسات غير مكتملة {_dataQuality.SessionWarnings.Count}",
                $"Missing values {_dataQuality.MissingByVariable.Values.Sum()} • incomplete sessions {_dataQuality.SessionWarnings.Count}"),
            _dataQuality.SessionWarnings.Count == 0 ? "READY" : "WARNING"));
        panel.Children.Add(Row(result.Method,
            T($"الإحصائية {Format(result.Statistic)} • p {FormatP(result.PValue)} • درجات الحرية {Format(result.DegreesOfFreedom)}",
                $"Statistic {Format(result.Statistic)} • p {FormatP(result.PValue)} • df {Format(result.DegreesOfFreedom)}"),
            result.Status));
        if (result.EffectSize is not null)
            panel.Children.Add(Row(T("حجم الأثر", "Effect size"),
                $"{result.EffectSize.Method} = {Format(result.EffectSize.Value)} • {result.EffectSize.Definition}",
                result.EffectSize.Method));
        panel.Children.Add(Row(T("قابلية الإعادة", "Reproducibility"),
            $"{ScientificEngineMetadata.Version} • SHA-256 {_analysisDataset.DatasetHash[..16]}… • seed {DemoScientificDataService.GeneratorSeed}",
            "PROVENANCE"));
        foreach (var diagnostic in result.Diagnostics)
            panel.Children.Add(Row(diagnostic.Code, diagnostic.Message, diagnostic.Severity));
        return panel;
    }

    private Control SessionsStage()
    {
        var panel = Stack(10);
        panel.Children.Add(MetricStrip(
            (T("مشاركون اصطناعيون", "Synthetic participants"), _participants.Count.ToString()),
            (T("جلسات اصطناعية", "Synthetic sessions"), _sessions.Count.ToString()),
            (T("مواد في كل عرض", "Items per presentation"), _content.Count.ToString())));
        panel.Children.Add(ThreeLayerEvidence());
        return panel;
    }

    private Control ThreeLayerEvidence()
    {
        var panel = Stack(6);
        panel.Children.Add(Row(T("حقيقة المصدر", "Source truth"), T("قيم مرصودة ووقت التقاط ومصدر", "Observed values, capture time, and provenance"), "01"));
        panel.Children.Add(Row(T("العرض التجريبي", "Experimental presentation"), T("شرط ومعالجة وترتيب محفوظ في لقطة ثابتة", "Condition, manipulation, and order preserved in an immutable snapshot"), "02"));
        panel.Children.Add(Row(T("السلوك المرصود", "Observed behavior"), T("تعرض وتفاعل وتوقيت واحداث خام", "Exposure, interaction, timing, and raw events"), "03"));
        return panel;
    }

    private Control PhysiologyReadiness()
    {
        var panel = Stack(6);
        panel.Children.Add(new TextBlock { Text = T("طبقة فسيولوجية مستقبلية", "Future physiological layer"), Classes = { "sectionTitle" } });
        foreach (var card in FuturePhysiologyPresentationService.Cards(LocalizationService.IsArabic))
            panel.Children.Add(Row(card.Measurement, card.Ecosystem + " · " + T("لا يوجد جهاز متصل حاليا", "No device connected"), T("تكامل مستقبلي", "FUTURE INTEGRATION")));
        return panel;
    }

    private Control ReportPreviewStage()
    {
        var panel = Stack(9);
        panel.Children.Add(Status(T("بيانات تجريبية اصطناعية فقط", "Synthetic demo data only"), "#2563EB"));
        var result = _analysisExecution?.Result;
        panel.Children.Add(Row(T("نظرة عامة على الدراسة", "Study overview"), _study?.Title ?? T("دراسة العرض", "Demo study"), "STUDY"));
        panel.Children.Add(Row(T("العينة والاكتمال", "Sample and completion"), T($"{_dataQuality?.IncludedN ?? 0} جلسة مكتملة مؤهلة من {_dataQuality?.TotalN ?? 0}", $"{_dataQuality?.IncludedN ?? 0} completed eligible sessions of {_dataQuality?.TotalN ?? 0}"), "SAMPLE"));
        panel.Children.Add(Row(T("الشروط", "Conditions"), string.Join(" • ", _groups.Select(group => group.Name)), "GROUPS"));
        if (result is not null)
            panel.Children.Add(Row(T("النتيجة الإحصائية", "Statistical result"), $"{result.Method} • p {FormatP(result.PValue)} • N {result.N}", "ANALYSIS"));
        panel.Children.Add(Row(T("السلوك المرصود", "Observed behavior"), T("تعرض مؤهل وتفاعلات ومدة جلسة، دون استنتاجات نفسية", "Qualified exposures, interactions, and session duration; no psychological claim"), "BEHAVIOR"));
        panel.Children.Add(Row(T("المنهج وقابلية التتبع", "Methods and provenance"), $"{ScientificEngineMetadata.Version} • SHA-256 {_analysisDataset?.DatasetHash[..Math.Min(16, _analysisDataset.DatasetHash.Length)] ?? "—"}…", "PROVENANCE"));
        return panel;
    }

    private Control AiIllustrativeStage()
    {
        var panel = Stack(9);
        panel.Children.Add(Status(T("معاينة توضيحية لـ SOCYVIA AI مبنية على بيانات تجريبية اصطناعية", "SOCYVIA AI illustrative preview based on synthetic demo data"), "#2563EB"));
        var result = _analysisExecution?.Result;
        panel.Children.Add(Row(T("النتائج المرصودة", "Observed results"), T($"تتضمن العينة {_dataQuality?.IncludedN ?? 0} جلسة مكتملة مؤهلة عبر {_groups.Count} مجموعات.", $"The fixture contains {_dataQuality?.IncludedN ?? 0} completed eligible sessions across {_groups.Count} groups."), "OBSERVED"));
        panel.Children.Add(Row(T("الدليل الإحصائي", "Statistical evidence"), result is null ? T("لا توجد نتيجة محسوبة متاحة.", "No computed result is available.") : $"{result.Method} • p {FormatP(result.PValue)} • N {result.N}", "STATISTICAL"));
        panel.Children.Add(Row(T("التفسير", "Interpretation"), T("هذا العرض يوضح كيف يقدم SOCYVIA تفسيرا حذرا لنتائج محسوبة؛ ولا يثبت سببية أو انتباها نفسيا.", "This preview shows how SOCYVIA can cautiously interpret computed results; it does not establish causality or psychological attention."), "INTERPRETATION"));
        panel.Children.Add(Row(T("جودة البيانات والقيود", "Data quality and cautions"), T("البيانات اصطناعية للعرض فقط ولا تمثل دليلا تجريبيا.", "The records are synthetic and illustrative only; they are not empirical evidence."), "CAUTION"));
        return panel;
    }

    private Control FutureStage(string label, string detail)
    {
        var panel = Stack(12);
        panel.Children.Add(new Border
        {
            Classes = { "emptyState" },
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = label, Classes = { "eyebrow" }, TextAlignment = TextAlignment.Center },
                    new TextBlock { Text = detail, MaxWidth = 480, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = Brush("#52647C") },
                    new Border { Classes = { "badge", "info" }, Child = new TextBlock { Text = T("معاينة • قادم في المحرك العلمي", "PREVIEW • COMING IN THE SCIENTIFIC ENGINE"), FontSize = 8, TextAlignment = TextAlignment.Center } }
                }
            }
        });
        return panel;
    }

    private async Task OpenPreviewAsync()
    {
        if (_study is null || _previewGroup is null || _previewCondition is null) return;
        try
        {
            await BrowserParticipantPreviewService.OpenAsync(_study, _previewGroup, _previewCondition);
        }
        catch (BrowserParticipantPreviewException exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Open guided demo participant preview");
            StageDescriptionText.Text = exception.Failure switch
            {
                BrowserParticipantPreviewFailure.LocalMediaUnavailable => T("تعذر العثور على أحد ملفات الوسائط المحلية", "A local media file could not be found"),
                BrowserParticipantPreviewFailure.AssetsUnavailable => T("تعذر العثور على ملفات واجهة المعاينة", "The preview interface files could not be found"),
                BrowserParticipantPreviewFailure.LocalHostUnavailable => T("تعذر تشغيل خادم المعاينة المحلي", "The local preview server could not be started"),
                BrowserParticipantPreviewFailure.BrowserLaunchUnavailable => T("تعذر فتح المتصفح الافتراضي", "The default browser could not be opened"),
                _ => T("تعذر تجهيز معاينة الدراسة", "The study could not be prepared for preview")
            };
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Open guided demo participant preview");
            StageDescriptionText.Text = T("تعذر فتح المتصفح الافتراضي", "The default browser could not be opened");
        }

    }

    private ExperimentalCondition? ConditionFor(StudyGroup? group) =>
        group is null ? null : _conditions.FirstOrDefault(item => item.GroupId == group.Id);

    private static StackPanel Stack(double spacing) => new() { Spacing = spacing };

    private static Control Labeled(string label, Control control) => new StackPanel
    {
        Spacing = 5,
        Children = { new TextBlock { Text = label, Classes = { "metadata" } }, control }
    };

    private static Control Status(string message, string color) => new StatusIndicatorView(message, color);

    private static Border Row(string title, string detail, string badge)
    {
        var titleIsArabic = ContainsArabic(title);
        var detailIsArabic = ContainsArabic(detail);
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("#263750"),
            TextWrapping = TextWrapping.Wrap,
            FlowDirection = titleIsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            TextAlignment = titleIsArabic ? TextAlignment.Right : TextAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var detailText = new TextBlock
        {
            Text = detail,
            FontSize = 8.3,
            Foreground = Brush("#6C7A8E"),
            TextWrapping = TextWrapping.Wrap,
            FlowDirection = detailIsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            TextAlignment = detailIsArabic ? TextAlignment.Right : TextAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var rtl = titleIsArabic || detailIsArabic;
        var grid = new Grid { ColumnDefinitions = rtl ? new ColumnDefinitions("Auto,16,*") : new ColumnDefinitions("*,16,Auto") };
        var textColumn = rtl ? 2 : 0;
        var badgeColumn = rtl ? 0 : 2;
        var textPanel = new StackPanel
        {
            Spacing = 3, HorizontalAlignment = HorizontalAlignment.Stretch,
            Children = { titleText, detailText }
        };
        Grid.SetColumn(textPanel, textColumn);
        grid.Children.Add(textPanel);
        var badgeControl = Badge(badge);
        Grid.SetColumn(badgeControl, badgeColumn);
        grid.Children.Add(badgeControl);
        return new Border { Classes = { "researchRow" }, Child = grid };
    }

    private static Border Badge(string text)
    {
        var border = new Border
        {
            Classes = { "badge" },
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 7.5, TextAlignment = TextAlignment.Center, Foreground = Brush("#2563EB") }
        };
        return border;
    }

    private static Control MetricStrip(params (string Label, string Value)[] metrics)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(',', Enumerable.Repeat("*", metrics.Length))), ColumnSpacing = 8 };
        for (var index = 0; index < metrics.Length; index++)
        {
            var metric = metrics[index];
            var card = new Border
            {
                Classes = { "denseSurface" }, Padding = new Thickness(14, 10),
                Child = new StackPanel
                {
                    Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = metric.Value, FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Brush("#10213B"), TextAlignment = TextAlignment.Center },
                        new TextBlock { Text = metric.Label, Classes = { "metadata" }, TextAlignment = TextAlignment.Center }
                    }
                }
            };
            Grid.SetColumn(card, index);
            grid.Children.Add(card);
        }
        return grid;
    }

    private static void SetButton(Button button, string text) => button.Content = new TextBlock
    {
        Text = text,
        TextAlignment = LocalizationService.IsArabic ? TextAlignment.Right : TextAlignment.Left,
        HorizontalAlignment = LocalizationService.IsArabic ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        FontSize = 9
    };

    private static FontFamily Font(bool arabic) => new(arabic
        ? "avares://SOCYVIA/Assets/Fonts#IBM Plex Sans Arabic"
        : "avares://SOCYVIA/Assets/Fonts#IBM Plex Sans");

    private static IBrush Brush(string value) => new SolidColorBrush(Color.Parse(value));
    private static bool ContainsArabic(string value) => value.Any(character =>
        character is >= '\u0600' and <= '\u06FF' or >= '\u0750' and <= '\u077F');
    private static string T(string arabic, string english) => UiTextService.Localized(arabic, english);
    private static string Format(double? value) => value.HasValue && double.IsFinite(value.Value)
        ? value.Value.ToString("0.####", CultureInfo.InvariantCulture)
        : "—";
    private static string FormatP(double? value) => !value.HasValue
        ? "—"
        : value.Value < 0.001
            ? "< 0.001"
            : value.Value.ToString("0.###", CultureInfo.InvariantCulture);

    private enum DemoStage
    {
        Content,
        Design,
        Groups,
        Conditions,
        Participants,
        Experiment,
        Experience,
        Questionnaire,
        Sessions,
        Analysis,
        Report,
        Ai
    }
}
