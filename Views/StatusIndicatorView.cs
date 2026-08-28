using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

/// <summary>A compact, direction-aware readiness row that keeps its state and text together.</summary>
public sealed class StatusIndicatorView : UserControl
{
    public StatusIndicatorView(string message, string colorHex = "#177A5B")
    {
        var arabic = LocalizationService.IsArabic;
        var dot = new Border
        {
            Width = 7,
            Height = 7,
            CornerRadius = new Avalonia.CornerRadius(4),
            Background = new SolidColorBrush(Color.Parse(colorHex)),
            VerticalAlignment = VerticalAlignment.Center
        };
        var label = new TextBlock
        {
            Text = arabic ? UiTextService.Arabic(message) : message,
            FontFamily = new FontFamily(arabic
                ? "avares://SOCYVIA/Assets/Fonts#IBM Plex Sans Arabic"
                : "avares://SOCYVIA/Assets/Fonts#IBM Plex Sans"),
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.Parse("#43536A")),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = arabic ? TextAlignment.Right : TextAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        var row = new Grid
        {
            ColumnDefinitions = arabic ? new ColumnDefinitions("Auto,8,*") : new ColumnDefinitions("*,8,Auto"),
            FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetColumn(dot, arabic ? 0 : 2);
        Grid.SetColumn(label, arabic ? 2 : 0);
        row.Children.Add(dot);
        row.Children.Add(label);
        Content = new Border
        {
            Padding = new Avalonia.Thickness(10, 8),
            Background = new SolidColorBrush(Color.Parse("#F8FAFD")),
            BorderBrush = new SolidColorBrush(Color.Parse("#E3E9F1")),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(8),
            Child = row
        };
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }
}
