using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using SOCYVIA.Models;
using SOCYVIA.Repositories;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

public partial class ParticipantExperimentView : UserControl
{
    private readonly string _sessionId;
    private readonly ExperimentRuntimeService _runtime = new();
    private readonly PostExposureTracker _exposureTracker = new();
    private readonly Dictionary<string, Control> _postControls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuntimePostPresentation> _posts = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _exposureTimer = new() { Interval = TimeSpan.FromMilliseconds(125) };
    private ExperimentRuntimeContext? _context;
    private RuntimePostPresentation? _focusedPost;
    private long _focusedStartedMilliseconds;
    private bool _initialized;
    private bool _busy;
    private bool _isRunning;
    private bool _isPaused;
    private bool _updatingVisibility;
    private long _lastScrollTelemetryMilliseconds = -1_000;
    private double _maximumScrollDepth;
    private List<QuestionnaireAssignment> _preQuestionnaires = [];
    private List<QuestionnaireAssignment> _postQuestionnaires = [];
    private int _preQuestionnaireIndex;
    private int _postQuestionnaireIndex;
    private bool _awaitingPostQuestionnaire;

    public ParticipantExperimentView() : this(string.Empty) { }

    public ParticipantExperimentView(string sessionId)
    {
        _sessionId = sessionId;
        InitializeComponent();
        ConfigureLanguage();
        StartButton.IsEnabled = false;
        StartButton.Click += async (_, _) => await StartExperimentAsync();
        PauseButton.Click += async (_, _) => await PauseExperimentAsync();
        ResumeButton.Click += async (_, _) => await ResumeExperimentAsync();
        FinishButton.Click += async (_, _) => await FinishExperimentAsync();
        FeedScrollViewer.ScrollChanged += OnScrollChanged;
        _exposureTimer.Tick += async (_, _) => await UpdateVisibilityAsync();
        Loaded += async (_, _) => await InitializeRuntimeAsync();
    }

    public event Action<ParticipantSessionSummary>? ExperimentFinished;
    public bool IsRunningOrPaused => _isRunning || _isPaused || _awaitingPostQuestionnaire;

    public async Task<ParticipantSessionSummary> InterruptAsync()
    {
        if (!_isRunning && !_isPaused && !_awaitingPostQuestionnaire)
            throw new InvalidOperationException("The participant session has not started.");
        await CloseFocusedPostAsync(true);
        await HideVisiblePostsAsync();
        _exposureTimer.Stop();
        _isRunning = false;
        _isPaused = false;
        return await _runtime.InterruptAsync(
            "Participant experiment window closed unexpectedly");
    }

    private async Task InitializeRuntimeAsync()
    {
        if (_initialized) return;
        try
        {
            _context = await _runtime.InitializeAsync(_sessionId);
            foreach (var post in _context.Posts)
            {
                _posts.Add(post.Source.StimulusId, post);
                var card = ParticipantFeedCardFactory.Create(post, CreateCardOptions(post));
                _postControls.Add(post.Source.StimulusId, card);
                FeedPanel.Children.Add(card);
            }

            ParticipantIdentityText.Text = Localize(
                $"رمز المشارك: {_context.Participant.ParticipantCode}",
                $"Participant code: {_context.Participant.ParticipantCode}");
            var consentReady = !_context.Snapshot.ConsentRequired ||
                               _context.Participant.ConsentAccepted;
            var eligible = _context.Participant.IsEligible &&
                           !_context.Participant.IsExcluded &&
                           !_context.Participant.HasWithdrawn;
            ConsentReadinessText.Text = consentReady && eligible
                ? Localize("الأهلية والموافقة جاهزتان", "Eligibility and consent are ready")
                : Localize(
                    "لا يمكن البدء قبل توثيق الأهلية والموافقة المطلوبة",
                    "The session cannot start until eligibility and required consent are documented");
            ConsentReadinessText.Foreground = Brush(consentReady && eligible ? "#287861" : "#B04A5F");
            ProgressText.Text = $"0 / {_context.Posts.Count}";
            PauseButton.IsVisible = _context.Snapshot.AllowSessionResume;
            await LoadQuestionnaireStagesAsync();
            _initialized = true;
            StartButton.IsEnabled = consentReady && eligible && _preQuestionnaires.Count == 0;
            if (consentReady && eligible && _preQuestionnaires.Count > 0)
                ShowPreQuestionnaire();
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Initialize participant runtime");
            ShowError(Localize(
                "تعذر فتح جلسة التجربة. يرجى إبلاغ الباحث",
                "The research session could not be opened. Please notify the researcher."));
            StartButton.IsEnabled = false;
        }
    }

