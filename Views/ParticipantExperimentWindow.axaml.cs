using System;
using Avalonia.Controls;
using Avalonia;
using SOCYVIA.Models;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

public partial class ParticipantExperimentWindow : Window
{
    private readonly ParticipantExperimentView _experimentView;
    private bool _allowClose;
    private bool _closeInProgress;

    public ParticipantExperimentWindow()
        : this(string.Empty, 0)
    {
    }

    public ParticipantExperimentWindow(string sessionId, int displayIndex = 0)
    {
        InitializeComponent();
        WindowAppearanceService.ApplyAppIcon(this);
        Title = LocalizationService.IsArabic
            ? "SOCYVIA · جلسة بحثية"
            : "SOCYVIA · Research session";
        _experimentView = new ParticipantExperimentView(sessionId);
        _experimentView.ExperimentFinished += OnExperimentFinished;
        Content = _experimentView;
        Closing += OnClosing;
        Opened += (_, _) => PositionOnDisplay(displayIndex);
    }

    private void PositionOnDisplay(int displayIndex)
    {
        var screens = Screens?.All;
        if (screens is null || screens.Count == 0) return;
        var screen = screens[Math.Clamp(displayIndex, 0, screens.Count - 1)];
        var area = screen.WorkingArea;
        var width = (int)Math.Round(Width * screen.Scaling);
        var height = (int)Math.Round(Height * screen.Scaling);
        Position = new PixelPoint(
            area.X + Math.Max(0, (area.Width - width) / 2),
            area.Y + Math.Max(0, (area.Height - height) / 2));
    }

    private void OnExperimentFinished(ParticipantSessionSummary summary)
    {
        _allowClose = true;
        Close(summary);
    }

    private async void OnClosing(
        object? sender,
        WindowClosingEventArgs eventArgs)
    {
        if (_allowClose || _closeInProgress)
        {
            return;
        }

        if (!_experimentView.IsRunningOrPaused)
        {
            _allowClose = true;
            return;
        }

        eventArgs.Cancel = true;
        _closeInProgress = true;
        try
        {
            var summary = await _experimentView.InterruptAsync();
            _allowClose = true;
            Close(summary);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Participant session interruption error: {exception}");
            _allowClose = true;
            Close(null);
        }
    }
}
