using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SOCYVIA.Models;
using SOCYVIA.Repositories;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

public partial class SessionLaunchView : UserControl
{
    public event EventHandler? SessionPrepared;

    private Study _study = new();
    private List<Participant> _participants = new();
    private List<StudyGroup> _groups = new();
    private List<ExperimentalCondition> _conditions = new();
    private bool _isLoading;
    private bool _isPreparing;
    private ExperimentLaunchContext? _preparedContext;
    private string? _preparedSessionId;

    private readonly FontFamily _englishFont =
        new("avares://SOCYVIA/Assets/Fonts#IBM Plex Sans");

    private readonly FontFamily _arabicFont =
        new("avares://SOCYVIA/Assets/Fonts#IBM Plex Sans Arabic");


    public SessionLaunchView()
    {
        InitializeComponent();
        SetupEvents();
        ConfigureLanguage();
        PopulateStrategies();
        Loaded += (_, _) => PopulateDisplays();
    }


    public SessionLaunchView(Study study)
        : this()
    {
        _study = study;

        PopulateStrategies();

        AttachedToVisualTree +=
            async (_, _) => await ReloadAsync();
    }


    public async Task ReloadAsync()
    {
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;

        try
        {
            var selectedParticipantId =
                GetSelectedValue(ParticipantComboBox);

            var participantsTask =
                ParticipantRepository.GetByStudyAsync(_study.Id);
            var groupsTask =
                GroupRepository.GetByStudyAsync(_study.Id);
            var conditionsTask =
                ExperimentalConditionRepository
                    .GetByStudyAsync(_study.Id);
            var readinessTask =
                ExperimentReadinessService.EvaluateAsync(_study);
            var sessionsTask =
                ExperimentSessionRepository.GetByStudyAsync(_study.Id);

            await Task.WhenAll(
                participantsTask,
                groupsTask,
                conditionsTask,
                readinessTask,
                sessionsTask);

            _participants = participantsTask.Result;
            _groups = groupsTask.Result;
            _conditions = conditionsTask.Result;

            ParticipantComboBox.ItemsSource =
                _participants
                    .OrderBy(participant => participant.ParticipantCode)
                    .Select(participant =>
                        new Choice(
                            participant.Id,
                            BuildParticipantDisplay(participant)))
                    .ToList();

            SelectChoice(
                ParticipantComboBox,
                selectedParticipantId);

            if (ParticipantComboBox.SelectedItem is null &&
                ParticipantComboBox.ItemCount > 0)
            {
                ParticipantComboBox.SelectedIndex = 0;
            }

            RenderReadiness(readinessTask.Result);
            RenderSessionsOverview(sessionsTask.Result);
            await UpdatePreviewAsync();
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Session launch workspace load error: {exception}");
        }
        finally
        {
            _isLoading = false;
        }
    }


