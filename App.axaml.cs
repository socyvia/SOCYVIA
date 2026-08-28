using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SOCYVIA.Data;
using SOCYVIA.Services;
using SOCYVIA.Views;

namespace SOCYVIA;

public partial class App : Application
{
    private bool _dispatcherDiagnosticsAttached;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        if (!_dispatcherDiagnosticsAttached)
        {
            Dispatcher.UIThread.UnhandledException += (_, eventArgs) =>
            {
                ApplicationDiagnosticsService.LogException(
                    eventArgs.Exception,
                    "Avalonia UI dispatcher");
                if (eventArgs.Exception is not (OutOfMemoryException or AccessViolationException))
                {
                    eventArgs.Handled = true;
                    ShowRecoverableError();
                }
            };
            _dispatcherDiagnosticsAttached = true;
        }
    }


    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            StorageService.Initialize();
            LocalizationService.Initialize();
            if (ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }
        }
        catch (System.Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Application startup");
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var close = new Button
                {
                    Content = "Close",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    MinWidth = 100
                };
                var message = new TextBlock
                {
                    Text = "SOCYVIA could not start safely. A local diagnostic log was created. Your research data was not reset or deleted.",
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    Foreground = new SolidColorBrush(Color.Parse("#24354E"))
                };
                var window = new Window
                {
                    Title = "SOCYVIA",
                    Width = 520,
                    Height = 230,
                    CanResize = false,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(34),
                        Spacing = 22,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children = { message, close }
                    }
                };
                WindowAppearanceService.ApplyAppIcon(window);
                WindowAppearanceService.ApplySocyviaDialogChrome(window);
                close.Click += (_, _) => window.Close();
                desktop.MainWindow = window;
            }
        }


        base.OnFrameworkInitializationCompleted();
    }


    private void ShowRecoverableError()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        var close = new Button { Content = "OK", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Center };
        var dialog = new Window
        {
            Title = "SOCYVIA",
            Width = 460,
            Height = 190,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(30),
                Spacing = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = LocalizationService.IsArabic
                            ? "حدث خطأ غير متوقع. تم حفظ تقرير فني محلي ولم يتم حذف بياناتك."
                            : "An unexpected error occurred. A local diagnostic report was saved and your data was not deleted.",
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center
                    },
                    close
                }
            }
        };
        WindowAppearanceService.ApplyAppIcon(dialog);
        WindowAppearanceService.ApplySocyviaDialogChrome(dialog);
        close.Click += (_, _) => dialog.Close();
        if (desktop.MainWindow is { } owner)
            dialog.Show(owner);
        else
            dialog.Show();
    }
}