    private ParticipantFeedCardOptions CreateCardOptions(RuntimePostPresentation post) => new()
    {
        InteractionAsync = (eventType, target, valueBoolean, valueText) =>
            _runtime.TrackInteractionAsync(
                eventType, post, target, valueBoolean, valueText).AsTask(),
        OpenRequestedAsync = OpenFocusedPostAsync,
        CommentRequestedAsync = OpenFocusedPostAsync
    };

    private async Task StartExperimentAsync()
    {
        if (_busy || !_initialized || _context is null) return;
        _busy = true;
        StartButton.IsEnabled = false;
        try
        {
            await _runtime.StartAsync();
            _isRunning = true;
            StartPanel.IsVisible = false;
            FeedScrollViewer.IsVisible = true;
            FinishButton.IsVisible = true;
            PauseButton.IsVisible = _context.Snapshot.AllowSessionResume;
            SessionHeaderText.Text = Localize("جلسة بحثية", "Research session");
            _exposureTimer.Start();
            Dispatcher.UIThread.Post(() => _ = UpdateVisibilityAsync(), DispatcherPriority.Render);
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Start participant experiment");
            ShowError(Localize(
                "تعذر بدء التجربة. يرجى إبلاغ الباحث",
                "The experiment could not start. Please notify the researcher."));
            StartButton.IsEnabled = true;
        }
        finally { _busy = false; }
    }

    private async Task PauseExperimentAsync()
    {
        if (_busy || !_isRunning) return;
        _busy = true;
        try
        {
            await CloseFocusedPostAsync(true);
            await HideVisiblePostsAsync();
            _exposureTimer.Stop();
            await _runtime.PauseAsync();
            _isRunning = false;
            _isPaused = true;
            PausePanel.IsVisible = true;
            FeedScrollViewer.IsVisible = false;
            PauseButton.IsVisible = false;
            FinishButton.IsVisible = false;
        }
        catch (Exception exception) { ShowRuntimeFailure(exception); }
        finally { _busy = false; }
    }

    private async Task ResumeExperimentAsync()
    {
        if (_busy || !_isPaused || _context is null) return;
        _busy = true;
        ResumeButton.IsEnabled = false;
        try
        {
            await _runtime.ResumeAsync();
            _isPaused = false;
            _isRunning = true;
            PausePanel.IsVisible = false;
            FeedScrollViewer.IsVisible = true;
            PauseButton.IsVisible = _context.Snapshot.AllowSessionResume;
            FinishButton.IsVisible = true;
            _exposureTimer.Start();
            Dispatcher.UIThread.Post(() => _ = UpdateVisibilityAsync(), DispatcherPriority.Render);
        }
        catch (Exception exception) { ShowRuntimeFailure(exception); }
        finally
        {
            ResumeButton.IsEnabled = true;
            _busy = false;
        }
    }

    private async Task FinishExperimentAsync()
    {
        if (_busy || (!_isRunning && !_isPaused)) return;
        _busy = true;
        PauseButton.IsEnabled = false;
        FinishButton.IsEnabled = false;
        try
        {
            await CloseFocusedPostAsync(true);
            await HideVisiblePostsAsync();
            _exposureTimer.Stop();
            await _runtime.EndExperimentPhaseAsync();
            _isRunning = false;
            _isPaused = false;
            if (_postQuestionnaires.Count > 0)
            {
                _awaitingPostQuestionnaire = true;
                ShowPostQuestionnaire();
            }
            else
            {
                await CompleteSessionAsync();
            }
        }
        catch (Exception exception)
        {
            ShowRuntimeFailure(exception);
            PauseButton.IsEnabled = true;
            FinishButton.IsEnabled = true;
        }
        finally { _busy = false; }
    }

    private async Task LoadQuestionnaireStagesAsync()
    {
        if (_context is null) return;
        var pre = await QuestionnaireRepository.GetAssignmentsAsync(_context.Session.StudyId, QuestionnairePlacements.PreExperiment);
        var post = await QuestionnaireRepository.GetAssignmentsAsync(_context.Session.StudyId, QuestionnairePlacements.PostExperiment);
        _preQuestionnaires = await IncompleteAssignmentsAsync(pre);
        _postQuestionnaires = await IncompleteAssignmentsAsync(post);
    }

    private async Task<List<QuestionnaireAssignment>> IncompleteAssignmentsAsync(
        IReadOnlyList<QuestionnaireAssignment> assignments)
    {
        var result = new List<QuestionnaireAssignment>();
        if (_context is null) return result;
        foreach (var assignment in assignments)
        {
            var completed = await QuestionnaireRepository.GetCompletedResponseAsync(
                assignment.Id, _context.Session.ParticipantId, _context.Session.Id);
            if (completed is null) result.Add(assignment);
        }
        return result;
    }

