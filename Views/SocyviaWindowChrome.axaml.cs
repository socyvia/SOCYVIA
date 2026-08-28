using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

public partial class SocyviaWindowChrome : UserControl
{
    private Window? _window;
    private bool _showMinimize = true;
    private bool _showMaximize = true;
    private bool _languageSubscribed;

    public SocyviaWindowChrome()
    {
        InitializeComponent();
        MinimizeButton.Click += (_, _) =>
        {
            if (_window is not null) _window.WindowState = WindowState.Minimized;
        };
        MaximizeRestoreButton.Click += (_, _) => ToggleMaximizeRestore();
        CloseButton.Click += (_, _) => _window?.Close();
        ChromeSurface.PointerPressed += OnChromePointerPressed;
    }

    public bool Attach(
        Window window,
        bool showMinimize = true,
        bool showMaximize = true)
    {
        if (!ReferenceEquals(_window, window))
        {
            if (_window is not null) _window.PropertyChanged -= OnWindowPropertyChanged;
            _window = window;
            _window.PropertyChanged += OnWindowPropertyChanged;
        }

        _showMinimize = showMinimize;
        _showMaximize = showMaximize;

        if (!_languageSubscribed)
        {
            LocalizationService.LanguageChanged += OnLanguageChanged;
            _languageSubscribed = true;
        }

        var customChromeAvailable = ConfigurePlatformChrome(window);
        IsVisible = customChromeAvailable;
        UpdateControls();
        return customChromeAvailable;
    }

    public void SetCapabilities(bool showMinimize, bool showMaximize)
    {
        _showMinimize = showMinimize;
        _showMaximize = showMaximize;
        UpdateControls();
    }

    private bool ConfigurePlatformChrome(Window window)
    {
        try
        {
            window.ExtendClientAreaToDecorationsHint = true;
            window.ExtendClientAreaTitleBarHeightHint = 32;

            if (OperatingSystem.IsMacOS())
            {
                // Keep native traffic-light semantics while carrying the same SOCYVIA material.
                window.WindowDecorations = WindowDecorations.Full;
                WindowControlsPanel.IsVisible = false;
                IdentityPanel.Margin = new Thickness(78, 0, 0, 0);
            }
            else
            {
                // BorderOnly preserves native resize edges; the title area and controls are app drawn.
                window.WindowDecorations = WindowDecorations.BorderOnly;
                WindowControlsPanel.IsVisible = true;
                IdentityPanel.Margin = new Thickness(10, 0, 0, 0);
            }

            return true;
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "SOCYVIA window chrome fallback");
            window.ExtendClientAreaToDecorationsHint = false;
            window.WindowDecorations = WindowDecorations.Full;
            return false;
        }
    }

    private void ToggleMaximizeRestore()
    {
        if (_window is null || !_window.CanResize) return;
        _window.WindowState = _window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnChromePointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (eventArgs.ClickCount != 2 ||
            !eventArgs.GetCurrentPoint(ChromeSurface).Properties.IsLeftButtonPressed ||
            _window is null ||
            !_window.CanResize)
        {
            return;
        }

        ToggleMaximizeRestore();
        eventArgs.Handled = true;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.Property == Window.WindowStateProperty ||
            eventArgs.Property == Window.CanResizeProperty)
        {
            UpdateControls();
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs eventArgs) => UpdateControls();

    private void UpdateControls()
    {
        if (_window is null) return;

        MinimizeButton.IsVisible = _showMinimize && !OperatingSystem.IsMacOS();
        MaximizeRestoreButton.IsVisible =
            _showMaximize && _window.CanResize && !OperatingSystem.IsMacOS();

        var maximized = _window.WindowState == WindowState.Maximized;
        MaximizePath.IsVisible = !maximized;
        RestorePath.IsVisible = maximized;

        var arabic = LocalizationService.IsArabic;
        var minimize = arabic ? "تصغير" : "Minimize";
        var maximize = arabic ? "تكبير" : "Maximize";
        var restore = arabic ? "استعادة" : "Restore";
        var close = arabic ? "إغلاق" : "Close";

        AutomationProperties.SetName(MinimizeButton, minimize);
        AutomationProperties.SetName(MaximizeRestoreButton, maximized ? restore : maximize);
        AutomationProperties.SetName(CloseButton, close);
        ToolTip.SetTip(MinimizeButton, minimize);
        ToolTip.SetTip(MaximizeRestoreButton, maximized ? restore : maximize);
        ToolTip.SetTip(CloseButton, close);
    }
}
