using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

public enum SplashStartupStage
{
    PreparingWorkspace = 1,
    CheckingResearchStorage = 2,
    InitializingData = 3,
    Ready = 4
}

public partial class SplashView : UserControl
{
    private const int MinimumVisibleMilliseconds = 3000;
    public event EventHandler? SplashCompleted;
    private Task? _entranceTask;
    private bool _startupRunning;
    private readonly TaskCompletionSource<long> _firstRendered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SplashView()
    {
        InitializeComponent();
        ConfigureLanguage();
        AttachedToVisualTree += (_, _) =>
        {
            _entranceTask ??= RunEntranceAsync();
            Dispatcher.UIThread.Post(
                () => _firstRendered.TrySetResult(Stopwatch.GetTimestamp()),
                DispatcherPriority.Render);
        };
    }

    public async Task RunStartupAsync(Func<Action<SplashStartupStage>, Task> initializeAsync)
    {
        if (_startupRunning) return;
        _startupRunning = true;
        _entranceTask ??= RunEntranceAsync();
        var firstRenderedTimestamp = await _firstRendered.Task;
        SetStage(SplashStartupStage.PreparingWorkspace);
        await Task.WhenAll(_entranceTask, initializeAsync(SetStage));
        SetStage(SplashStartupStage.Ready);
        var visibleMilliseconds = (int)Stopwatch.GetElapsedTime(
            firstRenderedTimestamp,
            Stopwatch.GetTimestamp()).TotalMilliseconds;
        var remaining = MinimumVisibleMilliseconds - visibleMilliseconds;
        if (remaining > 0) await Task.Delay(remaining);
        await Task.Delay(180);
        var measuredVisibleMilliseconds = (long)Stopwatch.GetElapsedTime(
            firstRenderedTimestamp,
            Stopwatch.GetTimestamp()).TotalMilliseconds;
        ApplicationDiagnosticsService.LogInformation(
            "Splash visible duration",
            $"First rendered to transition: {measuredVisibleMilliseconds} ms; " +
            $"required minimum: {MinimumVisibleMilliseconds} ms.");
        await FadeToAsync(this, 0, 240);
        SplashCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void ConfigureLanguage()
    {
        var arabic = LocalizationService.IsArabic;
        SplashRoot.FontFamily = new FontFamily(arabic
            ? "avares://SOCYVIA/Assets/Fonts#IBM Plex Sans Arabic"
            : "avares://SOCYVIA/Assets/Fonts#IBM Plex Sans");
        DescriptorText.Text = arabic
            ? SocyviaProductIdentity.ArabicPositioning
            : SocyviaProductIdentity.EnglishPositioning;
        SupportingLineText.Text = arabic
            ? "صمم التجارب • قس السلوك • تفحص الأدلة"
            : "Design Experiments • Measure Behavior • Examine Evidence";
        DescriptorText.FlowDirection = SupportingLineText.FlowDirection = arabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        StartupStatusText.Text = arabic ? "تحضير مساحة العمل" : "Preparing workspace";
    }

    private async Task RunEntranceAsync()
    {
        await FadeToAsync(AtmosphereLayer, 1, 180);
        await FadeToAsync(BrandPanel, 1, 300);
        await Task.WhenAll(
            FadeToAsync(SignalPointOne, 1, 120),
            FadeToAsync(SignalPointTwo, 1, 180),
            FadeToAsync(SignalPointThree, 1, 240));
        await FadeToAsync(DeveloperCredit, 1, 180);
    }

    private void SetStage(SplashStartupStage stage)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SetStage(stage));
            return;
        }
        StartupStatusText.Text = stage switch
        {
            SplashStartupStage.PreparingWorkspace => Text("تحضير مساحة العمل", "Preparing workspace"),
            SplashStartupStage.CheckingResearchStorage => Text("فحص مساحة البحث المحلية", "Checking research storage"),
            SplashStartupStage.InitializingData => Text("تهيئة البيانات", "Initializing data"),
            _ => Text("جاهز", "Ready")
        };
        var active = new SolidColorBrush(Color.Parse("#2563EB"));
        var inactive = new SolidColorBrush(Color.Parse("#D8E0EB"));
        var stages = new[] { StageOne, StageTwo, StageThree, StageFour };
        for (var index = 0; index < stages.Length; index++)
            stages[index].Background = index < (int)stage ? active : inactive;
    }

    private static async Task FadeToAsync(Control control, double target, int durationMilliseconds)
    {
        const int frames = 10;
        var start = control.Opacity;
        for (var frame = 1; frame <= frames; frame++)
        {
            control.Opacity = start + (target - start) * frame / frames;
            await Task.Delay(durationMilliseconds / frames);
        }
        control.Opacity = target;
    }

    private static string Text(string arabic, string english) =>
        UiTextService.Localized(arabic, english);
}