    private void ShowPreQuestionnaire()
    {
        if (_context is null || _preQuestionnaireIndex >= _preQuestionnaires.Count)
        {
            BeforeQuestionnaireStageHost.IsVisible = false;
            StartButton.IsVisible = true;
            StartButton.IsEnabled = true;
            return;
        }
        StartButton.IsVisible = false;
        BeforeQuestionnaireStageHost.IsVisible = true;
        var view = new ParticipantQuestionnaireView(
            _preQuestionnaires[_preQuestionnaireIndex], _context.Session);
        view.Completed += _ =>
        {
            _preQuestionnaireIndex++;
            ShowPreQuestionnaire();
        };
        BeforeQuestionnaireStageHost.Content = view;
    }

    private void ShowPostQuestionnaire()
    {
        if (_context is null || _postQuestionnaireIndex >= _postQuestionnaires.Count)
        {
            _ = CompleteSessionAsync();
            return;
        }
        FeedScrollViewer.IsVisible = false;
        FocusedScrollViewer.IsVisible = false;
        StartPanel.IsVisible = false;
        AfterQuestionnaireStageHost.IsVisible = true;
        _ = _runtime.TrackSessionEventAsync(
            CanonicalInteractionEventTypes.QuestionnaireStarted,
            "PostExperimentQuestionnaire",
            _postQuestionnaires[_postQuestionnaireIndex].QuestionnaireVersionId);
        var view = new ParticipantQuestionnaireView(
            _postQuestionnaires[_postQuestionnaireIndex], _context.Session);
        view.Completed += async _ =>
        {
            await _runtime.TrackSessionEventAsync(
                CanonicalInteractionEventTypes.QuestionnaireCompleted,
                "PostExperimentQuestionnaire",
                _postQuestionnaires[_postQuestionnaireIndex].QuestionnaireVersionId);
            _postQuestionnaireIndex++;
            ShowPostQuestionnaire();
        };
        AfterQuestionnaireStageHost.Content = view;
    }

    private async Task CompleteSessionAsync()
    {
        try
        {
            AfterQuestionnaireStageHost.IsVisible = false;
            var summary = await _runtime.CompleteAsync();
            _awaitingPostQuestionnaire = false;
            ExperimentFinished?.Invoke(summary);
        }
        catch (Exception exception)
        {
            _awaitingPostQuestionnaire = true;
            ShowRuntimeFailure(exception);
        }
    }

    private async Task OpenFocusedPostAsync(RuntimePostPresentation post)
    {
        if (!_isRunning || _focusedPost is not null) return;
        await HideVisiblePostsAsync();
        _focusedPost = post;
        _focusedStartedMilliseconds = _runtime.ElapsedMilliseconds;
        await _runtime.TrackInteractionAsync(
            CanonicalInteractionEventTypes.PostOpened, post, "FocusedPost");
        await _runtime.TrackInteractionAsync(
            CanonicalInteractionEventTypes.FocusedViewStarted, post, "FocusedPost");
        if (ParticipantFeedCardFactory.GetConfiguredCommentCount(post.Source) > 0)
            await _runtime.TrackInteractionAsync(
                CanonicalInteractionEventTypes.CommentsViewed, post, "ConfiguredComments");

        FocusedHost.Content = ParticipantFeedCardFactory.CreateFocused(
            post,
            CreateCardOptions(post),
            () => CloseFocusedPostAsync(true));
        FeedScrollViewer.IsVisible = false;
        FocusedScrollViewer.Offset = Vector.Zero;
        FocusedScrollViewer.IsVisible = true;
    }

    private async Task CloseFocusedPostAsync(bool trackTelemetry)
    {
        if (_focusedPost is null) return;
        var post = _focusedPost;
        var duration = Math.Max(0, _runtime.ElapsedMilliseconds - _focusedStartedMilliseconds);
        if (trackTelemetry && _isRunning)
        {
            await _runtime.TrackTimedInteractionAsync(
                CanonicalInteractionEventTypes.FocusedViewEnded,
                post,
                "FocusedPost",
                duration);
            await _runtime.TrackTimedInteractionAsync(
                CanonicalInteractionEventTypes.PostClosed,
                post,
                "FocusedPost",
                duration);
        }
        _focusedPost = null;
        FocusedHost.Content = null;
        FocusedScrollViewer.IsVisible = false;
        if (_isRunning)
        {
            FeedScrollViewer.IsVisible = true;
            Dispatcher.UIThread.Post(() => _ = UpdateVisibilityAsync(), DispatcherPriority.Render);
        }
    }

