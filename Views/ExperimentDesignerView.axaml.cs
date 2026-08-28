using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using SOCYVIA.Models;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

public partial class ExperimentDesignerView : UserControl
{
    public event EventHandler? ExperimentDesignChanged;

    private Study _study = new();
    private List<StudyGroup> _groups = new();
    private List<ExperimentalCondition> _conditions = new();
    private ExperimentalCondition? _editingCondition;
    private PublishedExperimentStatus? _publishedStatus;
    private bool _isSaving;

    private readonly FontFamily _englishFont =
        new("avares://SOCYVIA/Assets/Fonts#IBM Plex Sans");

    private readonly FontFamily _arabicFont =
        new("avares://SOCYVIA/Assets/Fonts#IBM Plex Sans Arabic");


    public ExperimentDesignerView()
    {
        InitializeComponent();
        SetupEvents();
        ConfigureLanguage();
        PopulateStaticSelections();
    }


    public ExperimentDesignerView(
        Study study)
        : this()
    {
        _study = study;

        AttachedToVisualTree +=
            async (_, _) => await ReloadAsync();
    }


    public async Task ReloadAsync()
    {
        try
        {
            var groupsTask =
                GroupManagementService
                    .GetGroupsAsync(_study.Id);

            var conditionsTask =
                ExperimentalConditionService
                    .GetStudyConditionsAsync(_study.Id);

            var summaryTask =
                ExperimentConfigurationService
                    .BuildSummaryAsync(_study);

            var readinessTask =
                ExperimentReadinessService
                    .EvaluateAsync(_study);

            var publicationTask = PublishedExperimentStatusStore.GetAsync(_study.Id);

            await Task.WhenAll(
                groupsTask,
                conditionsTask,
                summaryTask,
                readinessTask,
                publicationTask);

            _groups = groupsTask.Result;
            _conditions = conditionsTask.Result;

            PopulateGroupSelection();
            RenderConditions();
            RenderSummary(summaryTask.Result);
            RenderReadiness(readinessTask.Result);
            _publishedStatus = publicationTask.Result;
            RenderPublishedExperimentStatus();
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Experiment designer load error: {exception}");
        }
    }


