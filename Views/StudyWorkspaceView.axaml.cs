using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SOCYVIA.Models;
using SOCYVIA.Repositories;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

public partial class StudyWorkspaceView : UserControl
{
    public event EventHandler? BackRequested;
    public event EventHandler<Study>? EditRequested;
    public event EventHandler? ContentLibraryRequested;
    public event Action<string?>? MediaUrlsSetupRequested;
    public event EventHandler? CloudSettingsRequested;

    private Study? _study;
    private StimulusPost? _editingPost;

    private List<StimulusPost> _posts = new();
    private List<StudyGroup> _groups = new();

    private GroupManagementView? _groupManagementView;
    private ExperimentDesignerView? _experimentDesignerView;
    private ExperimentBuilderView? _experimentBuilderView;
    private PublishWorkspaceView? _publishWorkspaceView;
    private SessionLaunchView? _sessionLaunchView;
    private ParticipantManagementView? _participantManagementView;
    private QuestionnaireWorkspaceView? _questionnaireWorkspaceView;
    private AnalysisWorkspaceView? _analysisWorkspaceView;
    private ResearchResultsWorkspaceView? _researchResultsWorkspaceView;

    private bool _isSavingPost;
    private StudySaveCoordinator? _saveCoordinator;
    private bool _localizationSubscribed;

    private readonly FontFamily _englishFont =
        new("avares://SOCYVIA/Assets/Fonts#IBM Plex Sans");

    private readonly FontFamily _arabicFont =
        new("avares://SOCYVIA/Assets/Fonts#IBM Plex Sans Arabic");


    public StudyWorkspaceView()
    {
        InitializeComponent();

        SetupEvents();
        ConfigureLanguage();
        AttachedToVisualTree += OnWorkspaceAttached;
        DetachedFromVisualTree += OnWorkspaceDetached;
    }


