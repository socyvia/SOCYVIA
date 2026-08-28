using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Threading;
using SOCYVIA.Models;
using SOCYVIA.Repositories;
using SOCYVIA.Services;
using SOCYVIA.Data;

namespace SOCYVIA.Views;

public partial class MainWindow : Window
{
    private bool _startupStarted;
    private bool _safeCloseApproved;
    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public MainWindow()
    {
        InitializeComponent();
        // Progressive enhancement: Windows/macOS may provide a native material;
        // the shared semi-solid surfaces remain the intentional Linux/fallback skin.
        TransparencyLevelHint =
        [
            Avalonia.Controls.WindowTransparencyLevel.Mica,
            Avalonia.Controls.WindowTransparencyLevel.AcrylicBlur,
            Avalonia.Controls.WindowTransparencyLevel.Blur,
            Avalonia.Controls.WindowTransparencyLevel.None
        ];

        ApplicationWindowChrome.Attach(this, showMinimize: true, showMaximize: false);
        ApplicationWindowChrome.IsVisible = false;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;

        DesktopActivationBroker.ActivationRequested += OnDesktopActivationRequested;

        SplashContent.SplashCompleted +=
            OnSplashCompleted;

        Opened += async (_, _) => await InitializeApplicationAsync();
        Closing += async (_, eventArgs) =>
        {
            if (_safeCloseApproved || !StudySaveCoordinatorRegistry.HasUnsafeChanges) return;
            eventArgs.Cancel = true;
            if (!await StudySaveCoordinatorRegistry.FlushAllAsync()) return;
            _safeCloseApproved = true;
            Close();
        };
        Closed += (_, _) => DesktopActivationBroker.ActivationRequested -= OnDesktopActivationRequested;
    }