    private void SetupEvents()
    {
        AddConditionButton.Click +=
            (_, _) => OpenEditor(null);

        CancelConditionButton.Click +=
            (_, _) => CloseEditor();

        SaveConditionButton.Click +=
            async (_, _) => await SaveAsync();

        LikesModeComboBox.SelectionChanged +=
            (_, _) => ConfigureMetricInputs();

        CommentsModeComboBox.SelectionChanged +=
            (_, _) => ConfigureMetricInputs();

        SharesModeComboBox.SelectionChanged +=
            (_, _) => ConfigureMetricInputs();

        SavesModeComboBox.SelectionChanged +=
            (_, _) => ConfigureMetricInputs();

        ViewsModeComboBox.SelectionChanged +=
            (_, _) => ConfigureMetricInputs();

        CopyPublishedLinkButton.Click += async (_, _) =>
        {
            if (_publishedStatus is null) return;
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard is not null)
                await topLevel.Clipboard.SetTextAsync(_publishedStatus.CanonicalParticipantUrl);
        };
        OpenLiveExperimentButton.Click += (_, _) => OpenPublishedRuntime();
        PreviewPublishedExperimentButton.Click += async (_, _) => await OpenCurrentStudyPreviewAsync();
    }


    private void PopulateStaticSelections()
    {
        ConditionTypeComboBox.ItemsSource =
            new[]
            {
                new Choice("Custom", Text("مخصص", "Custom")),
                new Choice(
                    "HighSocialProof",
                    Text("دليل اجتماعي مرتفع", "High social proof")),
                new Choice(
                    "LowSocialProof",
                    Text("دليل اجتماعي منخفض", "Low social proof")),
                new Choice(
                    "NeutralControl",
                    Text("ضابط محايد", "Neutral control"))
            };

        PopulateMetricModes(LikesModeComboBox);
        PopulateMetricModes(CommentsModeComboBox);
        PopulateMetricModes(SharesModeComboBox);
        PopulateMetricModes(SavesModeComboBox);
        PopulateMetricModes(ViewsModeComboBox);

        ContentOrderComboBox.ItemsSource =
            new[]
            {
                new Choice(
                    ContentOrderMode.Original.ToString(),
                    Text("أصلي", "Original")),
                new Choice(
                    ContentOrderMode.Chronological.ToString(),
                    Text("زمني", "Chronological")),
                new Choice(
                    ContentOrderMode.ReverseChronological.ToString(),
                    Text("زمني عكسي", "Reverse chronological")),
                new Choice(
                    ContentOrderMode.Random.ToString(),
                    Text("عشوائي", "Random")),
                new Choice(
                    ContentOrderMode.Popularity.ToString(),
                    Text("حسب الشعبية", "Popularity"))
            };

        ContentOrderComboBox.SelectedIndex = 0;
    }


    private void PopulateMetricModes(
        ComboBox comboBox)
    {
        comboBox.ItemsSource =
            new[]
            {
                new Choice(
                    MetricManipulationMode.Original.ToString(),
                    Text("أصلي", "Original")),
                new Choice(
                    MetricManipulationMode.Hidden.ToString(),
                    Text("مخفي", "Hidden")),
                new Choice(
                    MetricManipulationMode.Fixed.ToString(),
                    Text("ثابت", "Fixed")),
                new Choice(
                    MetricManipulationMode.Multiplier.ToString(),
                    Text("مضاعف", "Multiplier")),
                new Choice(
                    MetricManipulationMode.RandomRange.ToString(),
                    Text("نطاق عشوائي", "Random range"))
            };

        comboBox.SelectedIndex = 0;
    }


    private void PopulateGroupSelection()
    {
        var selectedGroupId =
            GetSelectedChoiceValue(
                ConditionGroupComboBox);

        var items =
            new List<Choice>
            {
                new(
                    string.Empty,
                    Text("غير مرتبطة بمجموعة", "No group link"))
            };

        items.AddRange(
            _groups.Select(group =>
                new Choice(
                    group.Id,
                    group.IsActive
                        ? group.Name
                        : Text(
                            $"{group.Name} (غير نشطة)",
                            $"{group.Name} (inactive)"))));

        ConditionGroupComboBox.ItemsSource = items;
        SelectChoice(
            ConditionGroupComboBox,
            selectedGroupId ?? string.Empty);
    }


    private void OpenEditor(
        ExperimentalCondition? condition)
    {
        _editingCondition = condition;

        ConditionEditorTitle.Text = condition is null
            ? Text("شرط تجريبي جديد", "New experimental condition")
            : Text("تعديل الشرط التجريبي", "Edit experimental condition");

        ConditionNameBox.Text = condition?.Name ?? string.Empty;
        ConditionDescriptionBox.Text =
            condition?.Description ?? string.Empty;

        EnsureConditionTypeChoice(
            condition?.ConditionType ?? "Custom");

        SelectChoice(
            ConditionTypeComboBox,
            condition?.ConditionType ?? "Custom");

        SelectChoice(
            ConditionGroupComboBox,
            condition?.GroupId ?? string.Empty);

        ControlConditionCheckBox.IsChecked =
            condition?.IsControlCondition ?? false;

        ActiveConditionCheckBox.IsChecked =
            condition?.IsActive ?? true;

        ApplyManipulationSettings(
            ConditionManipulationService.Deserialize(
                condition?.ManipulationJson));

        ConditionEditorErrorText.IsVisible = false;
        ConditionEditorPanel.IsVisible = true;
    }


    private void CloseEditor()
    {
        _editingCondition = null;
        ConditionEditorPanel.IsVisible = false;
        ConditionEditorErrorText.IsVisible = false;
    }


    private async Task SaveAsync()
    {
        if (_isSaving)
        {
            return;
        }

        _isSaving = true;
        SaveConditionButton.IsEnabled = false;
        ConditionEditorErrorText.IsVisible = false;

        try
        {
            var settings =
                ReadManipulationSettings();

            var manipulationJson =
                ConditionManipulationService
                    .Serialize(settings);

            var conditionType =
                GetSelectedChoiceValue(
                    ConditionTypeComboBox)
                ?? "Custom";

            var groupId =
                GetSelectedChoiceValue(
                    ConditionGroupComboBox);

            if (string.IsNullOrWhiteSpace(groupId))
            {
                groupId = null;
            }

            if (_editingCondition is null)
            {
                await ExperimentalConditionService
                    .CreateConditionAsync(
                        _study.Id,
                        ConditionNameBox.Text ?? string.Empty,
                        conditionType,
                        groupId,
                        ConditionDescriptionBox.Text,
                        ControlConditionCheckBox.IsChecked == true,
                        manipulationJson,
                        ActiveConditionCheckBox.IsChecked == true);
            }
            else
            {
                var updated =
                    new ExperimentalCondition
                    {
                        Id = _editingCondition.Id,
                        StudyId = _editingCondition.StudyId,
                        GroupId = groupId,
                        Name = ConditionNameBox.Text ?? string.Empty,
                        Description = ConditionDescriptionBox.Text,
                        ConditionType = conditionType,
                        SortOrder = _editingCondition.SortOrder,
                        IsControlCondition =
                            ControlConditionCheckBox.IsChecked == true,
                        IsActive =
                            ActiveConditionCheckBox.IsChecked == true,
                        ManipulationJson = manipulationJson,
                        CreatedAtUtc = _editingCondition.CreatedAtUtc,
                        UpdatedAtUtc = _editingCondition.UpdatedAtUtc
                    };

                await ExperimentalConditionService
                    .UpdateConditionAsync(updated);
            }

            CloseEditor();
            await ReloadAsync();
            ExperimentDesignChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Condition save error: {exception}");

            ConditionEditorErrorText.Text =
                LocalizationService.IsArabic
                    ? "تعذر حفظ الشرط. تحقق من الاسم وقيم المعالجة."
                    : exception.Message;

            ConditionEditorErrorText.IsVisible = true;
        }
        finally
        {
            _isSaving = false;
            SaveConditionButton.IsEnabled = true;
        }
    }


    private ConditionManipulationSettings
        ReadManipulationSettings()
    {
        var existingSettings =
            ConditionManipulationService.Deserialize(
                _editingCondition?.ManipulationJson);

        existingSettings.ShowEngagementMetrics =
            ShowEngagementMetricsCheckBox.IsChecked == true;

        existingSettings.LikesMode =
            ReadMetricMode(LikesModeComboBox);
        existingSettings.LikesFixedValue =
            ReadLong(LikesFixedBox);
        existingSettings.LikesMultiplier =
            ReadDouble(LikesMultiplierBox);
        existingSettings.LikesRandomMin =
            ReadLong(LikesMinBox);
        existingSettings.LikesRandomMax =
            ReadLong(LikesMaxBox);

        existingSettings.CommentsMode =
            ReadMetricMode(CommentsModeComboBox);
        existingSettings.CommentsFixedValue =
            ReadLong(CommentsFixedBox);
        existingSettings.CommentsMultiplier =
            ReadDouble(CommentsMultiplierBox);
        existingSettings.CommentsRandomMin =
            ReadLong(CommentsMinBox);
        existingSettings.CommentsRandomMax =
            ReadLong(CommentsMaxBox);

        existingSettings.SharesMode =
            ReadMetricMode(SharesModeComboBox);
        existingSettings.SharesFixedValue =
            ReadLong(SharesFixedBox);
        existingSettings.SharesMultiplier =
            ReadDouble(SharesMultiplierBox);
        existingSettings.SharesRandomMin =
            ReadLong(SharesMinBox);
        existingSettings.SharesRandomMax =
            ReadLong(SharesMaxBox);

        existingSettings.SavesMode =
            ReadMetricMode(SavesModeComboBox);
        existingSettings.SavesFixedValue = ReadLong(SavesFixedBox);
        existingSettings.SavesMultiplier = ReadDouble(SavesMultiplierBox);
        existingSettings.SavesRandomMin = ReadLong(SavesMinBox);
        existingSettings.SavesRandomMax = ReadLong(SavesMaxBox);

        existingSettings.ViewsMode =
            ReadMetricMode(ViewsModeComboBox);
        existingSettings.ViewsFixedValue =
            ReadLong(ViewsFixedBox);
        existingSettings.ViewsMultiplier =
            ReadDouble(ViewsMultiplierBox);
        existingSettings.ViewsRandomMin =
            ReadLong(ViewsMinBox);
        existingSettings.ViewsRandomMax =
            ReadLong(ViewsMaxBox);

        var contentOrderValue =
            GetSelectedChoiceValue(
                ContentOrderComboBox)
            ?? ContentOrderMode.Original.ToString();

        existingSettings.ContentOrderMode =
            Enum.Parse<ContentOrderMode>(
                contentOrderValue);

        existingSettings.ShowAuthor =
            ShowAuthorCheckBox.IsChecked == true;
        existingSettings.ShowTimestamp =
            ShowTimestampCheckBox.IsChecked == true;
        existingSettings.ShowPlatformIdentity =
            ShowPlatformCheckBox.IsChecked == true;
        existingSettings.CustomPresentationJson =
            NormalizeOptional(CustomPresentationBox.Text);

        return existingSettings;
    }


    private void ApplyManipulationSettings(
        ConditionManipulationSettings settings)
    {
        ShowEngagementMetricsCheckBox.IsChecked =
            settings.ShowEngagementMetrics;

        SelectChoice(
            LikesModeComboBox,
            settings.LikesMode.ToString());
        LikesFixedBox.Value = settings.LikesFixedValue;
        LikesMultiplierBox.Value = ToDecimal(settings.LikesMultiplier);
        LikesMinBox.Value = settings.LikesRandomMin;
        LikesMaxBox.Value = settings.LikesRandomMax;

        SelectChoice(
            CommentsModeComboBox,
            settings.CommentsMode.ToString());
        CommentsFixedBox.Value = settings.CommentsFixedValue;
        CommentsMultiplierBox.Value =
            ToDecimal(settings.CommentsMultiplier);
        CommentsMinBox.Value = settings.CommentsRandomMin;
        CommentsMaxBox.Value = settings.CommentsRandomMax;

        SelectChoice(
            SharesModeComboBox,
            settings.SharesMode.ToString());
        SharesFixedBox.Value = settings.SharesFixedValue;
        SharesMultiplierBox.Value =
            ToDecimal(settings.SharesMultiplier);
        SharesMinBox.Value = settings.SharesRandomMin;
        SharesMaxBox.Value = settings.SharesRandomMax;

        SelectChoice(SavesModeComboBox, settings.SavesMode.ToString());
        SavesFixedBox.Value = settings.SavesFixedValue;
        SavesMultiplierBox.Value = ToDecimal(settings.SavesMultiplier);
        SavesMinBox.Value = settings.SavesRandomMin;
        SavesMaxBox.Value = settings.SavesRandomMax;

        SelectChoice(
            ViewsModeComboBox,
            settings.ViewsMode.ToString());
        ViewsFixedBox.Value = settings.ViewsFixedValue;
        ViewsMultiplierBox.Value =
            ToDecimal(settings.ViewsMultiplier);
        ViewsMinBox.Value = settings.ViewsRandomMin;
        ViewsMaxBox.Value = settings.ViewsRandomMax;

        EnsureContentOrderChoice(settings.ContentOrderMode);
        SelectChoice(
            ContentOrderComboBox,
            settings.ContentOrderMode.ToString());

        ShowAuthorCheckBox.IsChecked = settings.ShowAuthor;
        ShowTimestampCheckBox.IsChecked = settings.ShowTimestamp;
        ShowPlatformCheckBox.IsChecked =
            settings.ShowPlatformIdentity;
        CustomPresentationBox.Text =
            settings.CustomPresentationJson ?? string.Empty;

        ConfigureMetricInputs();
    }


    private void ConfigureMetricInputs()
    {
        ConfigureMetricInputs(
            LikesModeComboBox,
            LikesFixedBox,
            LikesMultiplierBox,
            LikesMinBox,
            LikesMaxBox);

        ConfigureMetricInputs(
            CommentsModeComboBox,
            CommentsFixedBox,
            CommentsMultiplierBox,
            CommentsMinBox,
            CommentsMaxBox);

        ConfigureMetricInputs(
            SharesModeComboBox,
            SharesFixedBox,
            SharesMultiplierBox,
            SharesMinBox,
            SharesMaxBox);

        ConfigureMetricInputs(
            SavesModeComboBox,
            SavesFixedBox,
            SavesMultiplierBox,
            SavesMinBox,
            SavesMaxBox);

        ConfigureMetricInputs(
            ViewsModeComboBox,
            ViewsFixedBox,
            ViewsMultiplierBox,
            ViewsMinBox,
            ViewsMaxBox);
    }


    private static void ConfigureMetricInputs(
        ComboBox modeBox,
        NumericUpDown fixedBox,
        NumericUpDown multiplierBox,
        NumericUpDown minBox,
        NumericUpDown maxBox)
    {
        var mode =
            ReadMetricMode(modeBox);

        fixedBox.IsEnabled =
            mode == MetricManipulationMode.Fixed;
        multiplierBox.IsEnabled =
            mode == MetricManipulationMode.Multiplier;
        minBox.IsEnabled =
            mode == MetricManipulationMode.RandomRange;
        maxBox.IsEnabled =
            mode == MetricManipulationMode.RandomRange;
    }


    private void RenderConditions()
    {
        ConditionsContainer.Children.Clear();
        ConditionsEmptyText.IsVisible = _conditions.Count == 0;

        foreach (var condition in _conditions)
        {
            ConditionsContainer.Children.Add(
                BuildConditionRow(condition));
        }
    }


    private Control BuildConditionRow(
        ExperimentalCondition condition)
    {
        var linkedGroup =
            _groups.FirstOrDefault(group =>
                string.Equals(
                    group.Id,
                    condition.GroupId,
                    StringComparison.Ordinal));

        var name =
            new TextBlock
            {
                Text = condition.Name,
                FontFamily = CurrentFont,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush("#203451")
            };

        var details =
            new TextBlock
            {
                Text = BuildConditionDetails(
                    condition,
                    linkedGroup),
                FontFamily = CurrentFont,
                FontSize = 8.2,
                Foreground = Brush("#7E899A"),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 500
            };

        ApplyReadingDirection(name);
        ApplyReadingDirection(details);

        var information =
            new StackPanel
            {
                Spacing = 3,
                VerticalAlignment = VerticalAlignment.Center
            };

        information.Children.Add(name);
        information.Children.Add(details);

        var actions =
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                VerticalAlignment = VerticalAlignment.Center
            };

        actions.Children.Add(
            ActionButton(
                Text("تعديل", "Edit"),
                (_, _) => OpenEditor(condition)));

        actions.Children.Add(
            ActionButton(
                Text("لأعلى", "Move up"),
                async (_, _) => await MoveAsync(condition, -1)));

        actions.Children.Add(
            ActionButton(
                Text("لأسفل", "Move down"),
                async (_, _) => await MoveAsync(condition, 1)));

        actions.Children.Add(
            ActionButton(
                Text("حذف", "Delete"),
                async (_, _) => await DeleteAsync(condition)));

        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("*,12,Auto")
            };

        Grid.SetColumn(information, 0);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(information);
        grid.Children.Add(actions);

        return new Border
        {
            Padding = new Thickness(13, 10),
            Background = Brush(
                condition.IsActive
                    ? "#FBFCFE"
                    : "#F4F5F8"),
            BorderBrush = Brush("#E3E9F3"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = grid,
            Opacity = condition.IsActive ? 1 : 0.72
        };
    }


    private string BuildConditionDetails(
        ExperimentalCondition condition,
        StudyGroup? linkedGroup)
    {
        var parts =
            new List<string>
            {
                condition.ConditionType
            };

        parts.Add(linkedGroup is null
            ? Text("غير مرتبطة بمجموعة", "No group link")
            : Text(
                $"المجموعة: {linkedGroup.Name}",
                $"Group: {linkedGroup.Name}"));

        if (condition.IsControlCondition)
        {
            parts.Add(Text("شرط ضابط", "Control condition"));
        }

        if (!condition.IsActive)
        {
            parts.Add(Text("غير نشط", "Inactive"));
        }

        if (!string.IsNullOrWhiteSpace(condition.Description))
        {
            parts.Add(condition.Description);
        }

        return string.Join("  •  ", parts);
    }


    private async Task MoveAsync(
        ExperimentalCondition condition,
        int direction)
    {
        try
        {
            await ExperimentalConditionService
                .MoveConditionAsync(
                    _study.Id,
                    condition.Id,
                    direction);

            await ReloadAsync();
            ExperimentDesignChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Condition reorder error: {exception}");
        }
    }


    private async Task DeleteAsync(
        ExperimentalCondition condition)
    {
        var confirmed =
            await ConfirmAsync(
                Text("حذف الشرط", "Delete condition"),
                Text(
                    "هل تريد حذف هذا الشرط التجريبي؟ لن تتغير بيانات المنشورات الأصلية.",
                    "Delete this experimental condition? Original stimulus data will not be changed."));

        if (!confirmed)
        {
            return;
        }

        try
        {
            await ExperimentalConditionService
                .DeleteConditionAsync(condition.Id);

            CloseEditor();
            await ReloadAsync();
            ExperimentDesignChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Condition delete error: {exception}");
        }
    }


    private void RenderSummary(
        ExperimentConfigurationSummary summary)
    {
        SummaryDesignValue.Text =
            LocalizeDesign(summary.StudyDesign);
        SummaryGroupsValue.Text = summary.GroupCount.ToString();
        SummaryConditionsValue.Text =
            summary.ActiveConditionCount.ToString();

        SummaryControlValue.Text =
            summary.ControlGroupName is null &&
            summary.ControlConditionName is null
                ? "—"
                : string.Join(
                    " / ",
                    new[]
                    {
                        summary.ControlGroupName,
                        summary.ControlConditionName
                    }.Where(value =>
                        !string.IsNullOrWhiteSpace(value)));

        SummaryTargetValue.Text =
            summary.TargetSampleSize?.ToString() ?? "—";
        SummaryAssignmentValue.Text =
            LocalizeAssignment(summary.AssignmentMethod);
        SummaryStimuliValue.Text =
            summary.StimulusCount.ToString();
        SummaryRandomizationValue.Text =
            summary.RandomizeStimuli
                ? Text("مفعلة", "Enabled")
                : Text("غير مفعلة", "Disabled");
        SummaryModulesValue.Text = Text(
            summary.PhysiologicalModuleEnabled
                ? $"الاستبيان {OnOff(summary.QuestionnaireModuleEnabled)} • EEG و GSR/EDA وتتبع العين دون موصل مهيأ"
                : $"الاستبيان {OnOff(summary.QuestionnaireModuleEnabled)} • الوحدات الفسيولوجية غير مفعلة",
            summary.PhysiologicalModuleEnabled
                ? $"Questionnaire {OnOff(summary.QuestionnaireModuleEnabled)} • EEG, GSR/EDA, Eye Tracking: no connector configured"
                : $"Questionnaire {OnOff(summary.QuestionnaireModuleEnabled)} • Physiological modules disabled");
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
                : check.Severity ==
                  ExperimentReadinessSeverity.Error
                    ? "#C43C55"
                    : "#C8872D";

            ReadinessContainer.Children.Add(new StatusIndicatorView(
                LocalizeReadiness(check), color));
        }
    }

    private void RenderPublishedExperimentStatus()
    {
        // Publication status belongs to the dedicated Publish stage. Keeping
        // this designer panel hidden prevents a draft/design screen from
        // becoming a second, competing participant-link surface.
        PublishedExperimentPanel.IsVisible = false;
    }

    private void OpenPublishedRuntime()
    {
        if (_publishedStatus is null) return;
        try { SocyviaProductUrls.OpenInDefaultBrowser(new Uri(_publishedStatus.RuntimeParticipantUrl)); }
        catch (Exception exception) { ApplicationDiagnosticsService.LogException(exception, "Open published experiment"); }
    }

    private async Task OpenCurrentStudyPreviewAsync()
    {
        if (_study is null) return;
        var group = _groups.Where(item => item.IsActive).OrderBy(item => item.SortOrder).FirstOrDefault();
        var condition = _conditions.Where(item => item.IsActive && item.GroupId == group?.Id)
            .OrderBy(item => item.SortOrder).FirstOrDefault();
        if (group is null || condition is null)
        {
            return;
        }

        try { await BrowserParticipantPreviewService.OpenAsync(_study, group, condition); }
        catch (Exception exception) { ApplicationDiagnosticsService.LogException(exception, "Open current-study participant preview"); }
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
                "توجد مجموعة نشطة واحدة على الأقل",
                "At least one active group"),
            "conditions.active" => Text(
                "يوجد شرط تجريبي نشط واحد على الأقل",
                "At least one active condition"),
            "stimuli.active" => Text(
                "المحفزات النشطة متاحة عند الحاجة",
                "Active stimuli available when required"),
            "conditions.links" => Text(
                "روابط الشروط بالمجموعات صالحة",
                "Condition-to-group links are valid"),
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


    private async Task<bool> ConfirmAsync(
        string title,
        string message)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return false;
        }

        var dialog =
            new Window
            {
                Title = title,
                Background = Brush("#F7F9FD")
            };

        WindowAppearanceService.ConfigureCompactDialog(
            dialog,
            430,
            220);

        var messageBlock =
            new TextBlock
            {
                Text = message,
                FontFamily = CurrentFont,
                FontSize = 10,
                Foreground = Brush("#31435F"),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = LocalizationService.IsArabic
                    ? TextAlignment.Right
                    : TextAlignment.Left
            };

        var cancel = DialogButton(Text("إلغاء", "Cancel"));
        var confirm = DialogButton(Text("حذف", "Delete"));
        confirm.Background = Brush("#2563EB");
        confirm.Foreground = Brush("#FFFFFF");

        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);

        var actions =
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 8
            };

        actions.Children.Add(cancel);
        actions.Children.Add(confirm);

        var content =
            new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 22,
                FlowDirection = LocalizationService.IsArabic
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight
            };

        content.Children.Add(messageBlock);
        content.Children.Add(actions);
        dialog.Content = content;

        return await dialog.ShowDialog<bool>(owner);
    }


    private Button ActionButton(
        string text,
        EventHandler<Avalonia.Interactivity.RoutedEventArgs> handler)
    {
        var button = DialogButton(text);
        button.MinWidth = 38;
        button.MinHeight = 28;
        button.Padding = new Thickness(8, 4);
        button.FontSize = 8;
        button.Click += handler;
        return button;
    }


    private Button DialogButton(
        string text)
    {
        return new Button
        {
            Content = text,
            FontFamily = CurrentFont,
            FontSize = 9,
            MinWidth = 90,
            MinHeight = 34,
            Background = Brush("#FFFFFF"),
            Foreground = Brush("#31435F"),
            BorderBrush = Brush("#DCE3EE"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
    }


    private void EnsureConditionTypeChoice(
        string conditionType)
    {
        var choices =
            (ConditionTypeComboBox.ItemsSource as IEnumerable<Choice>)?
                .ToList()
            ?? new List<Choice>();

        if (choices.Any(choice =>
                string.Equals(
                    choice.Value,
                    conditionType,
                    StringComparison.Ordinal)))
        {
            return;
        }

        choices.Add(new Choice(conditionType, conditionType));
        ConditionTypeComboBox.ItemsSource = choices;
    }


    private void EnsureContentOrderChoice(
        ContentOrderMode mode)
    {
        var choices =
            (ContentOrderComboBox.ItemsSource as IEnumerable<Choice>)?
                .ToList()
            ?? new List<Choice>();

        if (choices.Any(choice =>
                choice.Value == mode.ToString()))
        {
            return;
        }

        choices.Add(
            new Choice(
                mode.ToString(),
                Text("مخصص", "Custom")));

        ContentOrderComboBox.ItemsSource = choices;
    }


    private static MetricManipulationMode ReadMetricMode(
        ComboBox comboBox)
    {
        var value =
            GetSelectedChoiceValue(comboBox)
            ?? MetricManipulationMode.Original.ToString();

        return Enum.Parse<MetricManipulationMode>(value);
    }


    private static string? GetSelectedChoiceValue(
        ComboBox comboBox)
    {
        return (comboBox.SelectedItem as Choice)?.Value;
    }


    private static void SelectChoice(
        ComboBox comboBox,
        string value)
    {
        var choices =
            comboBox.ItemsSource as IEnumerable<Choice>;

        comboBox.SelectedItem =
            choices?.FirstOrDefault(choice =>
                string.Equals(
                    choice.Value,
                    value,
                    StringComparison.Ordinal));

        if (comboBox.SelectedItem is null &&
            comboBox.ItemCount > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }


    private static long? ReadLong(
        NumericUpDown box)
    {
        return box.Value.HasValue
            ? decimal.ToInt64(box.Value.Value)
            : null;
    }


    private static double? ReadDouble(
        NumericUpDown box)
    {
        return box.Value.HasValue
            ? decimal.ToDouble(box.Value.Value)
            : null;
    }


    private static decimal? ToDecimal(
        double? value)
    {
        return value.HasValue
            ? Convert.ToDecimal(value.Value)
            : null;
    }


    private void ConfigureLanguage()
    {
        var isArabic = LocalizationService.IsArabic;

        RootExperimentDesigner.FlowDirection = isArabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        RootExperimentDesigner.FontFamily = CurrentFont;

        PageTitle.Text = Text("تصميم التجربة", "Experiment design");
        PageSubtitle.Text = Text(
            "راجع بنية الدراسة وأدر الشروط التجريبية وقواعد العرض دون تغيير بيانات المحفزات الأصلية.",
            "Review the study structure and manage experimental conditions and presentation rules without changing original stimulus data.");
        SummaryTitle.Text = Text(
            "ملخص التكوين",
            "Configuration summary");
        ReadinessTitle.Text = Text(
            "جاهزية التجربة",
            "Experiment readiness");
        ConditionsTitle.Text = Text(
            "الشروط التجريبية",
            "Experimental conditions");
        ConditionsSubtitle.Text = Text(
            "تدار الشروط التجريبية منفصلة عن مجموعات المشاركين، مع ربط اختياري عند الحاجة إلى التصميم.",
            "Experimental conditions are managed separately from participant groups, with an optional group link when required by the design.");
        AddConditionButtonText.Text = Text(
            "إضافة شرط",
            "Add condition");
        ConditionsEmptyText.Text = Text(
            "لا توجد شروط تجريبية بعد.",
            "No experimental conditions yet.");

        SummaryDesignLabel.Text = Text("تصميم الدراسة", "Study design");
        SummaryGroupsLabel.Text = Text("المجموعات", "Groups");
        SummaryConditionsLabel.Text = Text(
            "الشروط النشطة",
            "Active conditions");
        SummaryControlLabel.Text = Text("الضبط", "Control");
        SummaryTargetLabel.Text = Text(
            "العينة المستهدفة",
            "Target sample");
        SummaryAssignmentLabel.Text = Text(
            "طريقة التوزيع",
            "Assignment method");
        SummaryStimuliLabel.Text = Text("المحفزات", "Stimuli");
        SummaryRandomizationLabel.Text = Text(
            "العشوائية",
            "Randomization");
        SummaryModulesLabel.Text = Text("الوحدات", "Modules");

        ConditionNameLabel.Text = Text("اسم الشرط", "Condition name");
        ConditionTypeLabel.Text = Text("نوع الشرط", "Condition type");
        ConditionGroupLabel.Text = Text("المجموعة", "Group link");
        ConditionDescriptionLabel.Text = Text("الوصف", "Description");
        ControlConditionCheckBox.Content = Text(
            "شرط ضابط",
            "Control condition");
        ActiveConditionCheckBox.Content = Text("نشط", "Active");

        ManipulationTitle.Text = Text(
            "إعدادات المعالجة",
            "Manipulation settings");
        ManipulationSubtitle.Text = Text(
            "تؤثر هذه القواعد في العرض التجريبي فقط ولا تستبدل المقاييس الأصلية للمنشور.",
            "These rules affect experimental presentation only and never overwrite original post metrics.");
        ShowEngagementMetricsCheckBox.Content = Text(
            "إظهار مقاييس التفاعل",
            "Show engagement metrics");

        MetricHeaderLabel.Text = Text("المقياس", "Metric");
        ModeHeaderLabel.Text = Text("الوضع", "Mode");
        FixedHeaderLabel.Text = Text("قيمة ثابتة", "Fixed value");
        MultiplierHeaderLabel.Text = Text("المضاعف", "Multiplier");
        MinimumHeaderLabel.Text = Text("الحد الأدنى", "Random min");
        MaximumHeaderLabel.Text = Text("الحد الأعلى", "Random max");
        LikesLabel.Text = Text("الإعجابات", "Likes");
        CommentsLabel.Text = Text("التعليقات", "Comments");
        SharesLabel.Text = Text("عمليات المشاركة", "Shares");
        SavesLabel.Text = Text("الحفظ", "Saves");
        ViewsLabel.Text = Text("المشاهدات", "Views");
        ContentOrderLabel.Text = Text(
            "ترتيب المحتوى",
            "Content order");
        VisibilityLabel.Text = Text("عناصر العرض", "Visibility");
        ShowAuthorCheckBox.Content = Text("الكاتب", "Author");
        ShowTimestampCheckBox.Content = Text("الوقت", "Timestamp");
        ShowPlatformCheckBox.Content = Text("المنصة", "Platform identity");
        CustomPresentationLabel.Text = Text(
            "إعداد عرض مخصص (اختياري)",
            "Custom presentation JSON (optional)");
        CancelConditionButtonText.Text = Text("إلغاء", "Cancel");
        SaveConditionButtonText.Text = Text("حفظ", "Save");

        ConditionNameBox.TextAlignment = isArabic
            ? TextAlignment.Right
            : TextAlignment.Left;
        ConditionDescriptionBox.TextAlignment = isArabic
            ? TextAlignment.Right
            : TextAlignment.Left;
        CustomPresentationBox.TextAlignment = isArabic
            ? TextAlignment.Right
            : TextAlignment.Left;
    }


    private string LocalizeDesign(
        string design)
    {
        return design switch
        {
            "BetweenSubjects" => Text(
                "بين المجموعات",
                "Between-subjects"),
            "WithinSubjects" => Text(
                "داخل المجموعة",
                "Within-subjects"),
            "Mixed" => Text("مختلط", "Mixed"),
            "SingleGroup" => Text(
                "مجموعة واحدة",
                "Single group"),
            _ => design
        };
    }


    private string LocalizeAssignment(
        string assignment)
    {
        return assignment switch
        {
            "Manual" => Text("يدوي", "Manual"),
            "Random" => Text("عشوائي", "Random"),
            "BalancedRandom" => Text(
                "عشوائي متوازن",
                "Balanced random"),
            "Imported" => Text("مستورد", "Imported"),
            _ => assignment
        };
    }


    private string OnOff(
        bool value)
    {
        return value
            ? Text("مفعل", "On")
            : Text("متوقف", "Off");
    }


    private void ApplyReadingDirection(
        TextBlock textBlock)
    {
        textBlock.FlowDirection = LocalizationService.IsArabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        textBlock.TextAlignment = LocalizationService.IsArabic
            ? TextAlignment.Right
            : TextAlignment.Left;
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


    private static string? NormalizeOptional(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }


    private static SolidColorBrush Brush(
        string value)
    {
        return new SolidColorBrush(
            Color.Parse(value));
    }


    private sealed record Choice(
        string Value,
        string Display)
    {
        public override string ToString() => Display;
    }
}