    public StudyWorkspaceView(
        Study study)
        : this()
    {
        _study = study;

        _saveCoordinator = StudySaveCoordinatorRegistry.ForStudy(study.Id);
        _saveCoordinator.StateChanged += OnStudySaveStateChanged;
        UpdateStudySaveState(_saveCoordinator.State);

        ConfigureStudy();

        AttachedToVisualTree +=
            async (_, _) =>
            {
                await LoadStudyDataAsync();
            };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_saveCoordinator is not null) _saveCoordinator.StateChanged -= OnStudySaveStateChanged;
        };
    }


    // =========================================================
    // EVENTS
    // =========================================================

    private void SetupEvents()
    {
        BackToDashboardButton.Click +=
            async (_, _) =>
            {
                if (!await FlushForTransitionAsync()) return;
                BackRequested?.Invoke(
                    this,
                    EventArgs.Empty);
            };


        OverviewButton.Click +=
            (_, _) =>
            {
                ShowOverview();
            };


        PostsButton.Click +=
            (_, _) =>
            {
                ShowPosts();
            };


        GroupsButton.Click +=
            async (_, _) =>
            {
                await ShowGroupsAsync();
            };


        ParticipantsButton.Click +=
            async (_, _) => await ShowParticipantsAsync();

        ParticipantFlowButton.Click +=
            (_, _) => ShowParticipantFlow();


        QuestionnairesButton.Click +=
            async (_, _) => await ShowQuestionnairesAsync();

        ValidatePreviewButton.Click +=
            async (_, _) => await ShowValidatePreviewAsync();


        ExperimentButton.Click +=
            async (_, _) =>
            {
                await ShowExperimentDesignerAsync();
            };

        PublishButton.Click += async (_, _) => await ShowPublishWorkspaceAsync();

        BuilderButton.Click +=
            async (_, _) => await ShowExperimentBuilderAsync();


        SessionsButton.Click +=
            async (_, _) =>
            {
                await ShowDataOperationsAsync();
            };


        AnalysisButton.Click +=
            async (_, _) => await ShowAnalysisAsync();


        ReportsButton.Click += async (_, _) => await ShowReportsAsync();


        StudySettingsButton.Click +=
            (_, _) =>
            {
                if (_study is null)
                    return;

                EditRequested?.Invoke(
                    this,
                    _study);
            };


        QuickPostsButton.Click +=
            async (_, _) => await ShowExperimentBuilderAsync();


        QuickParticipantsButton.Click +=
            async (_, _) => await ShowParticipantsAsync();


        QuickExperimentButton.Click +=
            async (_, _) =>
            {
                await ShowExperimentDesignerAsync();
            };


        AddPostButton.Click +=
            (_, _) =>
            {
                OpenNewPostEditor();
            };

        CreateManualButton.Click += (_, _) =>
            ContentLibraryRequested?.Invoke(this, EventArgs.Empty);
        FromUrlButton.Click += (_, _) =>
            ContentLibraryRequested?.Invoke(this, EventArgs.Empty);
        FromDeviceButton.Click += (_, _) =>
            ContentLibraryRequested?.Invoke(this, EventArgs.Empty);

        AcquisitionActionsGrid.SizeChanged += (_, _) => UpdateAcquisitionActionsLayout();


        PostsEmptyCreateButton.Click +=
            (_, _) =>
            {
                OpenNewPostEditor();
            };


        ImportPostsButton.Click +=
            async (_, _) =>
            {
                await ShowImportWindowAsync();
            };


        PostsSearchBox.TextChanged +=
            (_, _) =>
            {
                RenderPosts();
            };


        PlatformFilterComboBox.SelectionChanged +=
            (_, _) =>
            {
                RenderPosts();
            };


        ContentTypeFilterComboBox.SelectionChanged +=
            (_, _) =>
            {
                RenderPosts();
            };


        CancelPostEditorTopButton.Click +=
            (_, _) =>
            {
                ClosePostEditor();
            };


        CancelPostEditorButton.Click +=
            (_, _) =>
            {
                ClosePostEditor();
            };


        SavePostButton.Click +=
            async (_, _) =>
            {
                await SavePostAsync();
            };
    }


    // =========================================================
    // STUDY
    // =========================================================

    private void ConfigureStudy()
    {
        if (_study is null)
            return;


        StudyTitleText.Text =
            DisplayStudyTitle(_study.Title);


        StudyStatusText.Text =
            GetLocalizedStatus(
                _study.Status);


        StudySubtitleText.Text =
            GetStudySubtitle();


        ResearchQuestionValue.Text =
            string.IsNullOrWhiteSpace(
                _study.ResearchQuestion)
                ? "—"
                : _study.ResearchQuestion;


        HypothesisValue.Text =
            string.IsNullOrWhiteSpace(
                _study.Hypothesis)
                ? "—"
                : _study.Hypothesis;


        DesignValue.Text =
            GetLocalizedDesign();


        TargetSampleValue.Text =
            _study.TargetSampleSize?
                .ToString()
            ?? "—";


        ConfigureStudyDirection();
        ConfigureFoundationDirection();

        InitializeExperimentDesignWorkspaces();
    }

    private void OnWorkspaceAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (!_localizationSubscribed)
        {
            LocalizationService.LanguageChanged += OnWorkspaceLanguageChanged;
            _localizationSubscribed = true;
        }
        ConfigureLanguage();
    }

    private void OnWorkspaceDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (!_localizationSubscribed) return;
        LocalizationService.LanguageChanged -= OnWorkspaceLanguageChanged;
        _localizationSubscribed = false;
    }

    private void OnWorkspaceLanguageChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(ConfigureLanguage);


    private string DisplayStudyTitle(string? title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        return trimmed.Any(char.IsLetterOrDigit)
            ? trimmed
            : UiTextService.Localized("دراسة بلا عنوان", "Untitled study");
    }


    // =========================================================
    // LOAD DATA
    // =========================================================

    private async Task LoadStudyDataAsync()
    {
        if (_study is null)
            return;


        try
        {
            var groupsTask =
                GroupRepository
                    .GetByStudyAsync(
                        _study.Id);


            var participantsTask =
                ParticipantRepository
                    .GetByStudyAsync(
                        _study.Id);


            var postsTask =
                StimulusPostRepository
                    .GetByStudyAsync(
                        _study.Id);


            var sessionsTask =
                ExperimentSessionRepository
                    .CountByStudyAsync(
                        _study.Id);


            await Task.WhenAll(
                groupsTask,
                participantsTask,
                postsTask,
                sessionsTask);


            _groups =
                groupsTask.Result;


            _posts =
                postsTask.Result;


            GroupsMetricValue.Text =
                _groups.Count.ToString();


            ParticipantsMetricValue.Text =
                participantsTask
                    .Result
                    .Count
                    .ToString();


            PostsMetricValue.Text =
                _posts.Count.ToString();


            SessionsMetricValue.Text =
                sessionsTask
                    .Result
                    .ToString();


            ConfigureGroupComboBox();
            RenderPosts();
            ConfigureWorkflowState();
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Study workspace data error: {exception}");
        }
    }


    private async Task ReloadPostsAsync()
    {
        if (_study is null)
            return;


        _posts =
            await StimulusPostRepository
                .GetByStudyAsync(
                    _study.Id);


        PostsMetricValue.Text =
            _posts.Count.ToString();


        RenderPosts();
    }


    // =========================================================
    // NAVIGATION
    // =========================================================

    private void ShowOverview()
    {
        ClosePostEditor();
        SetSelectedNavigation(
            OverviewButton);
        HidePrimaryWorkspaces();
        OverviewPanel.IsVisible =
            true;
    }


    private void ShowPosts()
    {
        SetSelectedNavigation(
            PostsButton);
        HidePrimaryWorkspaces();

        PostsPanel.IsVisible =
            true;


        PostEditorPanel.IsVisible =
            false;

        PostsListCard.IsVisible =
            true;


        ConfigurePostsDirection();
        RenderPosts();
    }


    public void OpenStimulusLibrary() => ShowPosts();


    public Task OpenParticipantsAsync() => ShowParticipantsAsync();


    public Task OpenSessionsAsync() => ShowDataOperationsAsync();

    public Task OpenAnalysisAsync() => ShowAnalysisAsync();


    private async Task ShowQuestionnairesAsync()
    {
        ClosePostEditor();
        SetSelectedNavigation(QuestionnairesButton);
        HidePrimaryWorkspaces();
        QuestionnaireWorkspaceHost.IsVisible = true;
        if (_questionnaireWorkspaceView is not null)
            await _questionnaireWorkspaceView.ReloadAsync();
    }


    private async Task ShowAnalysisAsync()
    {
        ClosePostEditor();
        SetSelectedNavigation(AnalysisButton);
        HidePrimaryWorkspaces();
        AnalysisWorkspaceHost.IsVisible = true;
        if (_analysisWorkspaceView is not null)
            await _analysisWorkspaceView.ReloadRemoteAsync();
    }


    private void HidePrimaryWorkspaces()
    {
        OverviewPanel.IsVisible = false;
        PostsPanel.IsVisible = false;
        SectionPanel.IsVisible = false;
        GroupsWorkspaceHost.IsVisible = false;
        ExperimentDesignerHost.IsVisible = false;
        ExperimentBuilderHost.IsVisible = false;
        PublishWorkspaceHost.IsVisible = false;
        SessionLaunchHost.IsVisible = false;
        ParticipantsWorkspaceHost.IsVisible = false;
        QuestionnaireWorkspaceHost.IsVisible = false;
        AnalysisWorkspaceHost.IsVisible = false;
        ResearchResultsWorkspaceHost.IsVisible = false;
        ParticipantFlowPanel.IsVisible = false;
    }

    private void ShowParticipantFlow()
    {
        ClosePostEditor();
        SetSelectedNavigation(ParticipantFlowButton);
        HidePrimaryWorkspaces();
        ParticipantFlowPanel.IsVisible = true;
    }


    private void ShowSection(
        Button selectedButton,
        string title,
        string description)
    {
        ClosePostEditor();

        SetSelectedNavigation(
            selectedButton);


        OverviewPanel.IsVisible =
            false;

        PostsPanel.IsVisible =
            false;

        SectionPanel.IsVisible =
            true;

        GroupsWorkspaceHost.IsVisible =
            false;

        ExperimentDesignerHost.IsVisible =
            false;

        SessionLaunchHost.IsVisible =
            false;

        ParticipantsWorkspaceHost.IsVisible = false;


        SectionTitle.Text =
            title;

        SectionDescription.Text =
            description;


        ConfigureSectionText();
        ConfigureWorkflowLanguage();
        ConfigureAcquisitionLanguage();
    }

    private async Task ShowReportsAsync()
    {
        ClosePostEditor();
        SetSelectedNavigation(ReportsButton);
        HidePrimaryWorkspaces();
        ResearchResultsWorkspaceHost.IsVisible = true;
        if (_researchResultsWorkspaceView is not null)
            await _researchResultsWorkspaceView.ReloadAsync();
    }

    public async Task OpenSocyviaAiAsync()
    {
        await OpenResearchResultsAsync("ai");
    }

    public async Task OpenResearchResultsAsync(string section = "overview")
    {
        await ShowReportsAsync();
        if (_researchResultsWorkspaceView is not null)
            await _researchResultsWorkspaceView.OpenSectionAsync(section);
    }

    // Kept only as a legacy local-study summary helper; the Reports navigation now opens
    // the synchronized, study-scoped Research Results workspace above.
    private async Task ShowLegacyReportsAsync()
    {
        if (_study is null) return;
        var groupsTask = GroupRepository.GetByStudyAsync(_study.Id);
        var conditionsTask = ExperimentalConditionRepository.GetByStudyAsync(_study.Id);
        var participantsTask = ParticipantRepository.CountByStudyAsync(_study.Id);
        var sessionsTask = ExperimentSessionRepository.GetByStudyAsync(_study.Id);
        await Task.WhenAll(groupsTask, conditionsTask, participantsTask, sessionsTask);
        var completed = sessionsTask.Result.Count(item => item.Status == SessionLifecycleStates.Completed);
        ShowSection(
            ReportsButton,
            IsArabic() ? "التقارير" : "Reports",
            IsArabic()
                ? $"ملخص الدراسة المتاح: {groupsTask.Result.Count} مجموعات • {conditionsTask.Result.Count} شروط • {participantsTask.Result} مشاركين • {sessionsTask.Result.Count} جلسات • {completed} مكتملة. تعرض التقارير البيانات المتاحة فقط ولا تستنتج نتائج غير محسوبة."
                : $"Available study summary: {groupsTask.Result.Count} groups • {conditionsTask.Result.Count} conditions • {participantsTask.Result} participants • {sessionsTask.Result.Count} sessions • {completed} completed. Reports show available data only and never invent uncomputed findings.");
    }


    private void ShowAcquisitionNotice(string message)
    {
        AcquisitionNoticeText.Text = message;
        AcquisitionNoticeText.TextAlignment = IsArabic()
            ? TextAlignment.Right
            : TextAlignment.Left;
        AcquisitionNoticePanel.IsVisible = true;
    }


    private void ConfigureAcquisitionLanguage()
    {
        AcquisitionTitle.Text = IsArabic()
            ? "إضافة محفز"
            : "Add stimulus";
        AcquisitionSubtitle.Text = IsArabic()
            ? "اختر مسار الحصول على المادة البحثية مع الحفاظ على الفصل بين المصدر والعرض التجريبي."
            : "Choose an acquisition path while preserving the separation between source data and experimental presentation.";
        FromUrlTitle.Text = IsArabic() ? "إضافة رابط خارجي" : "Add External Link";
        FromUrlDescription.Text = IsArabic()
            ? "مصدر أو محتوى مستضاف خارجيا"
            : "Externally hosted source or content";
        FromDeviceTitle.Text = IsArabic() ? "إضافة وسائط" : "Add Media";
        FromDeviceDescription.Text = IsArabic()
            ? "صورة، فيديو أو صوت"
            : "Image, video or audio";
        CreateManualTitle.Text = IsArabic() ? "إضافة محتوى" : "Add Content";
        CreateManualDescription.Text = IsArabic()
            ? "نص، منشور أو مادة تجريبية"
            : "Text, post or experimental material";
        foreach (var button in new[] { FromUrlButton, FromDeviceButton, CreateManualButton })
            button.FlowDirection = IsArabic() ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        foreach (var text in new[] { FromUrlTitle, FromUrlDescription, FromDeviceTitle, FromDeviceDescription, CreateManualTitle, CreateManualDescription })
            text.TextAlignment = TextAlignment.Center;
    }

    private void UpdateAcquisitionActionsLayout()
    {
        var stacked = AcquisitionActionsGrid.Bounds.Width is > 0 and < 660;
        AcquisitionActionsGrid.ColumnDefinitions = stacked
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions("*,*,*");
        AcquisitionActionsGrid.RowDefinitions = stacked
            ? new RowDefinitions("Auto,Auto,Auto")
            : new RowDefinitions("Auto");

        var buttons = new[] { CreateManualButton, FromDeviceButton, FromUrlButton };
        for (var index = 0; index < buttons.Length; index++)
        {
            Grid.SetColumn(buttons[index], stacked ? 0 : index);
            Grid.SetRow(buttons[index], stacked ? index : 0);
        }
    }


    private void ConfigureWorkflowLanguage()
    {
        WorkflowDesignText.Text = IsArabic() ? "التصميم" : "DESIGN";
        WorkflowBuildText.Text = IsArabic() ? "البناء" : "BUILD";
        WorkflowRunText.Text = IsArabic() ? "التشغيل" : "RUN";
        WorkflowObserveText.Text = IsArabic() ? "المراقبة" : "OBSERVE";
        var font = IsArabic() ? _arabicFont : _englishFont;
        WorkflowDesignText.FontFamily = font;
        WorkflowBuildText.FontFamily = font;
        WorkflowRunText.FontFamily = font;
        WorkflowObserveText.FontFamily = font;
    }


    private async Task ShowParticipantsAsync()
    {
        ClosePostEditor();
        SetSelectedNavigation(ParticipantsButton);
        HidePrimaryWorkspaces();
        ParticipantsWorkspaceHost.IsVisible = true;
        if (_participantManagementView is not null)
        {
            await _participantManagementView.ReloadAsync();
        }
    }

    // Remote studies create sessions from the published participant flow. The
    // normal researcher entry point is therefore synchronized data operations,
    // not manual per-participant session preparation. SessionLaunchView remains
    // available to existing local/lab workflows but is no longer the default.
    private async Task ShowDataOperationsAsync()
    {
        ClosePostEditor();
        SetSelectedNavigation(SessionsButton);
        HidePrimaryWorkspaces();
        ParticipantsWorkspaceHost.IsVisible = true;
        if (_participantManagementView is not null)
            await _participantManagementView.ReloadAsync();
    }


    private async Task ShowGroupsAsync()
    {
        ClosePostEditor();
        SetSelectedNavigation(
            GroupsButton);
        HidePrimaryWorkspaces();
        GroupsWorkspaceHost.IsVisible = true;

        if (_groupManagementView is not null)
        {
            await _groupManagementView.ReloadAsync();
        }
    }


    private async Task ShowExperimentDesignerAsync()
    {
        ClosePostEditor();
        SetSelectedNavigation(
            ExperimentButton);
        HidePrimaryWorkspaces();
        ExperimentDesignerHost.IsVisible = true;

        if (_experimentDesignerView is not null)
        {
            await _experimentDesignerView.ReloadAsync();
        }
    }

    private async Task ShowPublishWorkspaceAsync()
    {
        ClosePostEditor();
        if (!await FlushForTransitionAsync()) return;
        SetSelectedNavigation(PublishButton);
        HidePrimaryWorkspaces();
        PublishWorkspaceHost.IsVisible = true;
        if (_publishWorkspaceView is not null)
            await _publishWorkspaceView.ReloadAsync();
    }


    private async Task ShowSessionLaunchAsync()
    {
        ClosePostEditor();
        SetSelectedNavigation(
            SessionsButton);
        HidePrimaryWorkspaces();
        SessionLaunchHost.IsVisible = true;

        if (_sessionLaunchView is not null)
        {
            await _sessionLaunchView.ReloadAsync();
        }
    }


    public Task OpenExperimentBuilderAsync() => ShowExperimentBuilderAsync();

    public async Task RefreshPublishAsync()
    {
        if (_publishWorkspaceView is not null) await _publishWorkspaceView.ReloadAsync();
    }

    private async Task<bool> FlushForTransitionAsync()
    {
        if (_study is null) return true;
        var saved = await StudySaveCoordinatorRegistry.FlushAsync(_study.Id);
        if (!saved) UpdateStudySaveState(StudySaveState.SaveFailed);
        return saved;
    }

    private void OnStudySaveStateChanged(object? sender, StudySaveState state) =>
        Dispatcher.UIThread.Post(() => UpdateStudySaveState(state));

    private void UpdateStudySaveState(StudySaveState state)
    {
        StudySaveStateText.Text = state switch
        {
            StudySaveState.Saving => IsArabic() ? "جار الحفظ..." : "Saving...",
            StudySaveState.Saved => IsArabic() ? "تم الحفظ" : "Saved",
            StudySaveState.UnsavedChanges => IsArabic() ? "تغييرات غير محفوظة" : "Unsaved Changes",
            _ => IsArabic() ? "تعذر الحفظ" : "Save Failed"
        };
        StudySaveStateText.Foreground = Brush(state == StudySaveState.SaveFailed ? "#B4233E" : "#7A8799");
    }


    private async Task ShowExperimentBuilderAsync()
    {
        ClosePostEditor();
        SetSelectedNavigation(BuilderButton);
        HidePrimaryWorkspaces();
        ExperimentBuilderHost.IsVisible = true;
        if (_experimentBuilderView is not null)
            await _experimentBuilderView.ReloadAsync();
    }

    private async Task ShowValidatePreviewAsync()
    {
        ClosePostEditor();
        if (!await FlushForTransitionAsync()) return;
        SetSelectedNavigation(ValidatePreviewButton);
        HidePrimaryWorkspaces();
        ExperimentBuilderHost.IsVisible = true;
        if (_experimentBuilderView is not null)
            await _experimentBuilderView.ReloadAsync();
    }


    private void InitializeExperimentDesignWorkspaces()
    {
        if (_study is null)
        {
            return;
        }

        _groupManagementView =
            new GroupManagementView(_study);

        _experimentDesignerView =
            new ExperimentDesignerView(_study);

        _experimentBuilderView =
            new ExperimentBuilderView(_study);

        _publishWorkspaceView =
            new PublishWorkspaceView(_study);
        _publishWorkspaceView.CloudSettingsRequested +=
            (_, _) => CloudSettingsRequested?.Invoke(this, EventArgs.Empty);
        _publishWorkspaceView.MediaUrlsSetupRequested +=
            contentItemId => MediaUrlsSetupRequested?.Invoke(contentItemId);
        _publishWorkspaceView.PilotDataRequested += async (_, _) =>
        {
            await ShowDataOperationsAsync();
            if (_participantManagementView is not null)
                await _participantManagementView.ShowPilotRemoteSessionsAsync();
        };

        _sessionLaunchView =
            new SessionLaunchView(_study);

        _participantManagementView =
            new ParticipantManagementView(_study);

        _questionnaireWorkspaceView =
            new QuestionnaireWorkspaceView(_study);

        _analysisWorkspaceView =
            new AnalysisWorkspaceView(_study);

        _researchResultsWorkspaceView =
            new ResearchResultsWorkspaceView(_study);

        _groupManagementView.GroupsChanged +=
            async (_, _) =>
                await RefreshGroupsAfterManagementAsync();

        _sessionLaunchView.SessionPrepared +=
            async (_, _) =>
                await RefreshSessionCountAsync();

        GroupsWorkspaceHost.Content =
            _groupManagementView;

        ExperimentDesignerHost.Content =
            _experimentDesignerView;

        ExperimentBuilderHost.Content =
            _experimentBuilderView;

        PublishWorkspaceHost.Content =
            _publishWorkspaceView;

        SessionLaunchHost.Content =
            _sessionLaunchView;

        ParticipantsWorkspaceHost.Content =
            _participantManagementView;

        QuestionnaireWorkspaceHost.Content =
            _questionnaireWorkspaceView;

        AnalysisWorkspaceHost.Content =
            _analysisWorkspaceView;

        ResearchResultsWorkspaceHost.Content =
            _researchResultsWorkspaceView;
    }


    private async Task RefreshSessionCountAsync()
    {
        if (_study is null)
        {
            return;
        }

        try
        {
            SessionsMetricValue.Text =
                (await ExperimentSessionRepository
                    .CountByStudyAsync(_study.Id))
                .ToString();
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Session count refresh error: {exception}");
        }
    }


    private async Task RefreshGroupsAfterManagementAsync()
    {
        if (_study is null)
        {
            return;
        }

        try
        {
            _groups =
                await GroupRepository
                    .GetByStudyAsync(_study.Id);

            GroupsMetricValue.Text =
                _groups.Count.ToString();

            ConfigureGroupComboBox();

            if (_experimentDesignerView is not null)
            {
                await _experimentDesignerView.ReloadAsync();
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Group workspace refresh error: {exception}");
        }
    }


    private void SetSelectedNavigation(
        Button selectedButton)
    {
        QuestionnaireWorkspaceHost.IsVisible = false;
        AnalysisWorkspaceHost.IsVisible = false;
        ParticipantFlowPanel.IsVisible = false;

        var buttons =
            new[]
            {
                OverviewButton,
                PostsButton,
                GroupsButton,
                ParticipantsButton,
                ParticipantFlowButton,
                QuestionnairesButton,
                ValidatePreviewButton,
                ExperimentButton,
                PublishButton,
                BuilderButton,
                SessionsButton,
                AnalysisButton,
                ReportsButton,
                StudySettingsButton
            };


        foreach (var button in buttons)
        {
            button.Classes.Remove(
                "selected");
        }

        if (selectedButton != BuilderButton)
        {
            ExperimentBuilderHost.IsVisible = false;
        }


        selectedButton.Classes.Add(
            "selected");
    }


    // =========================================================
    // POSTS
    // =========================================================

    private void RenderPosts()
    {
        PostsContainer
            .Children
            .Clear();


        var search =
            PostsSearchBox.Text?
                .Trim()
            ?? string.Empty;


        var selectedPlatform =
            GetSelectedPlatformFilter();


        var selectedContentType =
            GetSelectedContentTypeFilter();


        IEnumerable<StimulusPost> query =
            _posts;


        if (!string.IsNullOrWhiteSpace(
                search))
        {
            query =
                query.Where(
                    post =>
                        ContainsText(
                            post.Title,
                            search)
                        ||
                        ContainsText(
                            post.BodyText,
                            search)
                        ||
                        ContainsText(
                            post.AuthorName,
                            search)
                        ||
                        ContainsText(
                            post.SourceName,
                            search)
                        ||
                        ContainsText(
                            post.Topic,
                            search)
                        ||
                        ContainsText(
                            post.Category,
                            search)
                        ||
                        ContainsText(
                            post.OriginalUrl,
                            search));
        }


        if (selectedPlatform is not null)
        {
            query =
                query.Where(
                    post =>
                        string.Equals(
                            post.Platform,
                            selectedPlatform,
                            StringComparison.OrdinalIgnoreCase));
        }


        if (selectedContentType is not null)
        {
            query =
                query.Where(
                    post =>
                        string.Equals(
                            post.ContentType,
                            selectedContentType,
                            StringComparison.OrdinalIgnoreCase));
        }


        var filtered =
            query
                .OrderBy(
                    post =>
                        post.OrderIndex)
                .ThenByDescending(
                    post =>
                        post.UpdatedAtUtc)
                .ToList();


        PostsCountText.Text =
            GetPostsCountText(
                filtered.Count,
                _posts.Count);


        PostsEmptyPanel.IsVisible =
            _posts.Count == 0;


        PostsNoResultsPanel.IsVisible =
            _posts.Count > 0 &&
            filtered.Count == 0;


        PostsContainer.IsVisible =
            filtered.Count > 0;


        foreach (var post in filtered)
        {
            PostsContainer
                .Children
                .Add(
                    CreatePostCard(
                        post));
        }
    }


    private static bool ContainsText(
        string? value,
        string search)
    {
        return !string.IsNullOrWhiteSpace(
                   value)
               &&
               value.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }


    // =========================================================
    // POST CARD
    // =========================================================

    private Control CreatePostCard(
        StimulusPost post)
    {
        var isArabic =
            IsArabic();


        var title =
            new TextBlock
            {
                Text =
                    post.Title,

                FontFamily =
                    isArabic
                        ? _arabicFont
                        : _englishFont,

                FontSize =
                    10.5,

                FontWeight =
                    FontWeight.SemiBold,

                Foreground =
                    Brush(
                        post.IsActive
                            ? "#273A57"
                            : "#8C98AA"),

                FlowDirection =
                    isArabic
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight,

                TextAlignment =
                    isArabic
                        ? TextAlignment.Right
                        : TextAlignment.Left,

                HorizontalAlignment =
                    HorizontalAlignment.Stretch,

                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };


        var body =
            new TextBlock
            {
                Text =
                    string.IsNullOrWhiteSpace(
                        post.BodyText)
                        ? isArabic
                            ? "لا يوجد نص مرفق"
                            : "No text content"
                        : post.BodyText,

                FontFamily =
                    isArabic
                        ? _arabicFont
                        : _englishFont,

                FontSize =
                    8.2,

                Foreground =
                    Brush(
                        "#8C98AA"),

                FlowDirection =
                    isArabic
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight,

                TextAlignment =
                    isArabic
                        ? TextAlignment.Right
                        : TextAlignment.Left,

                TextWrapping =
                    TextWrapping.NoWrap,

                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };


        var meta =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,

                Spacing =
                    7,

                HorizontalAlignment =
                    isArabic
                        ? HorizontalAlignment.Right
                        : HorizontalAlignment.Left,

                FlowDirection =
                    isArabic
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight
            };


        meta.Children.Add(
            CreateBadge(
                GetLocalizedPlatform(
                    post.Platform),
                "#F0F1FF",
                "#2563EB"));


        meta.Children.Add(
            CreateBadge(
                GetLocalizedContentType(
                    post.ContentType),
                "#F4F6FA",
                "#64738A"));


        // =====================================================
        // POST AVAILABILITY
        //
        // Active is the normal state, so no badge is shown.
        // Only disabled posts receive a visible status badge.
        // =====================================================

        if (!post.IsActive)
        {
            meta.Children.Add(
                CreateBadge(
                    isArabic
                        ? "معطل"
                        : "Disabled",
                    "#F1F3F6",
                    "#7B8799"));
        }


        var metrics =
            BuildPostMetricsSummary(
                post);


        if (!string.IsNullOrWhiteSpace(
                metrics))
        {
            meta.Children.Add(
                new TextBlock
                {
                    Text =
                        metrics,

                    FontFamily =
                        _englishFont,

                    FontSize =
                        7.6,

                    Foreground =
                        Brush(
                            "#98A3B5"),

                    VerticalAlignment =
                        VerticalAlignment.Center
                });
        }


        var textPanel =
            new StackPanel
            {
                Spacing =
                    5,

                VerticalAlignment =
                    VerticalAlignment.Center
            };


        textPanel.Children.Add(
            title);

        textPanel.Children.Add(
            body);

        textPanel.Children.Add(
            meta);

        var contentLayout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,12,*")
        };
        var contentTypeMark = CreateContentTypeMark(post.ContentType);
        Grid.SetColumn(contentTypeMark, 0);
        Grid.SetColumn(textPanel, 2);
        contentLayout.Children.Add(contentTypeMark);
        contentLayout.Children.Add(textPanel);


        var openButton =
            new Button
            {
                Background =
                    Brushes.Transparent,

                BorderThickness =
                    new Thickness(0),

                Padding =
                    new Thickness(
                        16,
                        11),

                HorizontalAlignment =
                    HorizontalAlignment.Stretch,

                HorizontalContentAlignment =
                    HorizontalAlignment.Stretch,

                Cursor =
                    new Cursor(
                        StandardCursorType.Hand),

                Content =
                    contentLayout
            };


        openButton.Click +=
            (_, _) =>
            {
                OpenEditPostEditor(
                    post);
            };


        var menuButton =
            CreatePostMenuButton(
                post);


        var grid =
            new Grid();


        if (isArabic)
        {
            // العربية:
            // ⋯ أقصى اليسار
            // المحتوى في اليمين

            grid.ColumnDefinitions =
                new ColumnDefinitions(
                    "52,*");


            Grid.SetColumn(
                menuButton,
                0);


            Grid.SetColumn(
                openButton,
                1);
        }
        else
        {
            // English:
            // content left
            // ⋯ right

            grid.ColumnDefinitions =
                new ColumnDefinitions(
                    "*,52");


            Grid.SetColumn(
                openButton,
                0);


            Grid.SetColumn(
                menuButton,
                1);
        }


        grid.Children.Add(
            openButton);

        grid.Children.Add(
            menuButton);


        return new Border
        {
            Classes =
            {
                "postCard"
            },

            Opacity =
                post.IsActive
                    ? 1
                    : 0.72,

            Child =
                grid
        };
    }


    private Border CreateBadge(
        string text,
        string background,
        string foreground)
    {
        return new Border
        {
            MinWidth =
                54,

            Height =
                23,

            Padding =
                new Thickness(
                    8,
                    0),

            Background =
                Brush(
                    background),

            CornerRadius =
                new CornerRadius(
                    7),

            Child =
                new TextBlock
                {
                    Text =
                        text,

                    FontFamily =
                        IsArabic()
                            ? _arabicFont
                            : _englishFont,

                    FontSize =
                        7.4,

                    FontWeight =
                        FontWeight.SemiBold,

                    Foreground =
                        Brush(
                            foreground),

                    HorizontalAlignment =
                        HorizontalAlignment.Center,

                    VerticalAlignment =
                        VerticalAlignment.Center,

                    TextAlignment =
                        TextAlignment.Center
                }
        };
    }


    // =========================================================
    // POST MENU
    // =========================================================

    private Button CreatePostMenuButton(
    StimulusPost post)
{
    var isArabic =
        IsArabic();


    var button =
        new Button
        {
            Width =
                38,

            Height =
                38,

            Padding =
                new Thickness(0),

            Background =
                Brushes.Transparent,

            BorderThickness =
                new Thickness(0),

            CornerRadius =
                new CornerRadius(
                    8),

            HorizontalAlignment =
                HorizontalAlignment.Center,

            VerticalAlignment =
                VerticalAlignment.Center,

            HorizontalContentAlignment =
                HorizontalAlignment.Center,

            VerticalContentAlignment =
                VerticalAlignment.Center,

            Cursor =
                new Cursor(
                    StandardCursorType.Hand),

            Content =
                new TextBlock
                {
                    Text =
                        "⋯",

                    FontFamily =
                        _englishFont,

                    FontSize =
                        18,

                    Foreground =
                        Brush(
                            "#7C899D"),

                    HorizontalAlignment =
                        HorizontalAlignment.Center,

                    VerticalAlignment =
                        VerticalAlignment.Center,

                    TextAlignment =
                        TextAlignment.Center
                }
        };


    // Adaptive:
    // no fixed Width anymore.
    var menu =
        new StackPanel
        {
            MinWidth =
                142,

            MaxWidth =
                190,

            Spacing =
                1,

            HorizontalAlignment =
                HorizontalAlignment.Stretch
        };


    var edit =
        CreatePostMenuItem(
            isArabic
                ? "تعديل المنشور"
                : "Edit post");


    edit.Click +=
        (_, _) =>
        {
            button.Flyout?
                .Hide();


            OpenEditPostEditor(
                post);
        };


    var duplicate =
        CreatePostMenuItem(
            isArabic
                ? "إنشاء نسخة"
                : "Duplicate");


    duplicate.Click +=
        async (_, _) =>
        {
            button.Flyout?
                .Hide();


            await DuplicatePostAsync(
                post);
        };


    var active =
        CreatePostMenuItem(
            post.IsActive
                ? isArabic
                    ? "تعطيل المنشور"
                    : "Disable post"
                : isArabic
                    ? "تفعيل المنشور"
                    : "Enable post");


    active.Click +=
        async (_, _) =>
        {
            button.Flyout?
                .Hide();


            await TogglePostActiveAsync(
                post);
        };


    var delete =
        CreatePostMenuItem(
            isArabic
                ? "حذف المنشور"
                : "Delete post",
            true);


    delete.Click +=
        async (_, _) =>
        {
            button.Flyout?
                .Hide();


            await DeletePostAsync(
                post);
        };


    menu.Children.Add(
        edit);


    menu.Children.Add(
        duplicate);


    menu.Children.Add(
        new Border
        {
            Height =
                1,

            Margin =
                new Thickness(
                    6,
                    4),

            Background =
                Brush(
                    "#EDF0F5")
        });


    menu.Children.Add(
        active);


    menu.Children.Add(
        delete);


    button.Flyout =
        new Flyout
        {
            Content =
                new Border
                {
                    Padding =
                        new Thickness(
                            5),

                    Background =
                        Brushes.White,

                    BorderBrush =
                        Brush(
                            "#E6EAF1"),

                    BorderThickness =
                        new Thickness(
                            1),

                    CornerRadius =
                        new CornerRadius(
                            11),

                    Child =
                        menu
                }
        };


    return button;
}


    private Button CreateMenuItem(
        string text,
        bool destructive = false)
    {
        var isArabic =
            IsArabic();


        return new Button
        {
            Height =
                31,

            MinWidth =
                132,

            Padding =
                new Thickness(
                    12,
                    0),

            Background =
                Brushes.Transparent,

            BorderThickness =
                new Thickness(0),

            CornerRadius =
                new CornerRadius(
                    7),

            HorizontalAlignment =
                HorizontalAlignment.Stretch,

            HorizontalContentAlignment =
                HorizontalAlignment.Center,

            VerticalContentAlignment =
                VerticalAlignment.Center,

            Cursor =
                new Cursor(
                    StandardCursorType.Hand),

            Content =
                new TextBlock
                {
                    Text =
                        text,

                    FontFamily =
                        isArabic
                            ? _arabicFont
                            : _englishFont,

                    FontSize =
                        8.6,

                    Foreground =
                        Brush(
                            destructive
                                ? "#D84A5B"
                                : "#455671"),

                    FlowDirection =
                        isArabic
                            ? FlowDirection.RightToLeft
                            : FlowDirection.LeftToRight,

                    TextAlignment =
                        TextAlignment.Center,

                    HorizontalAlignment =
                        HorizontalAlignment.Center,

                    VerticalAlignment =
                        VerticalAlignment.Center
                }
        };
    }
    private Button CreatePostMenuItem(
        string text,
        bool destructive = false)
    {
        var isArabic =
            IsArabic();


        return new Button
        {
            Height =
                31,

            MinWidth =
                132,

            Padding =
                new Thickness(
                    12,
                    0),

            Background =
                Brushes.Transparent,

            BorderThickness =
                new Thickness(
                    0),

            CornerRadius =
                new CornerRadius(
                    7),

            HorizontalAlignment =
                HorizontalAlignment.Stretch,

            HorizontalContentAlignment =
                HorizontalAlignment.Center,

            VerticalContentAlignment =
                VerticalAlignment.Center,

            Cursor =
                new Cursor(
                    StandardCursorType.Hand),

            Content =
                new TextBlock
                {
                    Text =
                        text,

                    FontFamily =
                        isArabic
                            ? _arabicFont
                            : _englishFont,

                    FontSize =
                        8.6,

                    Foreground =
                        Brush(
                            destructive
                                ? "#D84A5B"
                                : "#455671"),

                    FlowDirection =
                        isArabic
                            ? FlowDirection.RightToLeft
                            : FlowDirection.LeftToRight,

                    TextAlignment =
                        TextAlignment.Center,

                    HorizontalAlignment =
                        HorizontalAlignment.Center,

                    VerticalAlignment =
                        VerticalAlignment.Center
                }
        };
    }
    // =========================================================
    // NEW / EDIT
    // =========================================================

    private void OpenNewPostEditor()
    {
        _editingPost =
            null;


        ClearPostEditor();


        PostEditorTitle.Text =
            IsArabic()
                ? "إضافة محفز"
                : "Add stimulus";


        PostEditorSubtitle.Text =
            IsArabic()
                ? "أدخل بيانات المحفز كما ستستعمل داخل الدراسة"
                : "Enter the stimulus information used in this study";


        SavePostButtonText.Text =
            IsArabic()
                ? "حفظ المحفز"
                : "Save stimulus";


        ShowPostEditor();
    }


    private void OpenEditPostEditor(
        StimulusPost post)
    {
        _editingPost =
            post;


        ClearPostEditor();


        PostEditorTitle.Text =
            IsArabic()
                ? "تعديل المحفز"
                : "Edit stimulus";


        PostEditorSubtitle.Text =
            IsArabic()
                ? "عدل البيانات ثم احفظ التغييرات"
                : "Update the information and save your changes";


        SavePostButtonText.Text =
            IsArabic()
                ? "حفظ التعديلات"
                : "Save Changes";


        FillPostEditor(
            post);


        ShowPostEditor();
    }


    private void ShowPostEditor()
    {
        PostsListCard.IsVisible =
            false;


        PostEditorPanel.IsVisible =
            true;


        ClearPostEditorError();

        ConfigurePostEditorDirection();
        ConfigureCompactSelectionFields();
    }


    private void ClosePostEditor()
    {
        _editingPost =
            null;


        PostEditorPanel.IsVisible =
            false;


        PostsListCard.IsVisible =
            true;


        ClearPostEditorError();
    }


    private void FillPostEditor(
        StimulusPost post)
    {
        PostTitleBox.Text =
            post.Title;


        PostBodyBox.Text =
            post.BodyText;


        PostUrlBox.Text =
            post.OriginalUrl
            ?? string.Empty;

        PostPublishedMediaUrlBox.Text =
            post.PublishedMediaUrl
            ?? string.Empty;


        PostAuthorBox.Text =
            post.AuthorName
            ?? string.Empty;


        PostSourceBox.Text =
            post.SourceName
            ?? string.Empty;


        PostCategoryBox.Text =
            post.Category
            ?? string.Empty;


        PostTopicBox.Text =
            post.Topic
            ?? string.Empty;


        PostConditionBox.Text =
            post.ConditionLabel
            ?? string.Empty;


        PostExperimentalTagBox.Text =
            post.ExperimentalTag
            ?? string.Empty;


        PostResearcherNotesBox.Text =
            post.ResearcherNotes
            ?? string.Empty;


        PostPlatformComboBox.SelectedIndex =
            PlatformToIndex(
                post.Platform);


        PostContentTypeComboBox.SelectedIndex =
            ContentTypeToIndex(
                post.ContentType);


        PostLikesBox.Value =
            post.OriginalLikes;


        PostCommentsBox.Value =
            post.OriginalComments;


        PostSharesBox.Value =
            post.OriginalShares;


        PostSavesBox.Value =
            post.OriginalSaves;


        PostViewsBox.Value =
            post.OriginalViews;


        if (post.PublishedAtUtc.HasValue)
        {
            PostPublishedDatePicker.SelectedDate =
                post.PublishedAtUtc
                    .Value
                    .ToLocalTime()
                    .Date;
        }
        else
        {
            PostPublishedDatePicker.SelectedDate =
                null;
        }


        if (string.IsNullOrWhiteSpace(
                post.GroupId))
        {
            PostGroupComboBox.SelectedIndex =
                0;
        }
        else
        {
            var groupIndex =
                _groups.FindIndex(
                    group =>
                        group.Id ==
                        post.GroupId);


            PostGroupComboBox.SelectedIndex =
                groupIndex >= 0
                    ? groupIndex + 1
                    : 0;
        }
    }


    private void ClearPostEditor()
    {
        PostTitleBox.Text =
            string.Empty;

        PostBodyBox.Text =
            string.Empty;

        PostUrlBox.Text =
            string.Empty;

        PostPublishedMediaUrlBox.Text =
            string.Empty;

        PostAuthorBox.Text =
            string.Empty;

        PostSourceBox.Text =
            string.Empty;

        PostCategoryBox.Text =
            string.Empty;

        PostTopicBox.Text =
            string.Empty;

        PostConditionBox.Text =
            string.Empty;

        PostExperimentalTagBox.Text =
            string.Empty;

        PostResearcherNotesBox.Text =
            string.Empty;


        PostPlatformComboBox.SelectedIndex =
            0;

        PostContentTypeComboBox.SelectedIndex =
            0;

        PostGroupComboBox.SelectedIndex =
            0;


        PostPublishedDatePicker.SelectedDate =
            null;


        PostLikesBox.Value =
            null;

        PostCommentsBox.Value =
            null;

        PostSharesBox.Value =
            null;

        PostSavesBox.Value =
            null;

        PostViewsBox.Value =
            null;


        ClearPostEditorError();
    }


    // =========================================================
    // SAVE
    // =========================================================

    private async Task SavePostAsync()
    {
        if (_isSavingPost ||
            _study is null)
        {
            return;
        }


        var title =
            PostTitleBox.Text?
                .Trim()
            ?? string.Empty;


        if (string.IsNullOrWhiteSpace(
                title))
        {
            ShowPostEditorError(
                IsArabic()
                    ? "أدخل عنوان المنشور قبل الحفظ"
                    : "Enter a post title before saving");


            PostTitleBox.Focus();

            return;
        }


        try
        {
            _isSavingPost =
                true;


            SavePostButton.IsEnabled =
                false;


            SavePostButtonText.Text =
                IsArabic()
                    ? "جار الحفظ..."
                    : "Saving...";


            var post =
                _editingPost
                ?? new StimulusPost
                {
                    Id =
                        Guid.NewGuid()
                            .ToString(),

                    StudyId =
                        _study.Id,

                    OrderIndex =
                        _posts.Count,

                    IsActive =
                        true,

                    AllowRandomization =
                        true,

                    CreatedAtUtc =
                        DateTime.UtcNow
                };


            post.Title =
                title;


            post.BodyText =
                NormalizeOptional(
                    PostBodyBox.Text)
                ?? string.Empty;


            post.Platform =
                GetSelectedPlatform();


            post.ContentType =
                GetSelectedContentType();


            post.GroupId =
                GetSelectedGroupId();


            post.OriginalUrl =
                NormalizeOptional(
                    PostUrlBox.Text);

            var publishedMediaUrl = NormalizeOptional(PostPublishedMediaUrlBox.Text);
            Uri? publishedMediaUri = null;
            var requiresDirectMedia = post.ContentType is "Image" or "Video" or "Audio" or "Mixed";
            var validPublishedSource = publishedMediaUrl is null ||
                (requiresDirectMedia
                    ? PublishedMediaUrlValidator.TryValidateDirectMedia(publishedMediaUrl, out publishedMediaUri, out _)
                    : PublishedMediaUrlValidator.TryValidate(publishedMediaUrl, out publishedMediaUri, out _));
            if (!validPublishedSource)
            {
                var externalContent = PublishedMediaUrlValidator.TryValidate(
                    publishedMediaUrl, out var pageUri, out _) &&
                    PublishedMediaUrlValidator.IsExternalContentPage(pageUri!);
                ShowPostEditorError(IsArabic()
                    ? externalContent
                        ? "هذا رابط لمحتوى خارجي أو منصة اجتماعية. استخدم إضافة رابط خارجي بدلا من مصدر وسائط مباشر."
                        : "أدخل رابط HTTPS عاما وصالحا لمصدر الوسائط عند النشر. لا يمكن استخدام ملف محلي أو localhost."
                    : externalContent
                        ? "This is external or social content. Use Add External Link instead of a direct media source."
                        : "Enter a valid public HTTPS published media source. Local files and localhost cannot be used.");
                PostPublishedMediaUrlBox.Focus();
                return;
            }
            post.PublishedMediaUrl = publishedMediaUrl is null ? null : publishedMediaUri!.AbsoluteUri;


            post.AuthorName =
                NormalizeOptional(
                    PostAuthorBox.Text);


            post.SourceName =
                NormalizeOptional(
                    PostSourceBox.Text);


            post.PublishedAtUtc =
                GetSelectedPublishedDateUtc();


            post.Category =
                NormalizeOptional(
                    PostCategoryBox.Text);


            post.Topic =
                NormalizeOptional(
                    PostTopicBox.Text);


            post.ConditionLabel =
                NormalizeOptional(
                    PostConditionBox.Text);


            post.ExperimentalTag =
                NormalizeOptional(
                    PostExperimentalTagBox.Text);


            post.OriginalLikes =
                ToNullableInt(
                    PostLikesBox.Value);


            post.OriginalComments =
                ToNullableInt(
                    PostCommentsBox.Value);


            post.OriginalShares =
                ToNullableInt(
                    PostSharesBox.Value);


            post.OriginalSaves =
                ToNullableInt(
                    PostSavesBox.Value);


            post.OriginalViews =
                ToNullableLong(
                    PostViewsBox.Value);


            post.ResearcherNotes =
                NormalizeOptional(
                    PostResearcherNotesBox.Text);


            post.UpdatedAtUtc =
                DateTime.UtcNow;


            if (_editingPost is null)
            {
                await StimulusPostRepository
                    .CreateAsync(
                        post);
            }
            else
            {
                await StimulusPostRepository
                    .UpdateAsync(
                        post);
            }


            ClosePostEditor();

            await ReloadPostsAsync();
        }
        catch (Exception exception)
        {
            ShowPostEditorError(
                IsArabic()
                    ? $"تعذر حفظ المنشور: {exception.Message}"
                    : $"Post could not be saved: {exception.Message}");
        }
        finally
        {
            _isSavingPost =
                false;


            SavePostButton.IsEnabled =
                true;


            SavePostButtonText.Text =
                IsArabic()
                    ? "حفظ المنشور"
                    : "Save Post";
        }
    }


    // =========================================================
    // DUPLICATE
    // =========================================================

    private async Task DuplicatePostAsync(
        StimulusPost source)
    {
        try
        {
            var duplicate =
                new StimulusPost
                {
                    Id =
                        Guid.NewGuid()
                            .ToString(),

                    StudyId =
                        source.StudyId,

                    GroupId =
                        source.GroupId,

                    Title =
                        IsArabic()
                            ? $"{source.Title} - نسخة"
                            : $"{source.Title} - Copy",

                    BodyText =
                        source.BodyText,

                    ContentType =
                        source.ContentType,

                    Platform =
                        source.Platform,

                    SourceName =
                        source.SourceName,

                    AuthorName =
                        source.AuthorName,

                    OriginalUrl =
                        source.OriginalUrl,

                    PublishedAtUtc =
                        source.PublishedAtUtc,

                    MediaPath =
                        source.MediaPath,

                    ThumbnailPath =
                        source.ThumbnailPath,

                    PublishedMediaUrl =
                        source.PublishedMediaUrl,

                    Category =
                        source.Category,

                    Topic =
                        source.Topic,

                    ConditionLabel =
                        source.ConditionLabel,

                    ExperimentalTag =
                        source.ExperimentalTag,

                    OriginalLikes =
                        source.OriginalLikes,

                    OriginalComments =
                        source.OriginalComments,

                    OriginalShares =
                        source.OriginalShares,

                    OriginalSaves =
                        source.OriginalSaves,

                    OriginalViews =
                        source.OriginalViews,

                    OrderIndex =
                        _posts.Count,

                    IsActive =
                        source.IsActive,

                    MinimumExposureMilliseconds =
                        source.MinimumExposureMilliseconds,

                    MaximumExposureMilliseconds =
                        source.MaximumExposureMilliseconds,

                    AllowRandomization =
                        source.AllowRandomization,

                    CustomMetadataJson =
                        source.CustomMetadataJson,

                    ResearcherNotes =
                        source.ResearcherNotes,

                    CreatedAtUtc =
                        DateTime.UtcNow,

                    UpdatedAtUtc =
                        DateTime.UtcNow
                };


            await StimulusPostRepository
                .CreateAsync(
                    duplicate);


            await ReloadPostsAsync();
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Duplicate post error: {exception}");
        }
    }


    // =========================================================
    // ACTIVATE / DEACTIVATE
    // =========================================================

    private async Task TogglePostActiveAsync(
        StimulusPost post)
    {
        try
        {
            post.IsActive =
                !post.IsActive;


            post.UpdatedAtUtc =
                DateTime.UtcNow;


            await StimulusPostRepository
                .UpdateAsync(
                    post);


            await ReloadPostsAsync();
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Toggle post error: {exception}");
        }
    }


    // =========================================================
    // DELETE
    // =========================================================

    private async Task DeletePostAsync(
        StimulusPost post)
    {
        var confirmed =
            await ShowDeleteConfirmationAsync(
                post);


        if (!confirmed)
            return;


        try
        {
            await StimulusPostRepository
                .DeleteAsync(
                    post.Id);


            await ReloadPostsAsync();


            var ordered =
                _posts
                    .OrderBy(
                        item =>
                            item.OrderIndex)
                    .ToList();


            for (var index = 0;
                 index < ordered.Count;
                 index++)
            {
                if (ordered[index]
                        .OrderIndex ==
                    index)
                {
                    continue;
                }


                ordered[index]
                    .OrderIndex =
                    index;


                await StimulusPostRepository
                    .UpdateAsync(
                        ordered[index]);
            }


            await ReloadPostsAsync();
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Delete post error: {exception}");
        }
    }


    private async Task<bool> ShowDeleteConfirmationAsync(
    StimulusPost post)
{
    if (TopLevel.GetTopLevel(this)
        is not Window owner)
    {
        return false;
    }


    var isArabic =
        IsArabic();


    var dialog =
        new Window
        {
            Title =
                isArabic
                    ? "حذف المنشور"
                    : "Delete post",

            Background =
                Brush(
                    "#F7F9FD")
        };


    WindowAppearanceService.ConfigureCompactDialog(
        dialog,
        380,
        188);


    var title =
        new TextBlock
        {
            Text =
                isArabic
                    ? "حذف المنشور"
                    : "Delete post",

            FontFamily =
                isArabic
                    ? _arabicFont
                    : _englishFont,

            FontSize =
                13,

            FontWeight =
                FontWeight.SemiBold,

            Foreground =
                Brush(
                    "#203451"),

            FlowDirection =
                isArabic
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight,

            TextAlignment =
                TextAlignment.Center,

            HorizontalAlignment =
                HorizontalAlignment.Center
        };


    var message =
        new TextBlock
        {
            Text =
                isArabic
                    ? $"هل تريد حذف «{post.Title}» نهائيا؟"
                    : $"Permanently delete “{post.Title}”?",

            FontFamily =
                isArabic
                    ? _arabicFont
                    : _englishFont,

            FontSize =
                8.8,

            Foreground =
                Brush(
                    "#718097"),

            FlowDirection =
                isArabic
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight,

            TextAlignment =
                TextAlignment.Center,

            HorizontalAlignment =
                HorizontalAlignment.Stretch,

            TextWrapping =
                TextWrapping.Wrap
        };


    var cancel =
        CreateDialogButton(
            isArabic
                ? "إلغاء"
                : "Cancel",
            false);


    var delete =
        CreateDialogButton(
            isArabic
                ? "حذف"
                : "Delete",
            true);


    cancel.Click +=
        (_, _) =>
        {
            dialog.Close(
                false);
        };


    delete.Click +=
        (_, _) =>
        {
            dialog.Close(
                true);
        };


    var buttons =
        new StackPanel
        {
            Orientation =
                Orientation.Horizontal,

            Spacing =
                8,

            HorizontalAlignment =
                HorizontalAlignment.Center
        };


    buttons.Children.Add(
        cancel);

    buttons.Children.Add(
        delete);


    dialog.Content =
        new Border
        {
            Margin =
                new Thickness(
                    12),

            Padding =
                new Thickness(
                    18,
                    14),

            Background =
                Brushes.White,

            BorderBrush =
                Brush(
                    "#E3E9F3"),

            BorderThickness =
                new Thickness(
                    1),

            CornerRadius =
                new CornerRadius(
                    12),

            Child =
                new StackPanel
                {
                    Spacing =
                        12,

                    VerticalAlignment =
                        VerticalAlignment.Center,

                    Children =
                    {
                        title,
                        message,
                        buttons
                    }
                }
        };


    return await dialog
        .ShowDialog<bool>(
            owner);
}


    private Button CreateDialogButton(
        string text,
        bool destructive)
    {
        return new Button
        {
            Height =
                38,

            MinWidth =
                92,

            Content =
                text,

            FontFamily =
                IsArabic()
                    ? _arabicFont
                    : _englishFont,

            Background =
                destructive
                    ? Brush(
                        "#D84A5B")
                    : Brushes.White,

            Foreground =
                destructive
                    ? Brushes.White
                    : Brush(
                        "#455671"),

            BorderBrush =
                destructive
                    ? null
                    : Brush(
                        "#DDE4EF"),

            BorderThickness =
                destructive
                    ? new Thickness(0)
                    : new Thickness(1),

            CornerRadius =
                new CornerRadius(
                    9),

            HorizontalContentAlignment =
                HorizontalAlignment.Center,

            VerticalContentAlignment =
                VerticalAlignment.Center
        };
    }


    // =========================================================
    // IMPORT INFO
    // =========================================================

    // =========================================================
