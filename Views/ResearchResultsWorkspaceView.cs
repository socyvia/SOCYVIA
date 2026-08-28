using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SOCYVIA.Models;
using SOCYVIA.Repositories;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

/// <summary>
/// Study-scoped researcher results workspace. It only reads the normalized local mirror;
/// Cloudflare details remain inside the synchronization service.
/// </summary>
public sealed class ResearchResultsWorkspaceView : UserControl
{

    private readonly Study _study;
    private const double ResultsToolbarHeight = 42;
    private readonly ComboBox _group = new()
    {
        MinWidth = 160,
        Height = ResultsToolbarHeight,
        MinHeight = ResultsToolbarHeight,
        Padding = new Thickness(14, 0),
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1),
        VerticalAlignment = VerticalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _syncState = new() { Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _content = new() { Spacing = 14 };
    private readonly RemoteResearchDashboardService _dashboard = new();
    private readonly LatestUiRenderGate _renderGate = new();
    private List<StudyGroup> _groups = [];
    private List<ExperimentalCondition> _conditions = [];
    private string _section = "overview";
    private ResearchReportDocument? _report;
    private bool _localizationSubscribed;

    public ResearchResultsWorkspaceView(Study study)
    {
        _study = study;
        FlowDirection = LocalizationService.IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var root = new StackPanel { Spacing = 16 };
        root.Children.Add(Header());
        root.Children.Add(Navigation());
        root.Children.Add(_content);
        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = root
        };
        _group.SelectionChanged += async (_, _) => await RenderAsync();
        AttachedToVisualTree += async (_, _) =>
        {
            if (!_localizationSubscribed)
            {
                LocalizationService.LanguageChanged += OnLanguageChanged;
                _localizationSubscribed = true;
            }
            await ReloadAsync();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (!_localizationSubscribed) return;
            LocalizationService.LanguageChanged -= OnLanguageChanged;
            _localizationSubscribed = false;
        };
    }

    private void OnLanguageChanged(object? sender, EventArgs eventArgs) =>
        Dispatcher.UIThread.Post(async () => await RenderAsync());

    public async Task ReloadAsync()
    {
        _groups = await GroupRepository.GetByStudyAsync(_study.Id);
        _conditions = await ExperimentalConditionRepository.GetByStudyAsync(_study.Id);
        var selected = (_group.SelectedItem as Choice)?.Id;
        _group.ItemsSource = new[] { new Choice(null, T("كل المجموعات", "All groups")) }
            .Concat(_groups.OrderBy(x => x.SortOrder).Select(x => new Choice(x.Id, x.Name))).ToArray();
        _group.SelectedItem = (_group.ItemsSource as IEnumerable<Choice>)?.FirstOrDefault(x => x.Id == selected) ?? (_group.ItemsSource as IEnumerable<Choice>)?.First();
        await RenderAsync();
    }

    public async Task OpenSectionAsync(string section)
    {
        if (section is not ("overview" or "participants" or "behavior" or "pre" or "post" or "comparison" or "dictionary" or "exports" or "report" or "ai"))
            throw new ArgumentOutOfRangeException(nameof(section));
        _section = section;
        await ReloadAsync();
    }