    private async void OnScrollChanged(object? sender, ScrollChangedEventArgs eventArgs)
    {
        await UpdateVisibilityAsync();
        if (!_isRunning || _focusedPost is not null) return;
        var elapsed = _runtime.ElapsedMilliseconds;
        var scrollableHeight = Math.Max(0, FeedScrollViewer.Extent.Height - FeedScrollViewer.Viewport.Height);
        var depth = scrollableHeight <= 0 ? 100 : Math.Clamp(
            FeedScrollViewer.Offset.Y / scrollableHeight * 100, 0, 100);
        try
        {
            if (elapsed - _lastScrollTelemetryMilliseconds >= 250)
            {
                _lastScrollTelemetryMilliseconds = elapsed;
                await _runtime.TrackScrollAsync(FeedScrollViewer.Offset.Y, depth);
            }
            if (depth >= _maximumScrollDepth + 1 || depth >= 100 && _maximumScrollDepth < 100)
            {
                _maximumScrollDepth = depth;
                await _runtime.TrackScrollDepthAsync(FeedScrollViewer.Offset.Y, depth);
            }
        }
        catch (Exception exception) { ShowRuntimeFailure(exception); }
    }

    private async Task UpdateVisibilityAsync()
    {
        if (!_isRunning || _updatingVisibility || _context is null || _focusedPost is not null)
            return;
        _updatingVisibility = true;
        try
        {
            var viewportHeight = FeedScrollViewer.Viewport.Height;
            var viewportWidth = FeedScrollViewer.Viewport.Width;
            if (viewportHeight <= 0) return;
            _runtime.UpdateViewport(viewportWidth, viewportHeight);
            foreach (var item in _postControls)
            {
                var control = item.Value;
                var origin = control.TranslatePoint(new Point(0, 0), FeedScrollViewer);
                if (!origin.HasValue || control.Bounds.Height <= 0) continue;
                var top = origin.Value.Y;
                var bottom = top + control.Bounds.Height;
                var visibleHeight = Math.Max(0,
                    Math.Min(bottom, viewportHeight) - Math.Max(top, 0));
                var ratio = visibleHeight / control.Bounds.Height;
                foreach (var transition in _exposureTracker.Update(
                             item.Key, ratio, _runtime.ElapsedMilliseconds))
                    await _runtime.TrackExposureTransitionAsync(
                        transition, _posts[item.Key].PresentationOrder);
            }
            ProgressText.Text = $"{_exposureTracker.ExposedCount} / {_context.Posts.Count}";
        }
        catch (Exception exception) { ShowRuntimeFailure(exception); }
        finally { _updatingVisibility = false; }
    }

    private async Task HideVisiblePostsAsync()
    {
        foreach (var transition in _exposureTracker.HideAll(_runtime.ElapsedMilliseconds))
            await _runtime.TrackExposureTransitionAsync(
                transition, _posts[transition.StimulusId].PresentationOrder);
    }

    private void ConfigureLanguage()
    {
        var arabic = LocalizationService.IsArabic;
        ParticipantRoot.FontFamily = new FontFamily(arabic
            ? "avares://SOCYVIA/Assets/Fonts#IBM Plex Sans Arabic"
            : "avares://SOCYVIA/Assets/Fonts#IBM Plex Sans");
        ParticipantRoot.FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        WelcomeTitle.Text = Localize("مرحبا بك", "Welcome");
        WelcomeText.Text = Localize(
            "ستشاهد محتوى ضمن بيئة بحثية محلية. تفاعل بصورة طبيعية ثم أكمل الجلسة عند الانتهاء",
            "You will view content in a local research environment. Interact naturally, then finish the session when you are done.");
        StartButton.Content = Localize("ابدأ التجربة", "Start Experiment");
        PausedTitle.Text = Localize("الجلسة متوقفة مؤقتا", "Session paused");
        PausedText.Text = Localize(
            "لن يتم احتساب وقت العرض أثناء التوقف",
            "Exposure time is not counted while paused.");
        ResumeButton.Content = Localize("متابعة", "Resume");
        PauseButton.Content = Localize("إيقاف مؤقت", "Pause");
        FinishButton.Content = Localize("إنهاء التجربة", "Finish experiment");
        PrivacyText.Text = Localize(
            "تحفظ بيانات الجلسة محليا لاغراض البحث",
            "Session data is stored locally for research purposes.");
        SessionHeaderText.Text = Localize("جلسة جاهزة", "Session ready");
    }

    private void ShowRuntimeFailure(Exception exception)
    {
        ApplicationDiagnosticsService.LogException(exception, "Participant runtime");
        ShowError(Localize(
            "حدث خطأ أثناء حفظ بيانات الجلسة. يرجى إبلاغ الباحث وعدم إغلاق النافذة",
            "Session data could not be saved safely. Please notify the researcher and keep this window open."));
    }

    private void ShowError(string message)
    {
        RuntimeErrorText.Text = message;
        RuntimeErrorText.IsVisible = true;
        StartPanel.IsVisible = true;
    }

    private static string Localize(string arabic, string english) =>
        LocalizationService.IsArabic ? arabic : english;
    private static SolidColorBrush Brush(string value) => new(Color.Parse(value));
}