    private void RenderSessionsOverview(
        IReadOnlyCollection<ExperimentSession> sessions)
    {
        SessionsOverviewContainer.Children.Clear();
        SessionsOverviewCount.Text = sessions.Count.ToString();
        SessionsOverviewEmpty.IsVisible = sessions.Count == 0;

        foreach (var session in sessions
                     .OrderByDescending(item => item.UpdatedAtUtc)
                     .Take(8))
        {
            var participant = _participants.FirstOrDefault(item =>
                string.Equals(item.Id, session.ParticipantId, StringComparison.Ordinal));
            var group = _groups.FirstOrDefault(item =>
                string.Equals(item.Id, session.GroupId, StringComparison.Ordinal));
            var condition = _conditions.FirstOrDefault(item =>
                string.Equals(item.Id, session.ConditionId, StringComparison.Ordinal));

            var action = new Button
            {
                Content = SessionActionText(session.Status),
                MinWidth = 96,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            action.Classes.Add(
                string.Equals(session.Status, SessionLifecycleStates.Ready, StringComparison.Ordinal)
                    ? "primary"
                    : "quiet");
            action.Click += async (_, _) => await HandleSessionActionAsync(session);

            var status = new Border
            {
                Background = Brush(StatusBackground(session.Status)),
                BorderBrush = Brush(StatusBorder(session.Status)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(9, 4),
                Child = new TextBlock
                {
                    Text = LocalizeStatus(session.Status),
                    FontFamily = CurrentFont,
                    FontSize = 8,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brush(StatusForeground(session.Status)),
                    TextAlignment = TextAlignment.Center
                }
            };
            status.Classes.Add("badge");
            status.Classes.Add(StatusClass(session.Status));

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("1.1*,1*,1*,Auto,Auto"),
                ColumnSpacing = 12
            };
            row.Children.Add(new TextBlock
            {
                Text = participant?.ParticipantCode ?? session.ParticipantId,
                FontFamily = CurrentFont,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            AddSessionCell(row, group?.Name ?? Text("غير محددة", "Not assigned"), 1);
            AddSessionCell(row, condition?.Name ?? Text("غير محدد", "Not assigned"), 2);
            Grid.SetColumn(status, 3);
            row.Children.Add(status);
            Grid.SetColumn(action, 4);
            row.Children.Add(action);

            var detail = new TextBlock
            {
                Text = BuildSessionDetail(session),
                FontFamily = CurrentFont,
                FontSize = 8,
                Foreground = Brush("#66738A"),
                TextAlignment = LocalizationService.IsArabic
                    ? TextAlignment.Right
                    : TextAlignment.Left
            };

            var card = new Border
            {
                Padding = new Thickness(12, 10),
                Background = Brush("#F8FAFD"),
                BorderBrush = Brush("#DCE5F0"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = new StackPanel
                {
                    Spacing = 5,
                    Children = { row, detail }
                }
            };
            SessionsOverviewContainer.Children.Add(card);
        }
    }


    private void AddSessionCell(Grid row, string value, int column)
    {
        var text = new TextBlock
        {
            Text = value,
            FontFamily = CurrentFont,
            FontSize = 8.5,
            Foreground = Brush("#465671"),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(text, column);
        row.Children.Add(text);
    }


    private async Task HandleSessionActionAsync(ExperimentSession session)
    {
        LaunchErrorText.IsVisible = false;

        if (string.Equals(session.Status, SessionLifecycleStates.Ready, StringComparison.Ordinal))
        {
            _preparedSessionId = session.Id;
            LaunchResultTitle.Text = Text("جلسة جاهزة", "Ready participant session");
            LaunchResultText.Text = BuildSessionDetail(session);
            LaunchResultPanel.IsVisible = true;
            LaunchParticipantButton.IsVisible = true;
            await LaunchParticipantAsync();
            return;
        }

        if (string.Equals(session.Status, SessionLifecycleStates.Completed, StringComparison.Ordinal))
        {
            try
            {
                var summary = await SessionSummaryService.CreateAsync(session.Id);
                ShowSessionSummary(summary);
            }
            catch (Exception exception)
            {
                ShowError(exception.Message);
            }
            return;
        }

        LaunchResultTitle.Text = Text("تفاصيل الجلسة", "Session details");
        LaunchResultText.Text = BuildSessionDetail(session);
        LaunchResultPanel.IsVisible = true;
        LaunchParticipantButton.IsVisible = false;
    }


    private void ShowSessionSummary(ParticipantSessionSummary summary)
    {
        LaunchResultTitle.Text = Text("ملخص جلسة المشارك", "Participant session summary");
        LaunchResultText.Text = Text(
            $"المشارك: {summary.ParticipantCode}\nالمجموعة: {summary.GroupName ?? "—"}\nالشرط: {summary.ConditionName}\nالحالة: {LocalizeStatus(summary.Status)}\nالمدة: {FormatDuration(summary.DurationMilliseconds)}\nالمحفزات المعروضة: {summary.StimuliExposed}\nالتفاعلات: {summary.InteractionCount}\nزمن العرض المتتبع: {FormatDuration(summary.TotalExposureMilliseconds)}",
            $"Participant: {summary.ParticipantCode}\nGroup: {summary.GroupName ?? "—"}\nCondition: {summary.ConditionName}\nStatus: {LocalizeStatus(summary.Status)}\nDuration: {FormatDuration(summary.DurationMilliseconds)}\nStimuli exposed: {summary.StimuliExposed}\nInteractions: {summary.InteractionCount}\nTracked exposure: {FormatDuration(summary.TotalExposureMilliseconds)}");
        LaunchResultPanel.IsVisible = true;
        LaunchParticipantButton.IsVisible = false;
    }


    private void SetupEvents()
    {
        ParticipantComboBox.SelectionChanged +=
            async (_, _) =>
            {
                if (!_isLoading)
                {
                    await UpdatePreviewAsync();
                }
            };

        StrategyComboBox.SelectionChanged +=
            async (_, _) =>
            {
                ConfigureStrategyControls();

                if (!_isLoading)
                {
                    await UpdatePreviewAsync();
                }
            };

        ConditionComboBox.SelectionChanged +=
            async (_, _) =>
            {
                if (!_isLoading)
                {
                    await UpdateManipulationPreviewAsync();
                }
            };

        PrepareSessionButton.Click +=
            async (_, _) => await PrepareSessionAsync();

        LaunchParticipantButton.Click +=
            async (_, _) => await LaunchParticipantAsync();
    }


    private void PopulateStrategies()
    {
        StrategyComboBox.ItemsSource =
            new[]
            {
                new Choice(
                    ConditionAssignmentStrategy.Manual.ToString(),
                    Text("يدوي", "Manual")),
                new Choice(
                    ConditionAssignmentStrategy.Random.ToString(),
                    Text("عشوائي", "Random")),
                new Choice(
                    ConditionAssignmentStrategy.BalancedRandom.ToString(),
                    Text("عشوائي متوازن", "Balanced random"))
            };

        var defaultStrategy =
            Enum.TryParse<ConditionAssignmentStrategy>(
                _study.AssignmentMethod,
                out var parsed)
                ? parsed.ToString()
                : ConditionAssignmentStrategy.Manual.ToString();

        SelectChoice(StrategyComboBox, defaultStrategy);

        if (StrategyComboBox.SelectedItem is null)
        {
            StrategyComboBox.SelectedIndex = 0;
        }

        ConfigureStrategyControls();
    }


    private async Task UpdatePreviewAsync()
    {
        var participant = GetSelectedParticipant();

        if (participant is null)
        {
            GroupValue.Text = "—";
            AssignedConditionValue.Text = "—";
            StimuliPreviewValue.Text = "0";
            ManipulationPreviewValue.Text = "—";
            ConditionComboBox.ItemsSource = Array.Empty<Choice>();
            return;
        }

        var group =
            _groups.FirstOrDefault(item =>
                string.Equals(
                    item.Id,
                    participant.GroupId,
                    StringComparison.Ordinal));

        GroupValue.Text = group is null
            ? Text("غير محددة", "Not assigned")
            : group.Name;

        var eligibleConditions =
            await ConditionAssignmentService
                .GetEligibleConditionsAsync(participant);

        var selectedConditionId =
            GetSelectedValue(ConditionComboBox);

        ConditionComboBox.ItemsSource =
            eligibleConditions
                .Select(condition =>
                    new Choice(condition.Id, condition.Name))
                .ToList();

        var activeAssignment =
            await ParticipantConditionAssignmentRepository
                .GetActiveForParticipantAsync(participant.Id);

        var activeCondition = activeAssignment is null
            ? null
            : eligibleConditions.FirstOrDefault(condition =>
                string.Equals(
                    condition.Id,
                    activeAssignment.ConditionId,
                    StringComparison.Ordinal));

        SelectChoice(
            ConditionComboBox,
            activeCondition?.Id ?? selectedConditionId);

        if (ConditionComboBox.SelectedItem is null &&
            ConditionComboBox.ItemCount > 0)
        {
            ConditionComboBox.SelectedIndex = 0;
        }

        AssignedConditionValue.Text = activeCondition?.Name ??
            Text("سيتم التعيين عند التحضير", "Assigned on prepare");

        if (group is null)
        {
            StimuliPreviewValue.Text = "0";
        }
        else
        {
            var stimuli =
                await StimulusPostRepository
                    .GetForGroupAsync(
                        _study.Id,
                        group.Id);

            StimuliPreviewValue.Text =
                stimuli.Count(stimulus => stimulus.IsActive)
                    .ToString();
        }

        var strategy =
            GetSelectedValue(StrategyComboBox);

        if (activeCondition is null &&
            !string.Equals(
                strategy,
                ConditionAssignmentStrategy.Manual.ToString(),
                StringComparison.Ordinal))
        {
            ManipulationPreviewValue.Text = Text(
                "سيتم تحديدها بعد التعيين",
                "Determined after assignment");
        }
        else
        {
            await UpdateManipulationPreviewAsync(
                activeCondition);
        }
    }


    private async Task UpdateManipulationPreviewAsync(
        ExperimentalCondition? preferredCondition = null)
    {
        await Task.CompletedTask;

        var condition = preferredCondition ??
            _conditions.FirstOrDefault(item =>
                string.Equals(
                    item.Id,
                    GetSelectedValue(ConditionComboBox),
                    StringComparison.Ordinal));

        if (condition is null)
        {
            ManipulationPreviewValue.Text = "—";
            return;
        }

        var settings =
            ConditionManipulationService.Deserialize(
                condition.ManipulationJson);

        ManipulationPreviewValue.Text =
            BuildManipulationSummary(settings);
    }


    private async Task PrepareSessionAsync()
    {
        if (_isPreparing)
        {
            return;
        }

        var participant = GetSelectedParticipant();

        if (participant is null)
        {
            ShowError(Text(
                "اختر مشاركا أولا.",
                "Select a participant first."));
            return;
        }

        var strategyValue =
            GetSelectedValue(StrategyComboBox) ??
            ConditionAssignmentStrategy.Manual.ToString();

        var strategy =
            Enum.Parse<ConditionAssignmentStrategy>(strategyValue);

        _isPreparing = true;
        PrepareSessionButton.IsEnabled = false;
        LaunchErrorText.IsVisible = false;
        LaunchResultPanel.IsVisible = false;
        LaunchParticipantButton.IsVisible = false;
        _preparedContext = null;
        _preparedSessionId = null;

        try
        {
            var result =
                await ExperimentLaunchService.PrepareAsync(
                    new ExperimentLaunchRequest
                    {
                        StudyId = _study.Id,
                        ParticipantId = participant.Id,
                        AssignmentStrategy = strategy,
                        ManualConditionId =
                            strategy == ConditionAssignmentStrategy.Manual
                                ? GetSelectedValue(ConditionComboBox)
                                : null
                    });

            if (!result.IsSuccessful || result.Context is null)
            {
                var existingSessionId = result.Failures
                    .FirstOrDefault(failure =>
                        failure.Code == "session.active_exists")?
                    .RelatedEntityId;
                if (await TryShowExistingReadySessionAsync(
                        existingSessionId))
                {
                    return;
                }

                ShowError(
                    string.Join(
                        Environment.NewLine,
                        result.Failures.Select(LocalizeFailure)));
                return;
            }

            var context = result.Context;
            _preparedContext = context;
            _preparedSessionId = context.Session.Id;

            LaunchResultTitle.Text = Text(
                "تم تحضير الجلسة",
                "Session prepared");

            LaunchResultText.Text = Text(
                $"الجلسة: {context.Session.Id}\nالحالة: جاهزة\nالشرط: {context.Condition.Name}\nالمحفزات: {context.ResolvedStimuli.Count}\nلقطة التكوين: {context.Snapshot.Id}",
                $"Session: {context.Session.Id}\nStatus: {context.Session.Status}\nCondition: {context.Condition.Name}\nStimuli: {context.ResolvedStimuli.Count}\nConfiguration snapshot: {context.Snapshot.Id}");

            LaunchResultPanel.IsVisible = true;
            LaunchParticipantButton.IsVisible = true;
            SessionPrepared?.Invoke(this, EventArgs.Empty);
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Session preparation error: {exception}");

            ShowError(
                LocalizationService.IsArabic
                    ? "تعذر تحضير الجلسة. راجع إعدادات التجربة والمشارك."
                    : exception.Message);
        }
        finally
        {
            _isPreparing = false;
            PrepareSessionButton.IsEnabled = true;
        }
    }


    private async Task LaunchParticipantAsync()
    {
        if (string.IsNullOrWhiteSpace(_preparedSessionId))
        {
            ShowError(Text(
                "حضر الجلسة أولا.",
                "Prepare the session first."));
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
        {
            ShowError(Text(
                "تعذر فتح نافذة المشارك.",
                "The participant window could not be opened."));
            return;
        }

        LaunchParticipantButton.IsEnabled = false;
        try
        {
            var displayIndex =
                (ParticipantDisplayComboBox.SelectedItem as DisplayChoice)?.Index ?? 0;
            var window = new ParticipantExperimentWindow(
                _preparedSessionId,
                displayIndex);
            var summary = await window
                .ShowDialog<ParticipantSessionSummary?>(owner);
            if (summary is null)
            {
                return;
            }

            ShowSessionSummary(summary);
            _preparedContext = null;
            _preparedSessionId = null;
            SessionPrepared?.Invoke(this, EventArgs.Empty);
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Participant runner launch error: {exception}");
            ShowError(Text(
                "تعذر تشغيل جلسة المشارك بأمان.",
                exception.Message));
        }
        finally
        {
            LaunchParticipantButton.IsEnabled = true;
        }
    }


    private async Task<bool> TryShowExistingReadySessionAsync(
        string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        var session = await ExperimentSessionRepository
            .GetByIdAsync(sessionId);
        if (session is null ||
            !string.Equals(
                session.Status,
                SessionLifecycleStates.Ready,
                StringComparison.Ordinal))
        {
            return false;
        }

        var snapshot = await ExperimentConfigurationSnapshotRepository
            .GetBySessionAsync(session.Id);
        if (snapshot is null ||
            SnapshotIntegrityService.Verify(snapshot).Status ==
                SnapshotIntegrityStatus.Invalid)
        {
            ShowError(Text(
                "تعذر التحقق من لقطة الجلسة الجاهزة.",
                "The existing Ready session snapshot could not be verified."));
            return true;
        }

        _preparedSessionId = session.Id;
        LaunchResultTitle.Text = Text(
            "جلسة جاهزة موجودة",
            "Existing session ready");
        LaunchResultText.Text = Text(
            $"الحالة: جاهزة\nالمحفزات: {snapshot.Stimuli.Count}",
            $"Status: Ready\nStimuli: {snapshot.Stimuli.Count}");
        LaunchResultPanel.IsVisible = true;
        LaunchParticipantButton.IsVisible = true;
        return true;
    }


    private void RenderReadiness(
        ExperimentReadinessResult result)
    {
        ReadinessContainer.Children.Clear();

        ReadinessStatus.Text = result.IsReady
            ? Text("جاهزة مبدئيا", "Foundation ready")
            : Text(
                $"{result.ErrorCount} أخطاء",
                $"{result.ErrorCount} errors");

        foreach (var check in result.Checks)
        {
            var color = check.IsPassed
                ? "#299778"
                : check.Severity == ExperimentReadinessSeverity.Error
                    ? "#C43C55"
                    : "#C8872D";

            ReadinessContainer.Children.Add(new StatusIndicatorView(
                LocalizeReadiness(check), color));
        }
    }


    private void ConfigureStrategyControls()
    {
        ConditionComboBox.IsEnabled =
            string.Equals(
                GetSelectedValue(StrategyComboBox),
                ConditionAssignmentStrategy.Manual.ToString(),
                StringComparison.Ordinal);
    }

    private void PopulateDisplays()
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        var count = window?.Screens?.All.Count ?? 1;
        ParticipantDisplayComboBox.ItemsSource = Enumerable.Range(0, Math.Max(1, count))
            .Select(index => new DisplayChoice(
                index,
                Text($"الشاشة {index + 1}", $"Display {index + 1}")))
            .ToList();
        ParticipantDisplayComboBox.SelectedIndex = 0;
    }


    private Participant? GetSelectedParticipant()
    {
        var participantId =
            GetSelectedValue(ParticipantComboBox);

        return _participants.FirstOrDefault(participant =>
            string.Equals(
                participant.Id,
                participantId,
                StringComparison.Ordinal));
    }


    private string BuildManipulationSummary(
        ConditionManipulationSettings settings)
    {
        return Text(
            $"التفاعل: {(settings.ShowEngagementMetrics ? "ظاهر" : "مخفي")} • الإعجابات: {LocalizeMode(settings.LikesMode)} • التعليقات: {LocalizeMode(settings.CommentsMode)} • الترتيب: {LocalizeOrder(settings.ContentOrderMode)}",
            $"Engagement: {(settings.ShowEngagementMetrics ? "shown" : "hidden")} • Likes: {settings.LikesMode} • Comments: {settings.CommentsMode} • Order: {settings.ContentOrderMode}");
    }


    private string BuildParticipantDisplay(
        Participant participant)
    {
        var status = participant.IsExcluded
            ? Text("مستبعد", "excluded")
            : participant.HasWithdrawn
                ? Text("منسحب", "withdrawn")
                : participant.Status;

        return $"{participant.ParticipantCode} — {status}";
    }


    private string LocalizeFailure(
        ExperimentLaunchFailure failure)
    {
        if (!LocalizationService.IsArabic)
        {
            return failure.Message;
        }

        return failure.Code switch
        {
            "participant.not_found" =>
                "المشارك لا ينتمي إلى هذه الدراسة.",
            "study.title" =>
                "عنوان الدراسة مطلوب.",
            "groups.active" =>
                "يجب توفير مجموعة نشطة واحدة على الأقل.",
            "conditions.active" =>
                "يجب توفير شرط تجريبي نشط واحد على الأقل.",
            "stimuli.active" =>
                "يجب توفير محفز نشط عند تفعيل المحفزات.",
            "conditions.links" =>
                "توجد روابط غير صالحة بين الشروط والمجموعات.",
            "assignment.method" =>
                "طريقة التوزيع مطلوبة.",
            "participant.ineligible" =>
                "المشارك غير مؤهل.",
            "participant.excluded" =>
                "المشارك مستبعد.",
            "participant.withdrawn" =>
                "المشارك منسحب.",
            "participant.consent_required" =>
                "موافقة المشارك مطلوبة.",
            "participant.group_invalid" =>
                "يجب تعيين المشارك إلى مجموعة نشطة.",
            "condition.none_eligible" =>
                "لا يوجد شرط نشط متوافق مع مجموعة المشارك.",
            "condition.manual_required" =>
                "اختر شرطا للتعيين اليدوي.",
            "condition.manual_incompatible" =>
                "الشرط المحدد غير متوافق مع مجموعة المشارك.",
            "stimuli.none_for_group" =>
                "لا توجد محفزات نشطة متاحة لمجموعة المشارك.",
            "session.active_exists" =>
                "توجد بالفعل جلسة جاهزة أو نشطة لهذا المشارك.",
            _ => failure.Message
        };
    }


    private string LocalizeReadiness(
        ExperimentReadinessCheck check)
    {
        return check.Code switch
        {
            "study.title" => Text(
                "عنوان الدراسة موجود",
                "Study title configured"),
            "groups.active" => Text(
                "توجد مجموعة نشطة",
                "Active group available"),
            "conditions.active" => Text(
                "يوجد شرط نشط",
                "Active condition available"),
            "stimuli.active" => Text(
                "المحفزات متاحة عند الحاجة",
                "Stimuli available when required"),
            "conditions.links" => Text(
                "روابط الشروط صالحة",
                "Condition links valid"),
            "sample.target" => Text(
                "العينة المستهدفة محددة",
                "Target sample configured"),
            "assignment.method" => Text(
                "طريقة التوزيع محددة",
                "Assignment method configured"),
            "questionnaires.module" => Text(
                _study.UsesQuestionnaires
                    ? "وحدة الاستبيان مفعلة والتحقق التفصيلي لاحقا"
                    : "وحدة الاستبيان غير مفعلة",
                check.CanonicalMessage),
            "physiological.module" => Text(
                _study.UsesPhysiologicalData
                    ? "الوحدة الفسيولوجية مفعلة والتحقق التفصيلي لاحقا"
                    : "الوحدة الفسيولوجية غير مفعلة",
                check.CanonicalMessage),
            _ => check.CanonicalMessage
        };
    }


    private static string LocalizeMode(
        MetricManipulationMode mode)
    {
        if (!LocalizationService.IsArabic)
        {
            return mode.ToString();
        }

        return mode switch
        {
            MetricManipulationMode.Original => "أصلي",
            MetricManipulationMode.Hidden => "مخفي",
            MetricManipulationMode.Fixed => "ثابت",
            MetricManipulationMode.Multiplier => "مضاعف",
            MetricManipulationMode.RandomRange => "نطاق عشوائي",
            _ => mode.ToString()
        };
    }


    private static string LocalizeOrder(
        ContentOrderMode mode)
    {
        if (!LocalizationService.IsArabic)
        {
            return mode.ToString();
        }

        return mode switch
        {
            ContentOrderMode.Original => "أصلي",
            ContentOrderMode.Chronological => "زمني",
            ContentOrderMode.ReverseChronological => "زمني عكسي",
            ContentOrderMode.Random => "عشوائي",
            ContentOrderMode.Popularity => "حسب الشعبية",
            ContentOrderMode.Custom => "مخصص",
            _ => mode.ToString()
        };
    }


    private void ShowError(string message)
    {
        LaunchErrorText.Text = message;
        LaunchErrorText.TextAlignment = LocalizationService.IsArabic
            ? TextAlignment.Right
            : TextAlignment.Left;
        LaunchErrorText.IsVisible = true;
    }


    private void ConfigureLanguage()
    {
        var isArabic = LocalizationService.IsArabic;

        RootSessionLaunch.FlowDirection = isArabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        RootSessionLaunch.FontFamily = CurrentFont;

        PageTitle.Text = Text(
            "تحضير جلسة المشارك",
            "Prepare participant session");
        PageSubtitle.Text = Text(
            "تحقق من الجاهزية والتعيين وأنشئ لقطة تكوين ثابتة قبل بدء التجربة.",
            "Validate readiness and assignment, then create an immutable configuration snapshot before the experiment starts.");
        PreparationTitle.Text = Text(
            "إعداد الإطلاق",
            "Launch preparation");
        ParticipantLabel.Text = Text("المشارك", "Participant");
        GroupLabel.Text = Text("مجموعة المشارك", "Participant group");
        StrategyLabel.Text = Text(
            "استراتيجية تعيين الشرط",
            "Condition assignment strategy");
        ConditionLabel.Text = Text(
            "الشرط اليدوي",
            "Manual condition");
        ParticipantDisplayLabel.Text = Text(
            "شاشة المشارك",
            "Participant display");
        AssignedConditionLabel.Text = Text(
            "الشرط المعين حاليا",
            "Current assigned condition");
        StimuliPreviewLabel.Text = Text(
            "عدد المحفزات",
            "Stimulus count");
        ManipulationPreviewLabel.Text = Text(
            "ملخص المعالجة",
            "Manipulation summary");
        PrepareSessionButtonText.Text = Text(
            "تحضير الجلسة",
            "Prepare session");
        LaunchParticipantButtonText.Text = Text(
            "بدء جلسة المشارك",
            "Start participant session");
        ReadinessTitle.Text = Text(
            "جاهزية التجربة",
            "Experiment readiness");
        SessionsOverviewTitle.Text = Text("حالات الجلسات", "Sessions by state");
        SessionsOverviewSubtitle.Text = Text(
            "الإجراء المتاح يتغير حسب حالة كل جلسة.",
            "Each session exposes only the action appropriate to its current state.");
        SessionsOverviewEmptyTitle.Text = Text("لا توجد جلسات بعد", "No sessions yet");
        SessionsOverviewEmptyText.Text = Text(
            "اختر مشاركا وتحقق من الجاهزية ثم حضر أول جلسة.",
            "Select a participant, review readiness, and prepare the first session.");
        SessionParticipantColumnText.Text = Text("المشارك", "Participant");
        SessionGroupColumnText.Text = Text("المجموعة", "Group");
        SessionConditionColumnText.Text = Text("الشرط", "Condition");
        SessionStateColumnText.Text = Text("الحالة", "State");
        SessionActionColumnText.Text = Text("الإجراء", "Action");
    }


    private string BuildSessionDetail(ExperimentSession session)
    {
        var timestamp = (session.StartedAtUtc ?? session.CreatedAtUtc).ToLocalTime();
        var duration = session.DurationMilliseconds.HasValue
            ? FormatDuration(session.DurationMilliseconds.Value)
            : "—";
        return Text(
            $"{timestamp:g} · المدة {duration} · التقدم {session.CompletedStimulusCount}",
            $"{timestamp:g} · Duration {duration} · Progress {session.CompletedStimulusCount}");
    }


    private static string SessionActionText(string status) => status switch
    {
        SessionLifecycleStates.Ready => Text("بدء الجلسة", "Start session"),
        SessionLifecycleStates.Completed => Text("عرض الملخص", "View summary"),
        SessionLifecycleStates.Running => Text("فتح التفاصيل", "Open details"),
        SessionLifecycleStates.Paused => Text("فحص الجلسة", "Inspect session"),
        SessionLifecycleStates.Interrupted => Text("فحص الانقطاع", "Inspect interruption"),
        _ => Text("عرض التفاصيل", "View details")
    };


    private static string LocalizeStatus(string status) => status switch
    {
        SessionLifecycleStates.Created => Text("أنشئت", "Created"),
        SessionLifecycleStates.Ready => Text("جاهزة", "Ready"),
        SessionLifecycleStates.Running => Text("قيد التشغيل", "Running"),
        SessionLifecycleStates.Paused => Text("متوقفة مؤقتا", "Paused"),
        SessionLifecycleStates.Completed => Text("مكتملة", "Completed"),
        SessionLifecycleStates.Interrupted => Text("منقطعة", "Interrupted"),
        SessionLifecycleStates.Cancelled => Text("ملغاة", "Cancelled"),
        _ => status
    };


    private static string StatusBackground(string status) => status switch
    {
        SessionLifecycleStates.Ready => "#E9F1FF",
        SessionLifecycleStates.Running => "#E8F7F1",
        SessionLifecycleStates.Completed => "#EAF7F2",
        SessionLifecycleStates.Paused => "#FFF6E5",
        SessionLifecycleStates.Interrupted => "#FFF0ED",
        SessionLifecycleStates.Cancelled => "#F3F4F7",
        _ => "#EEF2F7"
    };


    private static string StatusBorder(string status) => status switch
    {
        SessionLifecycleStates.Ready => "#B8CEF8",
        SessionLifecycleStates.Running or SessionLifecycleStates.Completed => "#B7DECF",
        SessionLifecycleStates.Paused => "#EACF92",
        SessionLifecycleStates.Interrupted => "#E8B8AE",
        _ => "#D3DAE5"
    };


    private static string StatusForeground(string status) => status switch
    {
        SessionLifecycleStates.Ready => "#1D55B5",
        SessionLifecycleStates.Running or SessionLifecycleStates.Completed => "#176A50",
        SessionLifecycleStates.Paused => "#805B14",
        SessionLifecycleStates.Interrupted => "#9C3C31",
        _ => "#526176"
    };


    private static string StatusClass(string status) => status switch
    {
        SessionLifecycleStates.Ready => "ready",
        SessionLifecycleStates.Running => "running",
        SessionLifecycleStates.Paused => "paused",
        SessionLifecycleStates.Completed => "completed",
        SessionLifecycleStates.Interrupted => "interrupted",
        SessionLifecycleStates.Cancelled => "cancelled",
        _ => "draft"
    };


    private static string? GetSelectedValue(
        ComboBox comboBox)
    {
        return (comboBox.SelectedItem as Choice)?.Value;
    }


    private static void SelectChoice(
        ComboBox comboBox,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        comboBox.SelectedItem =
            (comboBox.ItemsSource as IEnumerable<Choice>)?
                .FirstOrDefault(choice =>
                    string.Equals(
                        choice.Value,
                        value,
                        StringComparison.Ordinal));
    }


    private FontFamily CurrentFont =>
        LocalizationService.IsArabic
            ? _arabicFont
            : _englishFont;


    private static string Text(
        string arabic,
        string english)
    {
        return UiTextService.Localized(arabic, english);
    }


    private static SolidColorBrush Brush(string value) =>
        new(Color.Parse(value));


    private static string FormatDuration(long milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss")
            : duration.ToString(@"mm\:ss");
    }


    private sealed record Choice(string Value, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed record DisplayChoice(int Index, string Display)
    {
        public override string ToString() => Display;
    }
}