    private void OnDesktopActivationRequested(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show();
            Activate();
        });

    private async Task InitializeApplicationAsync()
    {
        if (_startupStarted) return;
        _startupStarted = true;
        try
        {
            await SplashContent.RunStartupAsync(async updateStage =>
            {
                updateStage(SplashStartupStage.CheckingResearchStorage);
                StorageService.Initialize();
                await Task.Yield();
                updateStage(SplashStartupStage.InitializingData);
                await DatabaseInitializer.InitializeAsync();
            });
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Application startup");
            await ShowStartupFailureAsync();
        }
    }

    private async Task ShowStartupFailureAsync()
    {
        SplashContent.SplashCompleted -= OnSplashCompleted;
        await FadeOutAsync(SplashContent);
        ApplicationWindowChrome.IsVisible = true;
        ApplicationWindowChrome.Attach(this, showMinimize: true, showMaximize: false);
        Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#EEF5FC"));
        TransparencyLevelHint = [Avalonia.Controls.WindowTransparencyLevel.None];
        var close = new Button
        {
            Content = LocalizationService.IsArabic ? "إغلاق" : "Close",
            Classes = { "primary" }, MinWidth = 100,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        close.Click += (_, _) => Close();
        MainContent.Content = new Border
        {
            Margin = new Avalonia.Thickness(30), Padding = new Avalonia.Thickness(34),
            MaxWidth = 600,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F9FCFF")),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#C7D8ED")),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(18),
            BoxShadow = new Avalonia.Media.BoxShadows(new Avalonia.Media.BoxShadow
            {
                Blur = 24,
                OffsetY = 8,
                Color = Avalonia.Media.Color.Parse("#1A31577A")
            }),
            Child = new StackPanel
            {
                Spacing = 18, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = LocalizationService.IsArabic ? "تعذر بدء SOCYVIA" : "SOCYVIA could not start",
                        FontSize = 22,
                        FontWeight = Avalonia.Media.FontWeight.SemiBold,
                        Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#183153")),
                        TextAlignment = Avalonia.Media.TextAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = LocalizationService.IsArabic
                            ? "تعذر بدء SOCYVIA بأمان. تم حفظ تقرير فني محلي ولم يتم حذف بيانات البحث."
                            : "SOCYVIA could not start safely. A local diagnostic report was saved and no research data was deleted.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        TextAlignment = Avalonia.Media.TextAlignment.Center,
                        Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3C526E"))
                    },
                    close
                }
            }
        };
    }


    // =========================================================
    // SPLASH
    // =========================================================

    private void OnSplashCompleted(
        object? sender,
        EventArgs e)
    {
        SplashContent.SplashCompleted -=
            OnSplashCompleted;

        ShowLogin();
    }


    // =========================================================
    // LOGIN
    // =========================================================

    private void ShowLogin()
    {
        WindowState =
            WindowState.Normal;


        ApplicationWindowChrome.IsVisible = true;
        ApplicationWindowChrome.Attach(this, showMinimize: true, showMaximize: false);


        Width =
            900;


        Height =
            600;


        MinWidth =
            860;


        MinHeight =
            560;


        CanResize =
            false;


        var loginView =
            new LoginView
            {
                Opacity = 0
            };


        loginView.LoginSucceeded +=
            OnLoginSucceeded;


        MainContent.Content =
            loginView;

        Dispatcher.UIThread.Post(CenterCurrentWindow, DispatcherPriority.Loaded);


        _ =
            FadeInAsync(
                loginView);
    }

    private void CenterCurrentWindow()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        var pixelWidth = (int)Math.Round(Width * screen.Scaling);
        var pixelHeight = (int)Math.Round(Height * screen.Scaling);
        Position = new PixelPoint(
            screen.WorkingArea.X + Math.Max(0, (screen.WorkingArea.Width - pixelWidth) / 2),
            screen.WorkingArea.Y + Math.Max(0, (screen.WorkingArea.Height - pixelHeight) / 2));
    }


    // =========================================================
    // LOGIN SUCCESS
    // =========================================================

    private async void OnLoginSucceeded(
        object? sender,
        ResearcherProfile researcher)
    {
        if (sender is LoginView loginView)
        {
            loginView.LoginSucceeded -=
                OnLoginSucceeded;
        }


        try
        {
            await ResearcherRepository.EnsureExistsAsync(
                researcher);


            await TransitionToDashboardAsync(
                researcher);
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(
                exception,
                "Researcher login database transition");
            Console.WriteLine(
                $"SOCYVIA database error: {exception}");


            ShowLogin();
            await ShowLoginFailureAsync();
        }
    }


    private async Task ShowLoginFailureAsync()
    {
        var close = new Button
        {
            Content = LocalizationService.IsArabic ? "إغلاق" : "Close",
            MinWidth = 90,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        var dialog = new Window
        {
            Title = "SOCYVIA",
            Width = 440,
            Height = 180,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(28),
                Spacing = 18,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = LocalizationService.IsArabic
                            ? "تعذر فتح مساحة البحث. تم حفظ تقرير فني محلي ولم تتغير بياناتك."
                            : "The research workspace could not be opened. A local diagnostic report was saved and your data was not changed.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        TextAlignment = Avalonia.Media.TextAlignment.Center
                    },
                    close
                }
            }
        };
        WindowAppearanceService.ApplyAppIcon(dialog);
        WindowAppearanceService.ApplySocyviaDialogChrome(dialog);
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }


    // =========================================================
    // DASHBOARD
    // =========================================================

    private async Task TransitionToDashboardAsync(
        ResearcherProfile researcher)
    {
        if (MainContent.Content is Control currentView)
        {
            await FadeOutAsync(
                currentView);
        }


        CanResize =
            true;

        ApplicationWindowChrome.SetCapabilities(showMinimize: true, showMaximize: true);


        MinWidth =
            1100;


        MinHeight =
            650;


        WindowState =
            WindowState.Maximized;


        var dashboardView =
            new DashboardView(
                researcher)
            {
                Opacity = 0
            };


        dashboardView.LogoutRequested +=
            OnLogoutRequested;


        MainContent.Content =
            dashboardView;


        await FadeInAsync(
            dashboardView);
    }


    // =========================================================
    // LOGOUT
    // =========================================================

    private async void OnLogoutRequested(
        object? sender,
        EventArgs e)
    {
        if (sender is DashboardView dashboard)
        {
            dashboard.LogoutRequested -=
                OnLogoutRequested;
        }


        ResearcherService.ClearActiveResearcher();


        if (MainContent.Content is Control currentView)
        {
            await FadeOutAsync(
                currentView);
        }


        ShowLogin();
    }


    // =========================================================
    // FADE IN
    // =========================================================

    private static async Task FadeInAsync(
        Control control)
    {
        double opacity =
            0;


        control.Opacity =
            opacity;


        while (opacity < 1.0)
        {
            opacity += 0.06;


            if (opacity > 1.0)
            {
                opacity = 1.0;
            }


            control.Opacity =
                opacity;


            await Task.Delay(
                16);
        }


        control.Opacity =
            1.0;
    }


    // =========================================================
    // FADE OUT
    // =========================================================

    private static async Task FadeOutAsync(
        Control control)
    {
        double opacity =
            control.Opacity;


        while (opacity > 0)
        {
            opacity -= 0.08;


            if (opacity < 0)
            {
                opacity = 0;
            }


            control.Opacity =
                opacity;


            await Task.Delay(
                14);
        }


        control.Opacity =
            0;
    }
}