// IMPORT POSTS
// =========================================================

    private async Task ShowImportWindowAsync()
    {
        if (_study is null)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this)
            is not Window owner)
        {
            return;
        }


        try
        {
            var dialog =
                new StimulusPostImportWindow(
                    _study,
                    _groups);


            var imported =
                await dialog.ShowDialog<bool>(
                    owner);


            if (imported)
            {
                await ReloadPostsAsync();
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Import posts window error: {exception}");
        }
    }


    private Border CreateContentTypeMark(string contentType)
    {
        var monogram = contentType switch
        {
            "Text" => "T",
            "Image" => "I",
            "Video" => "V",
            "Audio" => "A",
            "Link" => "L",
            "Mixed" => "M",
            _ => "S"
        };

        return new Border
        {
            Width = 38,
            Height = 38,
            Background = Brush("#102563EB"),
            BorderBrush = Brush("#3D2563EB"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = monogram,
                FontFamily = _englishFont,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush("#2563EB"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
    }


    private async Task ShowImportInformationAsync()
{
    if (TopLevel.GetTopLevel(this)
        is not Window owner)
    {
        return;
    }


    var isArabic =
        IsArabic();


    var dialog =
        new Window
        {
            Title =
                isArabic
                    ? "استيراد البيانات"
                    : "Import data",

            Background =
                Brush(
                    "#F7F9FD")
        };


    WindowAppearanceService.ConfigureCompactDialog(
        dialog,
        400,
        205);


    var title =
        new TextBlock
        {
            Text =
                isArabic
                    ? "استيراد منشورات إلى SOCYVIA"
                    : "Import posts into SOCYVIA",

            FontFamily =
                isArabic
                    ? _arabicFont
                    : _englishFont,

            FontSize =
                13,

            FontWeight =
                FontWeight.SemiBold,

            Foreground =
                Brush(
                    "#203451"),

            TextAlignment =
                TextAlignment.Center,

            HorizontalAlignment =
                HorizontalAlignment.Center
        };


    var description =
        new TextBlock
        {
            Text =
                isArabic
                    ? "سيتيح SOCYVIA قالبا رسميا جاهزا. حمل القالب، عبئ البيانات في Excel أو CSV، ثم استورد الملف ليتم التحقق منه قبل إضافته إلى الدراسة."
                    : "SOCYVIA will provide an official template. Download it, complete the data in Excel or CSV, then import it for validation before adding it to the study.",

            FontFamily =
                isArabic
                    ? _arabicFont
                    : _englishFont,

            FontSize =
                8.7,

            LineHeight =
                15,

            Foreground =
                Brush(
                    "#718097"),

            FlowDirection =
                isArabic
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight,

            TextAlignment =
                TextAlignment.Center,

            HorizontalAlignment =
                HorizontalAlignment.Stretch,

            TextWrapping =
                TextWrapping.Wrap
        };


    var close =
        new Button
        {
            Height =
                34,

            MinWidth =
                86,

            Padding =
                new Thickness(
                    16,
                    0),

            Content =
                isArabic
                    ? "حسنا"
                    : "OK",

            FontFamily =
                isArabic
                    ? _arabicFont
                    : _englishFont,

            Background =
                Brush(
                    "#2563EB"),

            Foreground =
                Brushes.White,

            BorderThickness =
                new Thickness(
                    0),

            CornerRadius =
                new CornerRadius(
                    9),

            HorizontalAlignment =
                HorizontalAlignment.Center,

            HorizontalContentAlignment =
                HorizontalAlignment.Center,

            VerticalContentAlignment =
                VerticalAlignment.Center
        };


    close.Click +=
        (_, _) =>
        {
            dialog.Close();
        };


    dialog.Content =
        new Border
        {
            Margin =
                new Thickness(
                    12),

            Padding =
                new Thickness(
                    18,
                    14),

            Background =
                Brushes.White,

            BorderBrush =
                Brush(
                    "#E3E9F3"),

            BorderThickness =
                new Thickness(
                    1),

            CornerRadius =
                new CornerRadius(
                    12),

            Child =
                new StackPanel
                {
                    Spacing =
                        12,

                    VerticalAlignment =
                        VerticalAlignment.Center,

                    Children =
                    {
                        title,
                        description,
                        close
                    }
                }
        };


    await dialog
        .ShowDialog(
            owner);
}


    // =========================================================
    // COMBOS
    // =========================================================

    private void ConfigurePostComboBoxes()
    {
        ConfigurePlatformComboBox();
        ConfigureContentTypeComboBox();
        ConfigureFilters();
        ConfigureGroupComboBox();
    }


    private void ConfigurePlatformComboBox()
    {
        var previous =
            PostPlatformComboBox
                .SelectedIndex;


        PostPlatformComboBox.ItemsSource =
            IsArabic()
                ? new[]
                {
                    "عام",
                    "فيسبوك",
                    "إنستغرام",
                    "تيك توك",
                    "X",
                    "يوتيوب",
                    "موقع إخباري",
                    "مخصص"
                }
                : new[]
                {
                    "Generic",
                    "Facebook",
                    "Instagram",
                    "TikTok",
                    "X",
                    "YouTube",
                    "News",
                    "Custom"
                };


        PostPlatformComboBox.SelectedIndex =
            previous >= 0
                ? previous
                : 0;
    }


    private void ConfigureContentTypeComboBox()
    {
        var previous =
            PostContentTypeComboBox
                .SelectedIndex;


        PostContentTypeComboBox.ItemsSource =
            IsArabic()
                ? new[]
                {
                    "نص",
                    "صورة",
                    "فيديو",
                    "صوت",
                    "رابط",
                    "مختلط"
                }
                : new[]
                {
                    "Text",
                    "Image",
                    "Video",
                    "Audio",
                    "Link",
                    "Mixed"
                };


        PostContentTypeComboBox.SelectedIndex =
            previous >= 0
                ? previous
                : 0;
    }


    private void ConfigureFilters()
    {
        PlatformFilterComboBox.ItemsSource =
            IsArabic()
                ? new[]
                {
                    "كل المنصات",
                    "عام",
                    "فيسبوك",
                    "إنستغرام",
                    "تيك توك",
                    "X",
                    "يوتيوب",
                    "موقع إخباري",
                    "مخصص"
                }
                : new[]
                {
                    "All platforms",
                    "Generic",
                    "Facebook",
                    "Instagram",
                    "TikTok",
                    "X",
                    "YouTube",
                    "News",
                    "Custom"
                };


        ContentTypeFilterComboBox.ItemsSource =
            IsArabic()
                ? new[]
                {
                    "كل الأنواع",
                    "نص",
                    "صورة",
                    "فيديو",
                    "صوت",
                    "رابط",
                    "مختلط"
                }
                : new[]
                {
                    "All types",
                    "Text",
                    "Image",
                    "Video",
                    "Audio",
                    "Link",
                    "Mixed"
                };


        PlatformFilterComboBox.SelectedIndex =
            Math.Max(
                0,
                PlatformFilterComboBox
                    .SelectedIndex);


        ContentTypeFilterComboBox.SelectedIndex =
            Math.Max(
                0,
                ContentTypeFilterComboBox
                    .SelectedIndex);
    }


    private void ConfigureGroupComboBox()
    {
        var previous =
            PostGroupComboBox
                .SelectedIndex;


        var items =
            new List<string>
            {
                IsArabic()
                    ? "كل المجموعات"
                    : "All groups"
            };


        items.AddRange(
            _groups.Select(
                group =>
                    group.Name));


        PostGroupComboBox.ItemsSource =
            items;


        PostGroupComboBox.SelectedIndex =
            previous >= 0 &&
            previous < items.Count
                ? previous
                : 0;
    }


    private string GetSelectedPlatform()
    {
        return PostPlatformComboBox.SelectedIndex switch
        {
            1 => "Facebook",
            2 => "Instagram",
            3 => "TikTok",
            4 => "X",
            5 => "YouTube",
            6 => "News",
            7 => "Custom",
            _ => "Generic"
        };
    }


    private string GetSelectedContentType()
    {
        return PostContentTypeComboBox.SelectedIndex switch
        {
            1 => "Image",
            2 => "Video",
            3 => "Audio",
            4 => "Link",
            5 => "Mixed",
            _ => "Text"
        };
    }


    private string? GetSelectedPlatformFilter()
    {
        return PlatformFilterComboBox.SelectedIndex switch
        {
            1 => "Generic",
            2 => "Facebook",
            3 => "Instagram",
            4 => "TikTok",
            5 => "X",
            6 => "YouTube",
            7 => "News",
            8 => "Custom",
            _ => null
        };
    }


    private string? GetSelectedContentTypeFilter()
    {
        return ContentTypeFilterComboBox.SelectedIndex switch
        {
            1 => "Text",
            2 => "Image",
            3 => "Video",
            4 => "Audio",
            5 => "Link",
            6 => "Mixed",
            _ => null
        };
    }


    private string? GetSelectedGroupId()
    {
        var index =
            PostGroupComboBox
                .SelectedIndex;


        if (index <= 0)
            return null;


        var groupIndex =
            index - 1;


        if (groupIndex < 0 ||
            groupIndex >= _groups.Count)
        {
            return null;
        }


        return _groups[groupIndex]
            .Id;
    }


    private static int PlatformToIndex(
        string? platform)
    {
        return platform switch
        {
            "Facebook" => 1,
            "Instagram" => 2,
            "TikTok" => 3,
            "X" => 4,
            "YouTube" => 5,
            "News" => 6,
            "Custom" => 7,
            _ => 0
        };
    }


    private static int ContentTypeToIndex(
        string? contentType)
    {
        return contentType switch
        {
            "Image" => 1,
            "Video" => 2,
            "Audio" => 3,
            "Link" => 4,
            "Mixed" => 5,
            _ => 0
        };
    }


    // =========================================================
    // LANGUAGE
    // =========================================================

    private bool IsArabic()
    {
        return LocalizationService
            .IsArabic;
    }


    private void ConfigureLanguage()
    {
        if (IsArabic())
        {
            ApplyArabic();
        }
        else
        {
            ApplyEnglish();
        }


        ConfigurePostComboBoxes();
        ConfigureHeaderDirection();
        ConfigureNavigationDirection();
        ConfigureQuickStartDirection();
        ConfigureFoundationDirection();
        ConfigurePostsDirection();
        ConfigurePostEditorDirection();
        ConfigureSectionText();
        ConfigureWorkflowNavigation();
        ConfigureParticipantFlow();
        ConfigureAcquisitionLanguage();
    }

    private void ConfigureWorkflowNavigation()
    {
        SetCenter(OverviewButtonText, IsArabic() ? "المشروع" : "Project");
        SetCenter(PostsButtonText, IsArabic() ? "المحتوى والوسائط" : "Content & Media");
        SetCenter(BuilderButtonText, IsArabic() ? "تصميم التجربة" : "Experiment Design");
        SetCenter(GroupsButtonText, IsArabic() ? "المجموعات والشروط" : "Groups & Conditions");
        SetCenter(ExperimentButtonText, IsArabic() ? "التعيين والعرض" : "Assignment & Presentation");
        SetCenter(ParticipantsButtonText, IsArabic() ? "المشاركون" : "Participants");
        SetCenter(ParticipantFlowButtonText, IsArabic() ? "مسار المشارك" : "Participant Flow");
        SetCenter(QuestionnairesButtonText, IsArabic() ? "المقاييس والاستبيانات" : "Measures & Questionnaires");
        SetCenter(ValidatePreviewButtonText, IsArabic() ? "التحقق والمعاينة" : "Validate & Preview");
        SetCenter(PublishButtonText, IsArabic() ? "النشر" : "Publish");
        SetCenter(SessionsButtonText, IsArabic() ? "الجلسات والبيانات" : "Sessions & Data");
        SetCenter(AnalysisButtonText, IsArabic() ? "التحليل" : "Analysis");
        SetCenter(ReportsButtonText, IsArabic() ? "التقرير" : "Report");
    }

    private void ConfigureWorkflowState()
    {
        SetConfigured(OverviewButton, true);
        SetConfigured(PostsButton, _posts.Any(item => item.IsActive));
        SetConfigured(BuilderButton, _posts.Any(item => item.IsActive));
        SetConfigured(GroupsButton, _groups.Any(item => item.IsActive));
        SetConfigured(ExperimentButton, _groups.Any(item => item.IsActive));
        SetConfigured(QuestionnairesButton, _study?.UsesQuestionnaires == true);
        SetConfigured(ValidatePreviewButton, !string.IsNullOrWhiteSpace(_study?.Title));
    }

    private static void SetConfigured(Button button, bool configured)
    {
        button.Classes.Remove("configured");
        if (configured) button.Classes.Add("configured");
    }

    private void ConfigureParticipantFlow()
    {
        var ar = IsArabic();
        // The header must occupy the card width before text alignment is applied.
        // A right/left-aligned StackPanel shrinks to its content and can never reach
        // the directional content edge.
        ParticipantFlowHeader.HorizontalAlignment = HorizontalAlignment.Stretch;
        ParticipantFlowHeader.FlowDirection = ar ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        ParticipantFlowTitle.HorizontalAlignment = HorizontalAlignment.Stretch;
        ParticipantFlowSubtitle.HorizontalAlignment = HorizontalAlignment.Stretch;
        ParticipantFlowTitle.FlowDirection = ParticipantFlowHeader.FlowDirection;
        ParticipantFlowSubtitle.FlowDirection = ParticipantFlowHeader.FlowDirection;
        ParticipantFlowTitle.TextAlignment = ar ? TextAlignment.Right : TextAlignment.Left;
        ParticipantFlowSubtitle.TextAlignment = ar ? TextAlignment.Right : TextAlignment.Left;
        ParticipantFlowTitle.Text = ar ? "مسار المشارك" : "Participant flow";
        ParticipantFlowSubtitle.Text = ar ? "تسلسل واضح قابل للتهيئة حسب تصميم الدراسة. لا تبدأ القياسات السلوكية إلا عند حد بدء التجربة الصريح." : "A clear sequence configured by the study design. Behavioral experimental timing begins only at the explicit Start Experiment boundary.";
        FlowEntryText.Text = ar ? "الدخول" : "Entry"; FlowEntryHint.Text = ar ? "تعريف المشارك" : "Participant identity";
        FlowConsentText.Text = ar ? "الموافقة" : "Consent"; FlowConsentHint.Text = ar ? "قبل بدء الجلسة" : "Before the session";
        FlowInstructionsText.Text = ar ? "التعليمات" : "Instructions"; FlowInstructionsHint.Text = ar ? "حسب التصميم" : "Design-specific";
        FlowPreMeasureText.Text = ar ? "قياس قبلي" : "Pre-measure"; FlowPreMeasureHint.Text = ar ? "استبيانات اختيارية" : "Optional measures";
        FlowStartText.Text = ar ? "بدء التجربة" : "Start Experiment"; FlowStartHint.Text = ar ? "حد T0 السلوكي" : "Behavioral T0 boundary";
        FlowFeedText.Text = ar ? "خلاصة SOCYVIA" : "SOCYVIA Feed"; FlowFeedHint.Text = ar ? "بيئة المحفز" : "Stimulus environment";
        FlowPostMeasureText.Text = ar ? "قياس بعدي" : "Post-measure"; FlowPostMeasureHint.Text = ar ? "استبيانات بعدية" : "Follow-up measures";
        FlowCompletionText.Text = ar ? "الإكمال" : "Completion"; FlowCompletionHint.Text = ar ? "نهاية الجلسة" : "Session completion";
        ParticipantFlowBoundaryText.Text = ar ? "خلاصة تجربة SOCYVIA هي بيئة التجربة للمشارك. يفصل SOCYVIA فتح هذه البيئة عن بداية T0 السلوكي، وتظل القياسات الخام وتوقيت الجلسة كما هي في دورة حياة الجلسة المعتمدة." : "SOCYVIA Experiment Feed is the participant experiment environment. SOCYVIA separates opening this environment from behavioral T0; raw measures and session timing remain governed by the validated session lifecycle.";
        foreach (var text in ParticipantFlowStages.Children.OfType<Border>().SelectMany(border => ((StackPanel)border.Child!).Children.OfType<TextBlock>()))
        {
            text.FlowDirection = ar ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            text.TextWrapping = TextWrapping.Wrap;
        }
    }


    private void ApplyArabic()
    {
        RootStudyWorkspace.FontFamily =
            _arabicFont;


        SetCenter(
            BackToDashboardText,
            "العودة");


        SetCenter(
            OverviewButtonText,
            "نظرة عامة");

        SetCenter(
            PostsButtonText,
            "المحفزات");

        SetCenter(
            GroupsButtonText,
            "المجموعات");

        SetCenter(
            ParticipantsButtonText,
            "المشاركون");

        SetCenter(
            QuestionnairesButtonText,
            "الاستبيانات");

        SetCenter(
            ExperimentButtonText,
            "الشروط");

        SetCenter(
            BuilderButtonText,
            "منشئ التجربة");

        SetCenter(
            SessionsButtonText,
            "الجلسات");

        SetCenter(
            AnalysisButtonText,
            "التحليل");

        SetCenter(
            ReportsButtonText,
            "التقارير");

        SetCenter(
            StudySettingsButtonText,
            "إعدادات الدراسة");


        SetCenter(
            GroupsMetricLabel,
            "المجموعات");

        SetCenter(
            ParticipantsMetricLabel,
            "المشاركون");

        SetCenter(
            PostsMetricLabel,
            "المنشورات");

        SetCenter(
            SessionsMetricLabel,
            "الجلسات");


        SetRight(
            QuickStartTitle,
            "ابدأ العمل على الدراسة");

        SetRight(
            QuickStartSubtitle,
            "اختر الخطوة التي تريد إعدادها الآن");


        SetCenter(
            QuickPostsTitle,
            "إضافة المحفزات");

        SetCenter(
            QuickPostsDescription,
            "أنشئ محتوى تجريبيا أو استورد بيانات جاهزة");


        SetCenter(
            QuickParticipantsTitle,
            "إدارة المشاركين");

        SetCenter(
            QuickParticipantsDescription,
            "أضف العينة ووزع المشاركين على المجموعات");


        SetCenter(
            QuickExperimentTitle,
            "بناء التجربة");

        SetCenter(
            QuickExperimentDescription,
            "رتب المحفزات والأسئلة والجلسة التجريبية");


        SetRight(
            FoundationTitle,
            "الأساس البحثي");

        SetRight(
            ResearchQuestionTitle,
            "السؤال البحثي");

        SetRight(
            HypothesisTitle,
            "الفرضية");

        SetRight(
            DesignTitle,
            "التصميم");

        SetRight(
            TargetSampleTitle,
            "العينة المستهدفة");


        SetRight(
            PostsPageTitle,
            "مكتبة المحفزات البحثية");

        SetRight(
            PostsPageSubtitle,
            "إدارة المحتوى الرقمي ومصدره الأصلي وعرضه التجريبي");


        SetCenter(
            AddPostButtonText,
            "+ إضافة محفز");

        SetCenter(
            ImportPostsButtonText,
            "استيراد محفزات");


        PostsSearchBox.PlaceholderText =
            "ابحث في المحفزات";


        SetCenter(
            PostsEmptyTitle,
            "لا توجد منشورات بعد");

        SetCenter(
            PostsEmptyDescription,
            "أضف أول منشور يدويا أو استورد مجموعة بيانات");

        SetCenter(
            PostsEmptyCreateText,
            "+ إضافة أول منشور");

        SetCenter(
            PostsNoResultsTitle,
            "لا توجد نتائج");

        SetCenter(
            PostsNoResultsDescription,
            "جرب كلمة بحث أو مرشحا آخر");


        SetCenter(
            CancelPostEditorTopText,
            "إلغاء");


        SetRight(
            PostContentSectionTitle,
            "المحتوى");

        SetRight(
            PostTitleLabel,
            "عنوان المنشور");

        SetRight(
            PostPlatformLabel,
            "المنصة");

        SetRight(
            PostContentTypeLabel,
            "نوع المحتوى");

        SetRight(
            PostUrlLabel,
            "رابط المنشور");

        SetRight(
            PostPublishedMediaUrlLabel,
            "مصدر الوسائط عند النشر");

        SetRight(
            PostPublishedMediaUrlHint,
            "يجب أن يكون الرابط متاحا للمشاركين دون تسجيل دخول.");

        SetRight(
            PostBodyLabel,
            "النص أو الوصف");


        SetRight(
            PostSourceSectionTitle,
            "المصدر");

        SetRight(
            PostAuthorLabel,
            "اسم الحساب أو الكاتب");

        SetRight(
            PostSourceLabel,
            "المصدر أو المؤسسة");


        SetCenter(
            PostPublishedDateLabel,
            "تاريخ النشر");


        PostPublishedDatePicker.PlaceholderText =
            "اختر تاريخ النشر";


        SetRight(
            PostMetricsSectionTitle,
            "المؤشرات الأصلية للمنشور");

        SetRight(
            PostMetricsSectionSubtitle,
            "هذه الأرقام تصف المنشور الأصلي وليست سلوك المشاركين");


        SetCenter(
            PostLikesLabel,
            "الإعجابات");

        SetCenter(
            PostCommentsLabel,
            "التعليقات");

        SetCenter(
            PostSharesLabel,
            "المشاركات");

        SetCenter(
            PostSavesLabel,
            "الحفظ");

        SetCenter(
            PostViewsLabel,
            "المشاهدات");


        SetRight(
            PostResearchSectionTitle,
            "التصنيف البحثي");

        SetRight(
            PostGroupLabel,
            "المجموعة المستهدفة");

        SetRight(
            PostCategoryLabel,
            "الفئة");

        SetRight(
            PostTopicLabel,
            "الموضوع");

        SetRight(
            PostConditionLabel,
            "الشرط التجريبي");

        SetRight(
            PostExperimentalTagLabel,
            "الوسم التجريبي");

        SetRight(
            PostResearcherNotesLabel,
            "ملاحظات الباحث");


        SetCenter(
            CancelPostEditorText,
            "إلغاء");


        ConfigureArabicTextBox(
            PostTitleBox,
            "مثال: منشور رياضي حول أداء الفريق");

        ConfigureArabicTextBox(
            PostUrlBox,
            "ألصق رابط المنشور الأصلي");

        ConfigureArabicTextBox(
            PostBodyBox,
            "أدخل نص المنشور أو الوصف");

        ConfigureArabicTextBox(
            PostAuthorBox,
            "اسم الحساب أو الكاتب");

        ConfigureArabicTextBox(
            PostSourceBox,
            "مثال: الصفحة الرسمية أو مؤسسة إعلامية");

        ConfigureArabicTextBox(
            PostCategoryBox,
            "مثال: رياضة، صحة، مجتمع");

        ConfigureArabicTextBox(
            PostTopicBox,
            "موضوع البحث");

        ConfigureArabicTextBox(
            PostConditionBox,
            "مثال: المجموعة التجريبية أ");

        ConfigureArabicTextBox(
            PostExperimentalTagBox,
            "وسم داخلي اختياري");

        ConfigureArabicTextBox(
            PostResearcherNotesBox,
            "ملاحظات خاصة بالباحث لا تظهر للمشارك");
    }


    private void ApplyEnglish()
    {
        RootStudyWorkspace.FontFamily =
            _englishFont;


        SetCenter(
            BackToDashboardText,
            "Back");

        SetCenter(
            OverviewButtonText,
            "Overview");

        SetCenter(
            PostsButtonText,
            "Stimuli");

        SetCenter(
            GroupsButtonText,
            "Groups");

        SetCenter(
            ParticipantsButtonText,
            "Participants");

        SetCenter(
            QuestionnairesButtonText,
            "Questionnaires");

        SetCenter(
            ExperimentButtonText,
            "Conditions");

        SetCenter(
            BuilderButtonText,
            "Experiment Builder");

        SetCenter(
            SessionsButtonText,
            "Sessions");

        SetCenter(
            AnalysisButtonText,
            "Analysis");

        SetCenter(
            ReportsButtonText,
            "Reports");

        SetCenter(
            StudySettingsButtonText,
            "Study settings");


        SetCenter(
            GroupsMetricLabel,
            "Groups");

        SetCenter(
            ParticipantsMetricLabel,
            "Participants");

        SetCenter(
            PostsMetricLabel,
            "Posts");

        SetCenter(
            SessionsMetricLabel,
            "Sessions");


        SetLeft(
            QuickStartTitle,
            "Start working on the study");

        SetLeft(
            QuickStartSubtitle,
            "Choose what you want to configure next");


        SetCenter(
            QuickPostsTitle,
            "Add stimuli");

        SetCenter(
            QuickPostsDescription,
            "Create experimental content or import an existing dataset");


        SetCenter(
            QuickParticipantsTitle,
            "Manage participants");

        SetCenter(
            QuickParticipantsDescription,
            "Build the sample and assign participants to groups");


        SetCenter(
            QuickExperimentTitle,
            "Build experiment");

        SetCenter(
            QuickExperimentDescription,
            "Arrange stimuli, questions and session flow");


        SetLeft(
            FoundationTitle,
            "Research foundation");

        SetLeft(
            ResearchQuestionTitle,
            "Research question");

        SetLeft(
            HypothesisTitle,
            "Hypothesis");

        SetLeft(
            DesignTitle,
            "Design");

        SetLeft(
            TargetSampleTitle,
            "Target sample");


        SetLeft(
            PostsPageTitle,
            "Research Stimulus Library");

        SetLeft(
            PostsPageSubtitle,
            "Manage digital content, original source data, and experimental presentation");


        SetCenter(
            AddPostButtonText,
            "+ Add Stimulus");

        SetCenter(
            ImportPostsButtonText,
            "Import stimuli");


        PostsSearchBox.PlaceholderText =
            "Search stimuli";


        SetCenter(
            PostsEmptyTitle,
            "No posts yet");

        SetCenter(
            PostsEmptyDescription,
            "Add your first post manually or import an existing dataset");

        SetCenter(
            PostsEmptyCreateText,
            "+ Add first post");

        SetCenter(
            PostsNoResultsTitle,
            "No results");

        SetCenter(
            PostsNoResultsDescription,
            "Try another search term or filter");


        SetCenter(
            CancelPostEditorTopText,
            "Cancel");


        SetLeft(
            PostContentSectionTitle,
            "Content");

        SetLeft(
            PostTitleLabel,
            "Post title");

        SetLeft(
            PostPlatformLabel,
            "Platform");

        SetLeft(
            PostContentTypeLabel,
            "Content type");

        SetLeft(
            PostUrlLabel,
            "Original URL");

        SetLeft(
            PostPublishedMediaUrlLabel,
            "Published media source");

        SetLeft(
            PostPublishedMediaUrlHint,
            "The URL must be accessible to participants without signing in.");

        SetLeft(
            PostBodyLabel,
            "Text or caption");


        SetLeft(
            PostSourceSectionTitle,
            "Source");

        SetLeft(
            PostAuthorLabel,
            "Account or author");

        SetLeft(
            PostSourceLabel,
            "Source or organisation");


        SetCenter(
            PostPublishedDateLabel,
            "Published date");


        PostPublishedDatePicker.PlaceholderText =
            "Select publication date";


        SetLeft(
            PostMetricsSectionTitle,
            "Original post metrics");

        SetLeft(
            PostMetricsSectionSubtitle,
            "These values describe the original post, not participant behaviour");


        SetCenter(
            PostLikesLabel,
            "Likes");

        SetCenter(
            PostCommentsLabel,
            "Comments");

        SetCenter(
            PostSharesLabel,
            "Shares");

        SetCenter(
            PostSavesLabel,
            "Saves");

        SetCenter(
            PostViewsLabel,
            "Views");


        SetLeft(
            PostResearchSectionTitle,
            "Research classification");

        SetLeft(
            PostGroupLabel,
            "Target group");

        SetLeft(
            PostCategoryLabel,
            "Category");

        SetLeft(
            PostTopicLabel,
            "Topic");

        SetLeft(
            PostConditionLabel,
            "Experimental condition");

        SetLeft(
            PostExperimentalTagLabel,
            "Experimental tag");

        SetLeft(
            PostResearcherNotesLabel,
            "Researcher notes");


        SetCenter(
            CancelPostEditorText,
            "Cancel");


        ConfigureEnglishTextBox(
            PostTitleBox,
            "Example: Sports post about team performance");

        ConfigureEnglishTextBox(
            PostUrlBox,
            "Paste the original post URL");

        ConfigureEnglishTextBox(
            PostBodyBox,
            "Enter the post text or caption");

        ConfigureEnglishTextBox(
            PostAuthorBox,
            "Account or author name");

        ConfigureEnglishTextBox(
            PostSourceBox,
            "Example: official page or news outlet");

        ConfigureEnglishTextBox(
            PostCategoryBox,
            "Example: sport, health, society");

        ConfigureEnglishTextBox(
            PostTopicBox,
            "Research topic");

        ConfigureEnglishTextBox(
            PostConditionBox,
            "Example: experimental group A");

        ConfigureEnglishTextBox(
            PostExperimentalTagBox,
            "Optional internal tag");

        ConfigureEnglishTextBox(
            PostResearcherNotesBox,
            "Private researcher notes, never shown to participants");
    }


    // =========================================================
    // DIRECTION
    // =========================================================

    private void ConfigureHeaderDirection()
    {
        var direction = IsArabic()
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        StudyHeaderTextPanel.FlowDirection = direction;
        StudyTitleLinePanel.FlowDirection = direction;
        StudyTitleText.TextAlignment = IsArabic()
            ? TextAlignment.Right
            : TextAlignment.Left;
        StudySubtitleText.TextAlignment = IsArabic()
            ? TextAlignment.Right
            : TextAlignment.Left;

        if (IsArabic())
        {
            Grid.SetColumn(
                BackToDashboardButton,
                0);

            Grid.SetColumn(
                StudyHeaderTextPanel,
                2);


            BackToDashboardButton.HorizontalAlignment =
                HorizontalAlignment.Left;


            StudyHeaderTextPanel.HorizontalAlignment =
                HorizontalAlignment.Right;
        }
        else
        {
            Grid.SetColumn(
                StudyHeaderTextPanel,
                0);

            Grid.SetColumn(
                BackToDashboardButton,
                2);


            StudyHeaderTextPanel.HorizontalAlignment =
                HorizontalAlignment.Left;


            BackToDashboardButton.HorizontalAlignment =
                HorizontalAlignment.Right;
        }
    }


    private void ConfigureNavigationDirection()
    {
        StudyNavigationPanel.FlowDirection =
            IsArabic()
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;


        StudyNavigationPanel.HorizontalAlignment =
            HorizontalAlignment.Center;
    }


    private void ConfigureQuickStartDirection()
    {
        if (IsArabic())
        {
            Grid.SetColumn(
                QuickPostsButton,
                4);

            Grid.SetColumn(
                QuickParticipantsButton,
                2);

            Grid.SetColumn(
                QuickExperimentButton,
                0);


            QuickStartHeader.HorizontalAlignment =
                HorizontalAlignment.Right;
        }
        else
        {
            Grid.SetColumn(
                QuickPostsButton,
                0);

            Grid.SetColumn(
                QuickParticipantsButton,
                2);

            Grid.SetColumn(
                QuickExperimentButton,
                4);


            QuickStartHeader.HorizontalAlignment =
                HorizontalAlignment.Left;
        }
    }


    private void ConfigurePostsDirection()
    {
        if (IsArabic())
        {
            Grid.SetColumn(
                PostsHeaderActionsPanel,
                0);

            Grid.SetColumn(
                PostsHeaderTextPanel,
                2);


            PostsHeaderActionsPanel.HorizontalAlignment =
                HorizontalAlignment.Left;


            PostsHeaderTextPanel.HorizontalAlignment =
                HorizontalAlignment.Right;


            PostsFilterGrid.ColumnDefinitions =
                new ColumnDefinitions(
                    "Auto,*,150,12,150,12,240");


            Grid.SetColumn(
                PostsCountContainer,
                0);

            Grid.SetColumn(
                ContentTypeFilterComboBox,
                2);

            Grid.SetColumn(
                PlatformFilterComboBox,
                4);

            Grid.SetColumn(
                PostsSearchBox,
                6);
        }
        else
        {
            Grid.SetColumn(
                PostsHeaderTextPanel,
                0);

            Grid.SetColumn(
                PostsHeaderActionsPanel,
                2);


            PostsHeaderTextPanel.HorizontalAlignment =
                HorizontalAlignment.Left;


            PostsHeaderActionsPanel.HorizontalAlignment =
                HorizontalAlignment.Right;


            PostsFilterGrid.ColumnDefinitions =
                new ColumnDefinitions(
                    "240,12,150,12,150,*,Auto");


            Grid.SetColumn(
                PostsSearchBox,
                0);

            Grid.SetColumn(
                PlatformFilterComboBox,
                2);

            Grid.SetColumn(
                ContentTypeFilterComboBox,
                4);

            Grid.SetColumn(
                PostsCountContainer,
                6);
        }


        ConfigureComboDirection();
    }


    private void ConfigurePostEditorDirection()
{
    var isArabic =
        IsArabic();


    // =========================================================
    // EDITOR HEADER
    // =========================================================

    if (isArabic)
    {
        Grid.SetColumn(
            CancelPostEditorTopButton,
            0);

        Grid.SetColumn(
            PostEditorHeaderTextPanel,
            2);


        PostEditorHeaderTextPanel.HorizontalAlignment =
            HorizontalAlignment.Right;


        Grid.SetColumn(
            CancelPostEditorButton,
            0);

        Grid.SetColumn(
            SavePostButton,
            2);


        SetRight(
            PostEditorTitle);

        SetRight(
            PostEditorSubtitle);
    }
    else
    {
        Grid.SetColumn(
            PostEditorHeaderTextPanel,
            0);

        Grid.SetColumn(
            CancelPostEditorTopButton,
            2);


        PostEditorHeaderTextPanel.HorizontalAlignment =
            HorizontalAlignment.Left;


        Grid.SetColumn(
            SavePostButton,
            0);

        Grid.SetColumn(
            CancelPostEditorButton,
            2);


        SetLeft(
            PostEditorTitle);

        SetLeft(
            PostEditorSubtitle);
    }


    // =========================================================
    // PLATFORM + CONTENT TYPE
    //
    // Arabic:
    // Content Type | Platform
    //
    // English:
    // Platform | Content Type
    // =========================================================

    if (PostPlatformLabel.Parent
        is StackPanel platformField)
    {
        Grid.SetColumn(
            platformField,
            isArabic
                ? 2
                : 0);


        platformField.HorizontalAlignment =
            HorizontalAlignment.Stretch;
    }


    if (PostContentTypeLabel.Parent
        is StackPanel contentTypeField)
    {
        Grid.SetColumn(
            contentTypeField,
            isArabic
                ? 0
                : 2);


        contentTypeField.HorizontalAlignment =
            HorizontalAlignment.Stretch;
    }


    // =========================================================
    // AUTHOR + SOURCE
    // =========================================================

    if (PostAuthorLabel.Parent
        is StackPanel authorField)
    {
        Grid.SetColumn(
            authorField,
            isArabic
                ? 2
                : 0);


        authorField.HorizontalAlignment =
            HorizontalAlignment.Stretch;
    }


    if (PostSourceLabel.Parent
        is StackPanel sourceField)
    {
        Grid.SetColumn(
            sourceField,
            isArabic
                ? 0
                : 2);


        sourceField.HorizontalAlignment =
            HorizontalAlignment.Stretch;
    }


    // =========================================================
    // GROUP + CATEGORY
    // =========================================================

    if (PostGroupLabel.Parent
        is StackPanel groupField)
    {
        Grid.SetColumn(
            groupField,
            isArabic
                ? 2
                : 0);


        groupField.HorizontalAlignment =
            HorizontalAlignment.Stretch;
    }


    if (PostCategoryLabel.Parent
        is StackPanel categoryField)
    {
        Grid.SetColumn(
            categoryField,
            isArabic
                ? 0
                : 2);


        categoryField.HorizontalAlignment =
            HorizontalAlignment.Stretch;
    }


    // =========================================================
    // TOPIC + CONDITION
    // =========================================================

    if (PostTopicLabel.Parent
        is StackPanel topicField)
    {
        Grid.SetColumn(
            topicField,
            isArabic
                ? 2
                : 0);


        topicField.HorizontalAlignment =
            HorizontalAlignment.Stretch;
    }


    if (PostConditionLabel.Parent
        is StackPanel conditionField)
    {
        Grid.SetColumn(
            conditionField,
            isArabic
                ? 0
                : 2);


        conditionField.HorizontalAlignment =
            HorizontalAlignment.Stretch;
    }


    // =========================================================
    // LABEL ALIGNMENT
    // =========================================================

    var labels =
        new[]
        {
            PostContentSectionTitle,
            PostTitleLabel,
            PostPlatformLabel,
            PostContentTypeLabel,
            PostUrlLabel,
            PostPublishedMediaUrlLabel,
            PostPublishedMediaUrlHint,
            PostBodyLabel,

            PostSourceSectionTitle,
            PostAuthorLabel,
            PostSourceLabel,

            PostResearchSectionTitle,
            PostGroupLabel,
            PostCategoryLabel,
            PostTopicLabel,
            PostConditionLabel,
            PostExperimentalTagLabel,
            PostResearcherNotesLabel
        };


    foreach (var label in labels)
    {
        label.FontFamily =
            isArabic
                ? _arabicFont
                : _englishFont;


        label.FlowDirection =
            isArabic
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;


        label.TextAlignment =
            isArabic
                ? TextAlignment.Right
                : TextAlignment.Left;


        label.HorizontalAlignment =
            isArabic
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left;
    }


    // =========================================================
    // TEXT BOXES
    // =========================================================

    ConfigureEditorTextBoxes();


    // =========================================================
    // COMBOBOXES
    // =========================================================

    ConfigureComboDirection();
}


    private void ConfigureComboDirection()
    {
        var direction =
            IsArabic()
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;


        var combos =
            new[]
            {
                PostPlatformComboBox,
                PostContentTypeComboBox,
                PostGroupComboBox,
                PlatformFilterComboBox,
                ContentTypeFilterComboBox
            };


        foreach (var combo in combos)
        {
            combo.FontFamily =
                IsArabic()
                    ? _arabicFont
                    : _englishFont;


            combo.FlowDirection =
                direction;


            combo.HorizontalContentAlignment =
                HorizontalAlignment.Center;


            combo.VerticalContentAlignment =
                VerticalAlignment.Center;
        }


        PostPublishedDatePicker.FontFamily =
            IsArabic()
                ? _arabicFont
                : _englishFont;


        PostPublishedDatePicker.FlowDirection =
            direction;


        PostPublishedDatePicker.HorizontalContentAlignment =
            HorizontalAlignment.Center;


        PostPublishedDatePicker.VerticalContentAlignment =
            VerticalAlignment.Center;
    }


    private void ConfigureEditorTextBoxes()
    {
        var boxes =
            new[]
            {
                PostTitleBox,
                PostUrlBox,
                PostPublishedMediaUrlBox,
                PostBodyBox,
                PostAuthorBox,
                PostSourceBox,
                PostCategoryBox,
                PostTopicBox,
                PostConditionBox,
                PostExperimentalTagBox,
                PostResearcherNotesBox
            };


        foreach (var box in boxes)
        {
            box.FontFamily =
                IsArabic()
                    ? _arabicFont
                    : _englishFont;


            box.FlowDirection =
                IsArabic()
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight;


            box.TextAlignment =
                IsArabic()
                    ? TextAlignment.Right
                    : TextAlignment.Left;
        }
    }


    private void ConfigureStudyDirection()
    {
        if (_study is null)
            return;


        StudyHeaderTextPanel.HorizontalAlignment =
            IsArabic()
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left;


        StudyTitleLinePanel.HorizontalAlignment =
            IsArabic()
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left;


        StudyTitleLinePanel.FlowDirection =
            IsArabic()
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;


        StudyTitleText.FontFamily =
            IsArabic()
                ? _arabicFont
                : _englishFont;


        StudyTitleText.FlowDirection =
            IsArabic()
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;


        StudyTitleText.TextAlignment =
            IsArabic()
                ? TextAlignment.Right
                : TextAlignment.Left;


        StudySubtitleText.FontFamily =
            IsArabic()
                ? _arabicFont
                : _englishFont;


        StudySubtitleText.FlowDirection =
            IsArabic()
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;


        StudySubtitleText.TextAlignment =
            IsArabic()
                ? TextAlignment.Right
                : TextAlignment.Left;


        StudyStatusText.FontFamily =
            IsArabic()
                ? _arabicFont
                : _englishFont;


        StudyStatusText.TextAlignment =
            TextAlignment.Center;


        ConfigureHeaderDirection();
    }


    private void ConfigureFoundationDirection()
    {
        if (IsArabic())
        {
            FoundationGrid.ColumnDefinitions =
                new ColumnDefinitions(
                    "*,130");


            Grid.SetColumn(
                ResearchQuestionValue,
                0);

            Grid.SetColumn(
                ResearchQuestionTitle,
                1);


            Grid.SetColumn(
                HypothesisValue,
                0);

            Grid.SetColumn(
                HypothesisTitle,
                1);


            Grid.SetColumn(
                DesignValue,
                0);

            Grid.SetColumn(
                DesignTitle,
                1);


            Grid.SetColumn(
                TargetSampleValue,
                0);

            Grid.SetColumn(
                TargetSampleTitle,
                1);


            FoundationTitle.HorizontalAlignment =
                HorizontalAlignment.Right;
        }
        else
        {
            FoundationGrid.ColumnDefinitions =
                new ColumnDefinitions(
                    "130,*");


            Grid.SetColumn(
                ResearchQuestionTitle,
                0);

            Grid.SetColumn(
                ResearchQuestionValue,
                1);


            Grid.SetColumn(
                HypothesisTitle,
                0);

            Grid.SetColumn(
                HypothesisValue,
                1);


            Grid.SetColumn(
                DesignTitle,
                0);

            Grid.SetColumn(
                DesignValue,
                1);


            Grid.SetColumn(
                TargetSampleTitle,
                0);

            Grid.SetColumn(
                TargetSampleValue,
                1);


            FoundationTitle.HorizontalAlignment =
                HorizontalAlignment.Left;
        }


        var values =
            new[]
            {
                ResearchQuestionValue,
                HypothesisValue,
                DesignValue,
                TargetSampleValue
            };


        foreach (var value in values)
        {
            value.FontFamily =
                IsArabic()
                    ? _arabicFont
                    : _englishFont;


            value.FlowDirection =
                IsArabic()
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight;


            value.TextAlignment =
                IsArabic()
                    ? TextAlignment.Right
                    : TextAlignment.Left;
        }
    }


    private void ConfigureSectionText()
    {
        SectionTitle.FontFamily =
            IsArabic()
                ? _arabicFont
                : _englishFont;


        SectionDescription.FontFamily =
            IsArabic()
                ? _arabicFont
                : _englishFont;


        SectionTitle.FlowDirection =
            IsArabic()
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;


        SectionDescription.FlowDirection =
            IsArabic()
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;


        SectionTitle.TextAlignment =
            TextAlignment.Center;


        SectionDescription.TextAlignment =
            TextAlignment.Center;
    }


    // =========================================================
    // LOCALIZED VALUES
    // =========================================================

    private string GetLocalizedStatus(
        string status)
    {
        if (!IsArabic())
            return status;


        return status switch
        {
            "Draft" =>
                "مسودة",

            "Ready" =>
                "جاهزة",

            "Running" =>
                "قيد التنفيذ",

            "Paused" =>
                "متوقفة مؤقتا",

            "Completed" =>
                "مكتملة",

            "Archived" =>
                "مؤرشفة",

            _ =>
                status
        };
    }


    private string GetLocalizedDesign()
    {
        if (_study is null)
            return "—";


        if (!IsArabic())
        {
            return _study.DesignType switch
            {
                "BetweenSubjects" =>
                    "Between-subjects",

                "WithinSubjects" =>
                    "Within-subjects",

                "Mixed" =>
                    "Mixed design",

                "SingleGroup" =>
                    "Single group",

                _ =>
                    _study.DesignType
            };
        }


        return _study.DesignType switch
        {
            "BetweenSubjects" =>
                "بين المجموعات",

            "WithinSubjects" =>
                "داخل المجموعة",

            "Mixed" =>
                "تصميم مختلط",

            "SingleGroup" =>
                "مجموعة واحدة",

            _ =>
                _study.DesignType
        };
    }


    private string GetStudySubtitle()
    {
        if (_study is null)
            return string.Empty;


        var updated =
            _study.UpdatedAtUtc
                .ToLocalTime()
                .ToString(
                    "dd MMM yyyy");


        return IsArabic()
            ? $"آخر تحديث: {updated}"
            : $"Last updated: {updated}";
    }


    private string GetLocalizedPlatform(
        string platform)
    {
        if (!IsArabic())
            return platform;


        return platform switch
        {
            "Generic" =>
                "عام",

            "Facebook" =>
                "فيسبوك",

            "Instagram" =>
                "إنستغرام",

            "TikTok" =>
                "تيك توك",

            "YouTube" =>
                "يوتيوب",

            "News" =>
                "إخباري",

            "Custom" =>
                "مخصص",

            _ =>
                platform
        };
    }


    private string GetLocalizedContentType(
        string contentType)
    {
        if (!IsArabic())
            return contentType;


        return contentType switch
        {
            "Text" =>
                "نص",

            "Image" =>
                "صورة",

            "Video" =>
                "فيديو",

            "Audio" =>
                "صوت",

            "Link" =>
                "رابط",

            "Mixed" =>
                "مختلط",

            _ =>
                contentType
        };
    }


    // =========================================================
    // DATE
    // =========================================================

    private DateTime? GetSelectedPublishedDateUtc()
    {
        if (!PostPublishedDatePicker
                .SelectedDate
                .HasValue)
        {
            return null;
        }


        var selected =
            DateTime.SpecifyKind(
                PostPublishedDatePicker
                    .SelectedDate
                    .Value
                    .Date,
                DateTimeKind.Local);


        return selected
            .ToUniversalTime();
    }


    // =========================================================
    // HELPERS
    // =========================================================

    private string GetPostsCountText(
        int filteredCount,
        int totalCount)
    {
        if (filteredCount ==
            totalCount)
        {
            return IsArabic()
                ? $"{totalCount} منشور"
                : totalCount == 1
                    ? "1 post"
                    : $"{totalCount} posts";
        }


        return IsArabic()
            ? $"{filteredCount} من {totalCount}"
            : $"{filteredCount} of {totalCount}";
    }


    private static string BuildPostMetricsSummary(
        StimulusPost post)
    {
        var values =
            new List<string>();


        if (post.OriginalLikes.HasValue)
        {
            values.Add(
                $"♥ {post.OriginalLikes.Value:N0}");
        }


        if (post.OriginalComments.HasValue)
        {
            values.Add(
                $"C {post.OriginalComments.Value:N0}");
        }


        if (post.OriginalShares.HasValue)
        {
            values.Add(
                $"S {post.OriginalShares.Value:N0}");
        }


        if (post.OriginalViews.HasValue)
        {
            values.Add(
                $"V {post.OriginalViews.Value:N0}");
        }


        return string.Join(
            "  ·  ",
            values);
    }


    private static int? ToNullableInt(
        decimal? value)
    {
        return value.HasValue
            ? Convert.ToInt32(
                value.Value)
            : null;
    }


    private static long? ToNullableLong(
        decimal? value)
    {
        return value.HasValue
            ? Convert.ToInt64(
                value.Value)
            : null;
    }


    private static string? NormalizeOptional(
        string? value)
    {
        return string.IsNullOrWhiteSpace(
            value)
            ? null
            : value.Trim();
    }


    private static IBrush Brush(
        string hex)
    {
        return new SolidColorBrush(
            Color.Parse(
                hex));
    }


    private void ShowPostEditorError(
        string message)
    {
        PostEditorErrorText.Text =
            message;


        PostEditorErrorText.FontFamily =
            IsArabic()
                ? _arabicFont
                : _englishFont;


        PostEditorErrorText.FlowDirection =
            IsArabic()
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;


        PostEditorErrorText.TextAlignment =
            IsArabic()
                ? TextAlignment.Right
                : TextAlignment.Left;


        PostEditorErrorText.IsVisible =
            true;
    }


    private void ClearPostEditorError()
    {
        PostEditorErrorText.Text =
            string.Empty;


        PostEditorErrorText.IsVisible =
            false;
    }


    // =========================================================
    // TEXT HELPERS
    // =========================================================

    private void SetRight(
        TextBlock block,
        string? text = null)
    {
        if (text is not null)
        {
            block.Text =
                text;
        }


        block.FontFamily =
            _arabicFont;


        block.FlowDirection =
            FlowDirection.RightToLeft;


        block.TextAlignment =
            TextAlignment.Right;


        block.HorizontalAlignment =
            HorizontalAlignment.Right;
    }


    private void SetLeft(
        TextBlock block,
        string? text = null)
    {
        if (text is not null)
        {
            block.Text =
                text;
        }


        block.FontFamily =
            _englishFont;


        block.FlowDirection =
            FlowDirection.LeftToRight;


        block.TextAlignment =
            TextAlignment.Left;


        block.HorizontalAlignment =
            HorizontalAlignment.Left;
    }


    private void SetCenter(
        TextBlock block,
        string text)
    {
        block.Text =
            text;


        block.FontFamily =
            IsArabic()
                ? _arabicFont
                : _englishFont;


        block.FlowDirection =
            IsArabic()
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;


        block.TextAlignment =
            TextAlignment.Center;


        block.HorizontalAlignment =
            HorizontalAlignment.Center;
    }


    private void ConfigureArabicTextBox(
        TextBox box,
        string placeholder)
    {
        box.FontFamily =
            _arabicFont;


        box.FlowDirection =
            FlowDirection.RightToLeft;


        box.TextAlignment =
            TextAlignment.Right;


        box.PlaceholderText =
            placeholder;
    }


    private void ConfigureEnglishTextBox(
        TextBox box,
        string placeholder)
    {
        box.FontFamily =
            _englishFont;


        box.FlowDirection =
            FlowDirection.LeftToRight;


        box.TextAlignment =
            TextAlignment.Left;


        box.PlaceholderText =
            placeholder;
    }
    private void ConfigureCompactSelectionFields()
    {
        var isArabic =
            IsArabic();


        ConfigureCompactSelectionField(
            PostPlatformLabel,
            PostPlatformComboBox,
            isArabic);


        ConfigureCompactSelectionField(
            PostContentTypeLabel,
            PostContentTypeComboBox,
            isArabic);


        ConfigureCompactSelectionField(
            PostGroupLabel,
            PostGroupComboBox,
            isArabic);
    }


    private void ConfigureCompactSelectionField(
        TextBlock label,
        ComboBox comboBox,
        bool isArabic)
    {
        // Label and control belong visually together.

        label.HorizontalAlignment =
            HorizontalAlignment.Center;


        label.TextAlignment =
            TextAlignment.Center;


        label.FlowDirection =
            isArabic
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;


        comboBox.HorizontalAlignment =
            HorizontalAlignment.Center;


        comboBox.HorizontalContentAlignment =
            HorizontalAlignment.Center;


        comboBox.VerticalContentAlignment =
            VerticalAlignment.Center;


        comboBox.FlowDirection =
            isArabic
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;


        comboBox.MinWidth =
            150;


        comboBox.MaxWidth =
            210;
    }
}