    private Control Header()
    {
        var title = new TextBlock { Text = T("نتائج البحث", "Research Results"), Classes = { "pageTitle" } };
        var subtitle = new TextBlock { Text = T("بيانات متزامنة ومؤهلة للتحليل؛ لا توجد أرقام نموذجية.", "Synchronized, provenance-preserving research records. No example metrics are shown."), Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap };
        var sync = Button(T("مزامنة البيانات البعيدة", "Sync Remote Data"), true);
        sync.Height = ResultsToolbarHeight;
        sync.MinHeight = ResultsToolbarHeight;
        sync.Padding = new Thickness(14, 0);
        sync.CornerRadius = new CornerRadius(8);
        sync.BorderThickness = new Thickness(1);
        sync.BorderBrush = new SolidColorBrush(Color.Parse("#2563EB"));
        sync.VerticalAlignment = VerticalAlignment.Center;
        sync.VerticalContentAlignment = VerticalAlignment.Center;
        sync.Click += async (_, _) => await SyncAsync(sync);
        // One explicit action cluster owns both controls.  They therefore have
        // a shared vertical box rather than independent margin/style geometry.
        var actions = new Grid
        {
            Height = ResultsToolbarHeight,
            ColumnDefinitions = new ColumnDefinitions("Auto,10,Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(_group);
        Grid.SetColumn(sync, 2);
        actions.Children.Add(sync);
        var grid = new Grid
        {
            MinHeight = ResultsToolbarHeight,
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        var words = new StackPanel { Spacing = 3, Children = { title, subtitle, _syncState } };
        grid.Children.Add(words);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return Surface(grid, 20);
    }

    private Control Navigation()
    {
        var nav = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var item in new[] { ("overview", T("نظرة عامة", "Overview")), ("participants", T("المشاركون", "Participants")), ("behavior", T("السلوك", "Behavior")), ("pre", T("قبل التجربة", "Pre-questionnaire")), ("post", T("بعد التجربة", "Post-questionnaire")), ("comparison", T("مقارنة المجموعات", "Group Comparison")), ("dictionary", T("قاموس البيانات", "Data Dictionary")), ("exports", T("التصدير", "Exports")), ("report", T("التقرير", "Report")), ("ai", "SOCYVIA AI") })
        {
            var button = Button(item.Item2);
            button.Margin = new Thickness(0, 0, 8, 8);
            button.Click += async (_, _) => { _section = item.Item1; await RenderAsync(); };
            nav.Children.Add(button);
        }
        return nav;
    }

    private async Task SyncAsync(Button button)
    {
        button.IsEnabled = false; _syncState.Text = T("جار المزامنة...", "Syncing...");
        try
        {
            var config = await new CloudflareProviderConfigurationStore().LoadAsync();
            var token = config is null ? null : await new CloudflareOAuthConnectionService().GetAccessTokenAsync(
                config, CloudflareOAuthClientConfiguration.LoadReleaseConfiguration());
            if (config is null || string.IsNullOrWhiteSpace(token) || !config.HasRequiredTextRuntimeIdentity) throw new InvalidOperationException();
            await new RemoteResearchSynchronizationService().SynchronizeAsync(config, token, await RemoteResearchRepository.GetCursorAsync());
            _syncState.Text = T("محدثة", "Up to date"); await RenderAsync();
        }
        catch { _syncState.Text = T("تعذرت المزامنة. تحقق من اتصال السحابة ثم أعد المحاولة.", "Synchronization failed. Check the cloud connection and retry."); }
        finally { button.IsEnabled = true; }
    }

    private Task RenderAsync() => _renderGate.RunAsync(RenderCoreAsync);

    private async Task RenderCoreAsync()
    {
        _content.Children.Clear();
        var sessions = (await RemoteResearchRepository.GetSessionsAsync(studyId: _study.Id)).Where(InSelectedGroup).ToArray();
        switch (_section)
        {
            case "participants": RenderParticipants(sessions); break;
            case "behavior": await RenderBehaviorAsync(); break;
            case "pre": await RenderQuestionnaireAsync(QuestionnaireStage.Pre); break;
            case "post": await RenderQuestionnaireAsync(QuestionnaireStage.Post); break;
            case "comparison": await RenderComparisonAsync(); break;
            case "dictionary": await RenderDataDictionaryAsync(); break;
            case "exports": RenderExports(); break;
            case "report": await RenderReportAsync(); break;
            case "ai": await RenderAiWorkspaceAsync(); break;
            default: RenderOverview(sessions); break;
        }
    }

    private void RenderOverview(IReadOnlyList<RemoteParticipantSessionContract> sessions)
    {
        var completed = sessions.Where(x => x.CompletionState == RemoteParticipantCompletionState.CompletedEligible).ToArray();
        var duration = completed.Where(x => x.StartedAtUtc.HasValue && x.CompletedAtUtc.HasValue).Select(x => (x.CompletedAtUtc!.Value - x.StartedAtUtc!.Value).TotalSeconds).ToArray();
        if (sessions.Count == 0) { Empty(T("لا توجد بيانات مشاركين بعيدة بعد.", "No remote participants yet.")); return; }
        var cards = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        cards.Children.Add(Metric(T("بدأوا", "Started"), sessions.Count.ToString()));
        cards.Children.Add(Metric(T("مكتملون", "Completed"), completed.Length.ToString()));
        cards.Children.Add(Metric(T("غير مكتملين", "Incomplete"), (sessions.Count - completed.Length).ToString()));
        cards.Children.Add(Metric(T("معدل الإكمال", "Completion rate"), $"{completed.Length * 100d / sessions.Count:0.0}%"));
        cards.Children.Add(Metric(T("متوسط مدة الجلسة المكتملة", "Mean completed session duration"), duration.Length == 0 ? "—" : $"{duration.Average():0.0}s"));
        _content.Children.Add(cards);
        _content.Children.Add(Surface(new TextBlock { Text = T("العينة التحليلية الافتراضية: المشاركون المكتملون والمؤهلون. تبقى الجلسات غير المكتملة متاحة لمراجعة الانسحاب والجودة.", "Default analytical sample: completed eligible sessions. Incomplete sessions remain available for attrition and quality review."), TextWrapping = TextWrapping.Wrap, Classes = { "metadata" } }));
    }

    private void RenderParticipants(IReadOnlyList<RemoteParticipantSessionContract> sessions)
    {
        if (sessions.Count == 0) { Empty(T("لا توجد جلسات مطابقة للمرشح المحدد.", "No participant sessions match the selected filter.")); return; }
        var list = new StackPanel { Spacing = 6 };
        list.Children.Add(Row(true, T("المشارك", "Participant"), T("المجموعة", "Group"), T("بدأ", "Started"), T("نهاية الخلاصة", "Feed end"), T("بعد", "Post"), T("المدة", "Duration"), T("الحالة", "Status")));
        foreach (var s in sessions)
        {
            var duration = s.StartedAtUtc.HasValue && s.CompletedAtUtc.HasValue ? $"{(s.CompletedAtUtc.Value - s.StartedAtUtc.Value).TotalMinutes:0.0}m" : "—";
            var detail = new Button { Content = Row(false, Short(s.ParticipantId), GroupName(s.GroupId), Date(s.StartedAtUtc), Date(s.FeedEndedAtUtc), Date(s.PostQuestionnaireCompletedAtUtc), duration, s.CompletionState == RemoteParticipantCompletionState.CompletedEligible ? T("مكتمل", "Completed") : T("غير مكتمل", "Incomplete")), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            detail.Classes.Add("secondary"); detail.Click += async (_, _) => await ShowParticipantDetailAsync(s); list.Children.Add(detail);
        }
        _content.Children.Add(Surface(list));
    }

    private async Task ShowParticipantDetailAsync(RemoteParticipantSessionContract session)
    {
        var events = await RemoteResearchRepository.GetEventsAsync(session.ConditionId, studyId: _study.Id).ConfigureAwait(true);
        var pre = await RemoteResearchRepository.GetQuestionnaireResponsesAsync(QuestionnaireStage.Pre, session.ConditionId, studyId: _study.Id).ConfigureAwait(true);
        var post = await RemoteResearchRepository.GetQuestionnaireResponsesAsync(QuestionnaireStage.Post, session.ConditionId, studyId: _study.Id).ConfigureAwait(true);
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = T("تفاصيل جلسة المشارك", "Participant session detail"), Classes = { "sectionTitle" } });
        panel.Children.Add(new TextBlock { Text = $"{T("المشارك", "Participant")}: {Short(session.ParticipantId)}\n{T("المجموعة", "Group")}: {GroupName(session.GroupId)}\n{T("الحالة", "Status")}: {CompletionLabel(session.CompletionState)}\n{T("دورة الحياة", "Lifecycle")}: {LifecycleLabel(session.LifecycleState)}", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"{T("استجابات قبل التجربة", "PRE responses")}: {pre.Count(x => x.ParticipantId == session.ParticipantId)}\n{T("نشاط سلوكي", "Behavioral events")}: {events.Count(x => x.SessionId == session.SessionId)}\n{T("استجابات بعد التجربة", "POST responses")}: {post.Count(x => x.SessionId == session.SessionId)}", Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = T("يعرض هذا الملخص سجلات بحثية موحدة البنية؛ ولا تظهر البيانات الخام أو الأسرار هنا.", "This summary uses normalized research records; raw JSON and secrets are not exposed here."), Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap });
        var window = new Window { Title = T("تفاصيل المشارك", "Participant detail"), Width = 560, Height = 390, Content = panel };
        if (TopLevel.GetTopLevel(this) is Window owner) await window.ShowDialog(owner); else window.Show();
    }

    private async Task RenderBehaviorAsync()
    {
        var events = (await RemoteResearchRepository.GetEventsAsync(null, true, _study.Id)).Where(InSelectedGroupEvent).ToArray();
        if (events.Length == 0) { Empty(T("لا تتوفر أحداث سلوكية للعينة المكتملة المحددة.", "No behavioral events are available for the selected completed sample.")); return; }
        var labels = new Dictionary<string, string> { ["content_impression"] = T("مرات التعرض المؤهل", "Qualified impressions"), ["content_open"] = T("فتح المحتوى", "Content opens"), ["read_more_open"] = T("اقرأ المزيد", "Read More"), ["like"] = T("إعجابات", "Likes"), ["comment_submit"] = T("تعليقات", "Comments"), ["save"] = T("حفظ", "Saves"), ["share"] = T("عمليات المشاركة", "Shares"), ["experiment_feed_end"] = T("نهاية الخلاصة", "Feed-end reached") };
        var rows = new StackPanel { Spacing = 7 };
        foreach (var pair in labels) rows.Children.Add(Metric(pair.Value, events.Count(x => x.EventType == pair.Key).ToString()));
        _content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { rows } });
        var byContent = events.Where(x => !string.IsNullOrWhiteSpace(x.ContentId)).GroupBy(x => x.ContentId!).OrderBy(x => x.Key);
        var content = new StackPanel { Spacing = 6 }; content.Children.Add(new TextBlock { Text = T("حسب المحتوى", "By content"), Classes = { "sectionTitle" } });
        foreach (var item in byContent) content.Children.Add(new TextBlock { Text = $"{item.Key}: {item.Count(x => x.EventType == "content_impression")} {T("مرات تعرض مؤهل", "qualified impressions")} · {item.Count(x => x.EventType == "content_open")} {T("عملية فتح", "opens")}", TextWrapping = TextWrapping.Wrap });
        _content.Children.Add(Surface(content));
    }

    private async Task RenderQuestionnaireAsync(QuestionnaireStage stage)
    {
        var rows = (await RemoteResearchRepository.GetQuestionnaireResponsesAsync(stage, null, true, _study.Id)).Where(InSelectedGroupQuestionnaire).ToArray();
        if (rows.Length == 0) { Empty(stage == QuestionnaireStage.Pre ? T("لم يتم تكوين أو مزامنة استبيان قبل التجربة بعد.", "No pre-questionnaire responses have been synchronized yet.") : T("لم تتم مزامنة استجابات بعد التجربة بعد.", "No post-questionnaire responses have been synchronized yet.")); return; }
        var answers = new Dictionary<string, List<string>>();
        foreach (var row in rows) try { using var doc = JsonDocument.Parse(row.ResponseJson); foreach (var item in doc.RootElement.EnumerateObject()) { if (!answers.TryGetValue(item.Name, out var list)) answers[item.Name] = list = []; list.Add(item.Value.ToString()); } } catch (JsonException) { }
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = stage == QuestionnaireStage.Pre ? T("نتائج قبل التجربة", "Pre-questionnaire results") : T("نتائج بعد التجربة", "Post-questionnaire results"), Classes = { "sectionTitle" } });
        foreach (var answer in answers.OrderBy(x => x.Key))
        {
            var numeric = answer.Value.Select(x => double.TryParse(x, out var n) ? (double?)n : null).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
            var detail = numeric.Length == answer.Value.Count ? $"n={numeric.Length}; mean={numeric.Average():0.##}" : $"n={answer.Value.Count}; {string.Join(" · ", answer.Value.GroupBy(x => x).OrderByDescending(x => x.Count()).Take(4).Select(x => $"{x.Key}: {x.Count()}"))}";
            panel.Children.Add(Surface(new TextBlock { Text = $"{answer.Key}  —  {detail}", TextWrapping = TextWrapping.Wrap }));
        }
        _content.Children.Add(panel);
    }

    private async Task RenderComparisonAsync()
    {
        if (_conditions.Count == 0) { Empty(T("لم يتم إعداد أي مجموعات أو شروط بعد.", "No groups or conditions are configured yet.")); return; }
        var table = new StackPanel { Spacing = 7 }; table.Children.Add(Row(true, T("الشرط", "Condition"), T("بدأوا", "Started"), T("مكتملون", "Completed"), T("غير مكتملين", "Incomplete"), T("معدل الإكمال", "Completion rate"), T("متوسط المدة", "Mean duration")));
        foreach (var condition in _conditions.Where(InSelectedGroupCondition).OrderBy(x => x.SortOrder))
        {
            var metric = await _dashboard.GetMetricsAsync(condition.Id);
            table.Children.Add(Row(false, condition.Name, metric.Started.ToString(), metric.Completed.ToString(), metric.Incomplete.ToString(), $"{metric.CompletionRatePercent:0.0}%", metric.MeanCompletedDurationSeconds is null ? "—" : $"{metric.MeanCompletedDurationSeconds:0.0}s"));
        }
        _content.Children.Add(Surface(table));
        _content.Children.Add(Surface(new TextBlock { Text = T("هذه مقارنة وصفية فقط؛ لا تعرض دلالة إحصائية أو استنتاجات سببية.", "This is descriptive comparison only; it does not show statistical significance or causal conclusions."), Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap }));
    }

    private async Task RenderDataDictionaryAsync()
    {
        var entries = await ResearchDataDictionaryService.ForStudyAsync(_study, LocalizationService.IsArabic);
        if (entries.Count == 0)
        {
            Empty(T("لا توجد متغيرات موثقة بعد.", "No documented variables are available yet."));
            return;
        }

        var table = new StackPanel { Spacing = 6 };
        table.Children.Add(Row(true,
            T("الاسم", "Display Name"), T("معرف المتغير", "Variable ID"),
            T("الفئة", "Class"), T("النوع", "Type"), T("المرحلة", "Stage"),
            T("المصدر", "Source"), T("المفقود", "Missing value"), T("الأصل", "Provenance")));
        foreach (var entry in entries)
            table.Children.Add(Row(false,
                entry.DisplayName, entry.VariableId, entry.VariableClass, entry.Type,
                entry.Stage, entry.Source, entry.MissingValueMeaning,
                entry.RunTypeProvenance ?? entry.EligibilityNote));

        var description = new TextBlock
        {
            Text = T(
                "تظهر صياغة السؤال التي كتبها الباحث كاسم أساسي، بينما تبقى المعرفات الداخلية ونسخة الاستبيان بيانات وصفية قابلة للتتبع.",
                "Researcher-authored question wording is the primary label; internal IDs and questionnaire versions remain traceable metadata."),
            Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap
        };
        _content.Children.Add(Surface(new StackPanel { Spacing = 10, Children = { description, new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = table } } }));
    }

    private void RenderExports()
    {
        var wrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        AddExport(wrap, T("مجموعة البيانات التحليلية المكتملة", "Completed Analytical Dataset"), "completed_dataset", () => RemoteResearchExportService.ExportCompletedAnalyticalDatasetCsvAsync(SelectedCondition(), _study.Id));
        AddExport(wrap, T("جلسات المشاركين", "Participant Sessions"), "participant_sessions", () => RemoteResearchExportService.ExportSessionsCsvAsync(SelectedCondition(), _study.Id));
        AddExport(wrap, T("استجابات قبل التجربة", "Pre-questionnaire Responses"), "pre_responses", () => RemoteResearchExportService.ExportQuestionnaireCsvAsync(QuestionnaireStage.Pre, SelectedCondition(), false, _study.Id));
        AddExport(wrap, T("استجابات بعد التجربة", "Post-questionnaire Responses"), "post_responses", () => RemoteResearchExportService.ExportQuestionnaireCsvAsync(QuestionnaireStage.Post, SelectedCondition(), false, _study.Id));
        AddExport(wrap, T("الأحداث السلوكية", "Behavioral Events"), "behavioral_events", () => RemoteResearchExportService.ExportBehavioralEventsCsvAsync(SelectedCondition(), false, _study.Id));
        AddExport(wrap, T("الجلسات غير المكتملة", "Incomplete Sessions"), "incomplete_sessions", () => RemoteResearchExportService.ExportIncompleteSessionsCsvAsync(SelectedCondition(), _study.Id));
        var dictionaryCsv = Button(T("قاموس البيانات (CSV)", "Data Dictionary (CSV)"));
        dictionaryCsv.Click += async (_, _) =>
        {
            var entries = await ResearchDataDictionaryService.ForStudyAsync(_study, LocalizationService.IsArabic);
            await SaveTextAsync("data_dictionary", "csv", ResearchDataDictionaryService.Csv(entries));
        };
        var dictionaryJson = Button(T("قاموس البيانات (JSON)", "Data Dictionary (JSON)"));
        dictionaryJson.Click += async (_, _) =>
        {
            var entries = await ResearchDataDictionaryService.ForStudyAsync(_study, LocalizationService.IsArabic);
            await SaveTextAsync("data_dictionary", "json", ResearchDataDictionaryService.Json(entries));
        };
        wrap.Children.Add(dictionaryCsv);
        wrap.Children.Add(dictionaryJson);
        _content.Children.Add(Surface(new StackPanel { Spacing = 8, Children = { new TextBlock { Text = T("مركز التصدير", "Export Center"), Classes = { "sectionTitle" } }, new TextBlock { Text = T("تتضمن مجموعة البيانات التحليلية الجلسات المكتملة والمؤهلة فقط. تصدر الجلسات غير المكتملة بشكل منفصل.", "The analytical dataset contains completed eligible sessions only. Incomplete sessions export separately."), Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap }, wrap } }));
    }

    private async Task RenderReportAsync()
    {
        var dataset = await RemoteAnalysisDatasetService.BuildCompletedEligibleAsync(_study.Id);
        var quality = DataQualityService.Evaluate(dataset);
        var specifications = await AnalysisRepository.GetSpecificationsAsync(_study.Id, false);
        var executions = new List<AnalysisExecution>();
        foreach (var specification in specifications)
        {
            var execution = await AnalysisRepository.GetLatestExecutionAsync(specification.Id);
            if (execution is not null) executions.Add(execution);
        }
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = T("منشئ التقرير", "Report Builder"), Classes = { "sectionTitle" } });
        panel.Children.Add(new TextBlock { Text = T("يتم إنشاء التقرير من النتائج الحتمية المحفوظة، وليس من نص واجهة المستخدم أو استنتاجات مولدة.", "The report is built from saved deterministic results, not UI text or generated findings."), Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap });
        var choices = new[] { "Study Overview", "Sample / Participation", "Data Quality", "Statistical Analyses" }.Select(name => new CheckBox { Content = name, IsChecked = true }).ToArray();
        foreach (var choice in choices) panel.Children.Add(choice);
        ResearchFigure? figure = null;
        var build = Button(T("إنشاء معاينة التقرير", "Build report preview"), true);
        var export = Button(T("تصدير التقرير بصيغة Markdown", "Export report as Markdown")); export.IsEnabled = false;
        var exportFigure = Button(T("تصدير شكل (SVG)", "Export figure (SVG)")); exportFigure.IsEnabled = false;
        var ai = Button("SOCYVIA AI");
        var result = new TextBlock { Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap };
        build.Click += (_, _) => { _report = ResearchReportService.Build(_study, dataset, quality, executions, choices.Where(choice => choice.IsChecked == true).Select(choice => Convert.ToString(choice.Content)!).ToArray()); var variable = dataset.Variables.FirstOrDefault(item => item.DataType == "Double" && dataset.Rows.Any(row => row.NumericValues.GetValueOrDefault(item.Id).HasValue)); figure = variable is null ? null : ResearchFigureService.CreateGroupedMeanFigure(dataset, variable.Id); result.Text = _report.Markdown + (figure is null ? string.Empty : $"\n\nFigure prepared: {figure.Title}"); export.IsEnabled = true; exportFigure.IsEnabled = figure is not null; };
        export.Click += async (_, _) => { if (_report is not null) await SaveTextAsync("research_report", "md", _report.Markdown); };
        exportFigure.Click += async (_, _) => { if (figure is not null) await SaveTextAsync("figure", "svg", figure.Svg); };
        ai.Click += async (_, _) =>
        {
            var request = ResearchInterpretationService.BuildRequest(_study, dataset, quality, executions);
            try
            {
                var provider = await ResearchInterpretationProviderFactory.CreateConfiguredAsync();
                if (provider is null)
                {
                    var notConfigured = await ResearchInterpretationService.InterpretAsync(request);
                    result.Text = T("خدمة SOCYVIA AI غير متاحة حاليا. لم يتم إنشاء تفسير، وتظل النتائج الحتمية مصدر الحقيقة.", "SOCYVIA AI is currently unavailable. No interpretation was generated; deterministic results remain the source of truth.") + $"\nInput hash: {notConfigured.InputHash}";
                    return;
                }

                var response = await ResearchInterpretationService.InterpretAsync(request, provider);
                result.Text = $"{T("تفسير مساعد بالذكاء الاصطناعي — يتطلب مراجعة الباحث.", "AI-assisted interpretation — requires researcher review.")}\n\n{response.Interpretation}\n\nInput hash: {response.InputHash}";
            }
            catch (SocyviaAiRateLimitException exception)
            {
                ApplicationDiagnosticsService.LogException(exception, "SOCYVIA AI interpretation rate limit");
                result.Text = T("بلغت خدمة SOCYVIA AI سعتها المؤقتة. حاول مرة أخرى لاحقا.", "SOCYVIA AI has reached temporary capacity. Try again later.");
            }
            catch (Exception exception)
            {
                ApplicationDiagnosticsService.LogException(exception, "SOCYVIA AI interpretation");
                result.Text = T("تعذر على خدمة SOCYVIA AI إنشاء تفسير. تظل النتائج الحتمية متاحة دون تغيير.", "The SOCYVIA AI service could not generate an interpretation. Deterministic results remain available and unchanged.");
            }
        };
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { build, export, exportFigure, ai } }); panel.Children.Add(result); _content.Children.Add(Surface(panel));
    }

    private async Task RenderAiWorkspaceAsync()
    {
        var dataset = await RemoteAnalysisDatasetService.BuildCompletedEligibleAsync(_study.Id, ExperimentRunType.Main);
        var quality = DataQualityService.Evaluate(dataset);
        var specifications = await AnalysisRepository.GetSpecificationsAsync(_study.Id, false);
        var executions = new List<AnalysisExecution>();
        foreach (var specification in specifications)
            if (await AnalysisRepository.GetLatestExecutionAsync(specification.Id) is { } execution) executions.Add(execution);

        var store = new AiConversationService();
        var conversation = await store.GetOrCreateAsync(_study.Id, dataset.DatasetHash);
        var aiServiceStatus = await SocyviaAiService.GetStatusAsync();
        var applicationState = await SocyviaAiApplicationContextService.ForStudyAsync(
            _study, "SOCYVIA AI", executions.Count > 0);
        var ar = LocalizationService.IsArabic;
        var groupLabels = dataset.Rows
            .Select(row => row.GroupName ?? row.ConditionName ?? T("غير معين", "Unassigned"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var latestExecution = executions.OrderByDescending(item => item.ExecutedAtUtc).FirstOrDefault();
        var latestSpecification = latestExecution is null
            ? null
            : specifications.FirstOrDefault(item => item.Id == latestExecution.AnalysisSpecificationId);
        var latestResult = latestExecution?.Result;
        var totalMissing = quality.MissingByVariable.Values.Sum();
        var qualityWarnings = quality.DuplicateParticipantWarnings.Count +
                              quality.IncompleteQuestionnaireWarnings.Count +
                              quality.SessionWarnings.Count;

        var root = new Grid
        {
            Name = "SocyviaAiWorkspaceRoot",
            ColumnDefinitions = new ColumnDefinitions(ar ? "*,18,2*" : "2*,18,*"),
            MinHeight = 660,
            FlowDirection = FlowDirection.LeftToRight
        };
        var context = new StackPanel
        {
            Spacing = 14,
            FlowDirection = ar ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };
        context.Children.Add(new TextBlock { Text = T("سياق البحث", "Research context"), Classes = { "pageTitle" } });
        context.Children.Add(new TextBlock
        {
            Text = T("الأدلة الحتمية المتاحة للمحادثة", "Deterministic evidence available to the conversation"),
            Foreground = new SolidColorBrush(Color.Parse("#2563EB")),
            FontWeight = FontWeight.SemiBold
        });
        context.Children.Add(new TextBlock { Text = StudyContextLabelService.ForDisplay(_study.Title, ar), Classes = { "sectionTitle" }, TextWrapping = TextWrapping.Wrap });
        context.Children.Add(new TextBlock
        {
            Text = $"{T("العينة التحليلية الرئيسية", "Main analytical sample")}: n={dataset.Rows.Count}\n" +
                   $"{T("الحالات المستبعدة", "Excluded")}: {quality.ExcludedN}\n" +
                   $"{T("التحليلات الحتمية", "Deterministic analyses")}: {executions.Count}\n" +
                   $"Dataset: {Short(dataset.DatasetHash)}",
            Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap
        });
        var aiReady = aiServiceStatus.State == SocyviaAiServiceState.Ready;
        Border KnowledgeCard(string label, string value, string detail) => new()
        {
            MinHeight = 76,
            Padding = new Thickness(11, 10),
            Classes = { "denseSurface" },
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = label, Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = value, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#1E304A")), TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = detail, FontSize = 8.5, Foreground = new SolidColorBrush(Color.Parse("#687A92")), TextWrapping = TextWrapping.Wrap }
                }
            }
        };
        static string EvidenceNumber(double? value) => value.HasValue ? value.Value.ToString("0.####") : "—";

        context.Children.Add(new TextBlock
        {
            Text = aiReady
                ? T("SOCYVIA AI · جاهز", "SOCYVIA AI · Ready")
                : SocyviaAiStatusPresentationService.Detail(aiServiceStatus, ar),
            Foreground = new SolidColorBrush(Color.Parse(aiReady ? "#177A5B" : "#9A650E")),
            TextWrapping = TextWrapping.Wrap
        });
        context.Children.Add(new TextBlock
        {
            Text = T(
                "السياق الافتراضي تجميعي: لا يتضمن بيانات تعريف شخصية أو معرفات مشاركين أو إجابات نصية خام.",
                "Default context is aggregate-only: no PII, participant identifiers, or raw open-text responses."),
            Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap
        });

        context.Children.Add(new TextBlock { Text = T("ما الذي يعرفه SOCYVIA AI", "What SOCYVIA AI knows"), Classes = { "sectionTitle" } });
        var knowledge = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,8,*"),
            RowDefinitions = new RowDefinitions("Auto,8,Auto,8,Auto")
        };
        void AddKnowledge(Control card, int column, int row)
        {
            Grid.SetColumn(card, column);
            Grid.SetRow(card, row);
            knowledge.Children.Add(card);
        }
        AddKnowledge(KnowledgeCard(
            T("العينة", "Sample"),
            $"n={dataset.Rows.Count}",
            T("الجلسات الرئيسية (Main) المكتملة والمؤهلة", "Completed eligible Main sessions")), 0, 0);
        AddKnowledge(KnowledgeCard(
            T("التصميم", "Design"),
            $"{_groups.Count} {T("مجموعات", "groups")} · {_conditions.Count} {T("شروط", "conditions")}",
            T("بنية الدراسة الحالية", "Current study structure")), 2, 0);
        AddKnowledge(KnowledgeCard(
            T("النتيجة", "Outcome"),
            latestSpecification?.Name ?? T("غير محسوبة", "Not calculated"),
            latestResult?.Method ?? T("لا يوجد تحليل حتمي محدد", "No deterministic analysis selected")), 0, 2);
        AddKnowledge(KnowledgeCard(
            T("المقارنة", "Comparison"),
            groupLabels.Length > 1 ? $"{groupLabels.Length} {T("مجموعات", "groups")}" : T("غير متاحة", "Not available"),
            groupLabels.Length == 0 ? T("لا توجد مجموعات في العينة", "No sample groups") : string.Join(" · ", groupLabels.Take(3))), 2, 2);
        AddKnowledge(KnowledgeCard(
            T("جودة البيانات", "Data quality"),
            qualityWarnings == 0 && totalMissing == 0 ? T("لا تنبيهات تجميعية", "No aggregate warnings") : T("تحتاج المراجعة", "Review advised"),
            $"{T("مستبعد", "Excluded")}: {quality.ExcludedN} · {T("مفقود", "Missing")}: {totalMissing} · {T("تنبيهات", "Warnings")}: {qualityWarnings}"), 0, 4);
        AddKnowledge(KnowledgeCard(
            T("المصدر", "Provenance"),
            ScientificEngineMetadata.Version,
            $"Dataset {Short(dataset.DatasetHash)}"), 2, 4);
        context.Children.Add(knowledge);

        context.Children.Add(new TextBlock { Text = T("الدليل الحالي", "Current Evidence"), Classes = { "sectionTitle" } });
        var evidence = new StackPanel { Spacing = 10 };
        if (latestResult is null)
        {
            evidence.Children.Add(new TextBlock
            {
                Text = T(
                    "لا تتوفر نتيجة حتمية محسوبة لهذه الدراسة بعد. يمكن أن يناقش SOCYVIA AI التصميم وجودة البيانات دون اختراع نتائج رقمية.",
                    "No computed deterministic result is available for this study yet. SOCYVIA AI can discuss design and data quality without inventing numerical findings."),
                Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap
            });
        }
        else
        {
            evidence.Children.Add(new TextBlock
            {
                Text = latestSpecification?.Name ?? T("أحدث تحليل حتمي", "Latest deterministic analysis"),
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            var evidenceMetrics = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,8,*"),
                RowDefinitions = new RowDefinitions("Auto,8,Auto")
            };
            var evidenceCards = new[]
            {
                KnowledgeCard(T("الطريقة", "Method"), latestResult.Method, latestResult.Status),
                KnowledgeCard("N", latestResult.N.ToString(), T("العينة المستخدمة في التحليل", "Analysis sample")),
                KnowledgeCard(T("قيمة p", "p-value"), EvidenceNumber(latestResult.PValue), T("محسوبة بواسطة المحرك الحتمي", "Deterministically calculated")),
                KnowledgeCard(T("حجم الأثر", "Effect size"), EvidenceNumber(latestResult.EffectSize?.Value), latestResult.EffectSize?.Method ?? T("غير محسوب", "Not calculated"))
            };
            for (var index = 0; index < evidenceCards.Length; index++)
            {
                Grid.SetColumn(evidenceCards[index], index % 2 == 0 ? 0 : 2);
                Grid.SetRow(evidenceCards[index], index < 2 ? 0 : 2);
                evidenceMetrics.Children.Add(evidenceCards[index]);
            }
            evidence.Children.Add(evidenceMetrics);
            if (!string.IsNullOrWhiteSpace(latestResult.CanonicalSummary))
                evidence.Children.Add(new TextBlock { Text = latestResult.CanonicalSummary, TextWrapping = TextWrapping.Wrap });
        }
        context.Children.Add(new Border { Padding = new Thickness(14), Classes = { "scientificFrame" }, Child = evidence });

        var messages = new StackPanel { Spacing = 8 };
        void RenderMessages(AiStudyConversation value)
        {
            messages.Children.Clear();
            if (value.Messages.Count == 0)
            {
                messages.Children.Add(new StackPanel
                {
                    Spacing = 7,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(22),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = T("اسأل SOCYVIA AI عن المنتج أو دراستك", "Ask SOCYVIA AI about the product or your study"),
                            FontSize = 16,
                            FontWeight = FontWeight.SemiBold,
                            TextAlignment = TextAlignment.Center,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = T(
                                "اطلب المساعدة في الخطوة التالية أو النشر أو الوسائط، أو ناقش النتائج والقيود.",
                                "Ask for help with the next step, publishing, or media, or discuss findings and limitations."),
                            Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center
                        }
                    }
                });
                return;
            }
            foreach (var message in value.Messages)
            {
                var researcher = message.Role == "researcher";
                var bubble = new Border
                {
                    MaxWidth = 720, Padding = new Thickness(14, 11), CornerRadius = new CornerRadius(11),
                    HorizontalAlignment = researcher
                        ? (LocalizationService.IsArabic ? HorizontalAlignment.Left : HorizontalAlignment.Right)
                        : (LocalizationService.IsArabic ? HorizontalAlignment.Right : HorizontalAlignment.Left),
                    Classes = { researcher ? "evidenceCard" : "aiContextCard" },
                    Child = new StackPanel
                    {
                        Spacing = 5,
                        Children =
                        {
                            new TextBlock { Text = researcher ? T("الباحث", "Researcher") : "SOCYVIA AI", FontWeight = FontWeight.SemiBold },
                            new TextBlock { Text = message.Content, TextWrapping = TextWrapping.Wrap, TextAlignment = LocalizationService.IsArabic ? TextAlignment.Right : TextAlignment.Left },
                            new TextBlock { Text = message.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), Classes = { "metadata" } }
                        }
                    }
                };
                messages.Children.Add(bubble);
            }
        }
        var input = new TextBox
        {
            AcceptsReturn = true, MinHeight = 76, MaxHeight = 150, TextWrapping = TextWrapping.Wrap,
            IsEnabled = aiReady,
            PlaceholderText = aiReady
                ? T("اسأل عن SOCYVIA أو هذه الدراسة...", "Ask about SOCYVIA or this study...")
                : SocyviaAiStatusPresentationService.Detail(aiServiceStatus, ar)
        };
        var send = Button(T("إرسال", "Send"), true);
        send.Classes.Add("ai");
        send.IsEnabled = aiReady;
        var retry = Button(T("إعادة المحاولة", "Retry")); retry.IsVisible = false;
        var state = new TextBlock { Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap };
        string? lastPrompt = null;

        var promptStarters = new WrapPanel { Name = "SocyviaAiSuggestedPrompts", Orientation = Orientation.Horizontal };
        foreach (var prompt in SocyviaAiUiCopy.StudyPrompts.Select(item => T(item.Arabic, item.English)))
        {
            var starter = Button(prompt);
            starter.Classes.Add("subtle");
            starter.Margin = new Thickness(0, 0, 8, 8);
            starter.IsEnabled = aiReady;
            starter.Click += (_, _) =>
            {
                input.Text = prompt;
                input.Focus();
                input.CaretIndex = input.Text?.Length ?? 0;
            };
            promptStarters.Children.Add(starter);
        }
        RenderMessages(conversation);

        async Task SendAsync(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return;
            if (!aiReady)
            {
                state.Text = SocyviaAiStatusPresentationService.Detail(aiServiceStatus, ar);
                return;
            }
            lastPrompt = prompt.Trim(); send.IsEnabled = false; retry.IsVisible = false;
            input.IsEnabled = false;
            state.Text = T("جار إعداد إجابة مقيدة بسياق SOCYVIA والأدلة المتاحة...", "Preparing an answer constrained by SOCYVIA context and available evidence...");
            var researcherMessage = new AiConversationMessage("researcher", lastPrompt, DateTime.UtcNow);
            var repeatedRetry = conversation.Messages.LastOrDefault() is { Role: "researcher" } last &&
                                string.Equals(last.Content, researcherMessage.Content, StringComparison.Ordinal);
            var workingMessages = (repeatedRetry ? conversation.Messages : conversation.Messages.Concat([researcherMessage]))
                .TakeLast(24).ToArray();
            var request = ResearchInterpretationService.BuildRequest(_study, dataset, quality, executions,
                prompt: lastPrompt, conversation: workingMessages, applicationState: applicationState);
            if (!AiConversationService.IsAggregateSafe(request))
            {
                state.Text = T("منع SOCYVIA AI إرسال سياق غير تجميعي.", "SOCYVIA AI blocked a non-aggregate context.");
                send.IsEnabled = true; input.IsEnabled = true; return;
            }
            try
            {
                var provider = await ResearchInterpretationProviderFactory.CreateConfiguredAsync();
                if (provider is null)
                {
                    state.Text = SocyviaAiStatusPresentationService.Detail(aiServiceStatus, ar);
                    retry.IsVisible = true; return;
                }
                conversation = conversation with
                {
                    DatasetHash = dataset.DatasetHash,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Provider = provider.ProviderName,
                    Messages = workingMessages
                };
                await store.SaveAsync(conversation);
                RenderMessages(conversation);
                var response = await ResearchInterpretationService.InterpretAsync(request, provider);
                var assistantMessage = new AiConversationMessage("assistant", response.Interpretation ?? T("لا توجد استجابة.", "No response was returned."), DateTime.UtcNow);
                conversation = conversation with
                {
                    DatasetHash = dataset.DatasetHash, UpdatedAtUtc = DateTime.UtcNow,
                    Provider = response.Provider, Model = response.Model,
                    Messages = workingMessages.Concat([assistantMessage]).ToArray()
                };
                await store.SaveAsync(conversation);
                input.Text = string.Empty;
                state.Text = $"{T("تفسير بمساعدة الذكاء الاصطناعي — يتطلب مراجعة الباحث.", "AI-assisted interpretation — researcher review required.")} · {Short(response.InputHash)}";
                RenderMessages(conversation);
            }
            catch (SocyviaAiRateLimitException exception)
            {
                ApplicationDiagnosticsService.LogException(exception, "SOCYVIA AI conversation rate limit");
                state.Text = SocyviaAiStatusPresentationService.Detail(
                    new SocyviaAiServiceStatus(SocyviaAiServiceState.RateLimited, exception.Message, SocyviaAiServiceAvailabilityReason.RateLimited), ar);
                retry.IsVisible = true;
            }
            catch (Exception exception)
            {
                ApplicationDiagnosticsService.LogException(exception, "SOCYVIA AI conversation");
                state.Text = T(
                    "تعذر على خدمة SOCYVIA AI إنشاء تفسير. لم تتغير النتائج الحتمية ويمكنك إعادة المحاولة.",
                    "The SOCYVIA AI service could not generate an interpretation. Deterministic results are unchanged; you can retry.");
                retry.IsVisible = true;
            }
            finally { send.IsEnabled = aiReady; input.IsEnabled = aiReady; }
        }

        send.Click += async (_, _) => await SendAsync(input.Text ?? string.Empty);
        input.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.Key != Key.Enter || eventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
            eventArgs.Handled = true;
            await SendAsync(input.Text ?? string.Empty);
        };
        retry.Click += async (_, _) => await SendAsync(lastPrompt ?? input.Text ?? string.Empty);
        var newConversation = Button(T("محادثة جديدة", "New Conversation"));
        newConversation.Click += async (_, _) =>
        {
            conversation = store.New(_study.Id, dataset.DatasetHash);
            await store.SaveAsync(conversation); RenderMessages(conversation); state.Text = string.Empty;
        };
        var clear = Button(T("مسح المحادثة", "Clear Conversation"));
        clear.Click += async (_, _) =>
        {
            await store.ClearAsync(_study.Id); conversation = store.New(_study.Id, dataset.DatasetHash);
            RenderMessages(conversation); state.Text = string.Empty;
        };

        var conversationPanel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,12,Auto,12,*,12,Auto"),
            MinHeight = 630,
            FlowDirection = ar ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };
        var conversationHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        conversationHeader.Children.Add(new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = "SOCYVIA AI", Classes = { "pageTitle" } },
                new TextBlock
                {
                    Text = aiReady
                        ? T("مساعد SOCYVIA للإرشاد داخل المنتج وتفسير أدلة الدراسة الحتمية.", "SOCYVIA assistant for product guidance and interpretation of deterministic study evidence.")
                        : T("خدمة SOCYVIA AI غير متاحة حاليا. تظل الأدلة الحتمية أدناه متاحة للمراجعة.", "SOCYVIA AI is currently unavailable. The deterministic evidence below remains available for review."),
                    Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap
                }
            }
        });
        var historyActions = new WrapPanel { Orientation = Orientation.Horizontal };
        newConversation.Margin = new Thickness(0, 0, 6, 6);
        clear.Margin = new Thickness(0, 0, 0, 6);
        historyActions.Children.Add(newConversation);
        historyActions.Children.Add(clear);
        Grid.SetColumn(historyActions, 1);
        conversationHeader.Children.Add(historyActions);
        conversationPanel.Children.Add(conversationHeader);

        var promptSurface = new Border
        {
            Padding = new Thickness(12, 10),
            Classes = { "aiContextCard" },
            Child = new StackPanel
            {
                Spacing = 7,
                Children =
                {
                    new TextBlock { Text = T(SocyviaAiUiCopy.ArabicSuggestedQuestionsTitle, "Suggested questions"), Classes = { "sectionTitle" } },
                    promptStarters
                }
            }
        };
        Grid.SetRow(promptSurface, 2);
        conversationPanel.Children.Add(promptSurface);

        var messageSurface = new Border
        {
            MinHeight = 360,
            Classes = { "denseSurface" },
            Padding = new Thickness(12),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = messages
            }
        };
        Grid.SetRow(messageSurface, 4);
        conversationPanel.Children.Add(messageSurface);

        var composerActions = new WrapPanel { Orientation = Orientation.Horizontal };
        send.Margin = new Thickness(0, 0, 8, 0);
        retry.Margin = new Thickness(0, 0, 8, 0);
        composerActions.Children.Add(send);
        composerActions.Children.Add(retry);
        var composer = new Border
        {
            Name = "SocyviaAiComposer",
            Padding = new Thickness(12),
            Classes = { "scientificFrame" },
            Child = new StackPanel
            {
                Spacing = 9,
                Children =
                {
                    input,
                    composerActions,
                    state,
                    new TextBlock
                    {
                        Text = T(
                            "يفصل SOCYVIA AI بين الملاحظات الوصفية والنتيجة الإحصائية والتفسير. لا يستبدل الحسابات الحتمية ولا يثبت السببية.",
                            "SOCYVIA AI distinguishes observed, statistical, and interpretive statements. It never replaces deterministic calculations or establishes causality."),
                        Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
        Grid.SetRow(composer, 6);
        conversationPanel.Children.Add(composer);

        var contextSurface = new Border
        {
            Padding = new Thickness(16),
            Classes = { "aiSurface" },
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = context
            }
        };
        var conversationSurface = new Border { Padding = new Thickness(18), Classes = { "aiSurface" }, Child = conversationPanel };
        Grid.SetColumn(contextSurface, ar ? 0 : 2);
        Grid.SetColumn(conversationSurface, ar ? 2 : 0);
        root.Children.Add(contextSurface);
        root.Children.Add(conversationSurface);
        _content.Children.Add(root);
    }

    private void AddExport(Panel panel, string label, string kind, Func<Task<string>> create)
    {
        var button = Button(label); button.Click += async (_, _) => await ExportAsync(kind, create); panel.Children.Add(button);
    }

    private async Task ExportAsync(string kind, Func<Task<string>> create)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage?.CanSave != true) { _syncState.Text = T("تعذر فتح نافذة حفظ الملف على هذا الجهاز.", "The save dialog is unavailable on this device."); return; }
        var safe = string.Concat((_study.Title.Length == 0 ? "study" : _study.Title).Select(c => char.IsLetterOrDigit(c) ? c : '_')).Trim('_');
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = T("تصدير بيانات SOCYVIA", "Export SOCYVIA data"), SuggestedFileName = $"{safe}_{kind}_{DateTime.UtcNow:yyyyMMdd}.csv", DefaultExtension = "csv", ShowOverwritePrompt = true, FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"], MimeTypes = ["text/csv"] }] });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync(); stream.SetLength(0); await using var writer = new StreamWriter(stream); await writer.WriteAsync(await create()); _syncState.Text = T("تم إنشاء ملف التصدير.", "Export created.");
    }

    private async Task SaveTextAsync(string kind, string extension, string content)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage?.CanSave != true) { _syncState.Text = T("تعذر فتح نافذة حفظ الملف على هذا الجهاز.", "The save dialog is unavailable on this device."); return; }
        var safe = string.Concat((_study.Title.Length == 0 ? "study" : _study.Title).Select(c => char.IsLetterOrDigit(c) ? c : '_')).Trim('_');
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = T("حفظ ملف SOCYVIA", "Save SOCYVIA file"), SuggestedFileName = $"{safe}_{kind}_{DateTime.UtcNow:yyyyMMdd}.{extension}", DefaultExtension = extension, ShowOverwritePrompt = true });
        if (file is null) return; await using var stream = await file.OpenWriteAsync(); stream.SetLength(0); await using var writer = new StreamWriter(stream); await writer.WriteAsync(content); _syncState.Text = T("تم إنشاء الملف.", "File created.");
    }

    private bool InSelectedGroup(RemoteParticipantSessionContract item) => SelectedGroup() is not { } id || item.GroupId == id;
    private bool InSelectedGroupEvent(RemoteTelemetryEvent item) => SelectedConditionIds().Contains(item.ConditionId);
    private bool InSelectedGroupQuestionnaire(RemoteQuestionnaireResponseContract item) => SelectedGroup() is not { } id || item.GroupId == id;
    private bool InSelectedGroupCondition(ExperimentalCondition item) => SelectedGroup() is not { } id || item.GroupId == id;
    private string? SelectedGroup() => (_group.SelectedItem as Choice)?.Id;
    private IReadOnlySet<string> SelectedConditionIds() => SelectedGroup() is not { } id ? _conditions.Select(x => x.Id).ToHashSet() : _conditions.Where(x => x.GroupId == id).Select(x => x.Id).ToHashSet();
    private string? SelectedCondition() { var ids = SelectedConditionIds(); return ids.Count == 1 ? ids.First() : null; }
    private string GroupName(string? id) => _groups.FirstOrDefault(x => x.Id == id)?.Name ?? "—";
    private static string CompletionLabel(RemoteParticipantCompletionState state) => state switch
    {
        RemoteParticipantCompletionState.CompletedEligible => T("مكتمل ومؤهل", "Completed — Eligible"),
        RemoteParticipantCompletionState.CompletedFlagged => T("مكتمل مع تنبيه", "Completed — Flagged"),
        RemoteParticipantCompletionState.TechnicalFailure => T("فشل تقني", "Technical Failure"),
        RemoteParticipantCompletionState.Excluded => T("مستبعد", "Excluded"),
        _ => T("غير مكتمل", "Incomplete")
    };
    private static string LifecycleLabel(RemoteParticipantLifecycleState state) => state switch
    {
        RemoteParticipantLifecycleState.PreStarted => T("بدء الاستبيان القبلي", "Pre-questionnaire Started"),
        RemoteParticipantLifecycleState.PreCompleted => T("اكتمال الاستبيان القبلي", "Pre-questionnaire Completed"),
        RemoteParticipantLifecycleState.SessionStarted => T("بدء الجلسة", "Session Started"),
        RemoteParticipantLifecycleState.FeedInProgress => T("المسار قيد التنفيذ", "Participant Path In Progress"),
        RemoteParticipantLifecycleState.FeedEndReached => T("نهاية المسار", "Participant Path Completed"),
        RemoteParticipantLifecycleState.PostStarted => T("بدء الاستبيان البعدي", "Post-questionnaire Started"),
        RemoteParticipantLifecycleState.PostCompleted => T("اكتمال الاستبيان البعدي", "Post-questionnaire Completed"),
        RemoteParticipantLifecycleState.Completed => T("مكتمل", "Completed"),
        _ => T("غير مكتمل", "Incomplete")
    };
    private static string Short(string x) => x.Length <= 10 ? x : x[..8] + "…";
    private static string Date(DateTime? x) => x?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";
    private static Border Surface(Control child, double padding = 16) => new() { Padding = new Thickness(padding), Classes = { "researchCard" }, Child = child };
    private static Control Metric(string label, string value) => new Border { Padding = new Thickness(13), Classes = { "metricCard" }, Child = new StackPanel { Spacing = 4, Children = { new TextBlock { Text = value, FontSize = 19, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#1E304A")) }, new TextBlock { Text = label, Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap } } } };
    private static Control Row(bool header, params string[] values) { var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(',', Enumerable.Repeat("*", values.Length))) }; for (var index = 0; index < values.Length; index++) { var item = new TextBlock { Text = values[index], FontSize = header ? 8.5 : 9, FontWeight = header ? FontWeight.SemiBold : FontWeight.Normal, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse(header ? "#60718A" : "#263A55")) }; Grid.SetColumn(item, index); grid.Children.Add(item); } return header ? grid : Surface(grid, 10); }
    private void Empty(string message) => _content.Children.Add(Surface(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Classes = { "metadata" }, Margin = new Thickness(12) }));
    private static Button Button(string label, bool primary = false) { var button = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Center }; button.Classes.Add(primary ? "primary" : "secondary"); return button; }
    private static string T(string ar, string en) => UiTextService.Localized(ar, en);
    private sealed record Choice(string? Id, string Label) { public override string ToString() => Label; }
}
