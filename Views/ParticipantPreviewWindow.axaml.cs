using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SOCYVIA.Models;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

public partial class ParticipantPreviewWindow : Window
{
    public ParticipantPreviewWindow()
    {
        InitializeComponent();
    }

    public ParticipantPreviewWindow(ParticipantPreviewContext preview) : this()
    {
        WindowAppearanceService.ApplyAppIcon(this);
        ExperimentFeedTitle.Text = "SOCYVIA Experiment Feed";
        Title = LocalizationService.IsArabic
            ? "SOCYVIA Experiment Feed — بيئة التجربة للمشارك"
            : "SOCYVIA Experiment Feed — Participant Experiment Environment";
        ExperimentFeedSubtitle.Text = LocalizationService.IsArabic
            ? "بيئة التجربة للمشارك"
            : "Participant Experiment Environment";
        PreviewLabel.Text = LocalizationService.IsArabic
            ? "معاينة الباحث • لا يتم تسجيل بيانات بحثية"
            : "Researcher Preview • No research data recorded";
        ExperimentFeedSubtitle.FlowDirection = LocalizationService.IsArabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        PreviewLabel.FlowDirection = LocalizationService.IsArabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        CloseButton.Content = LocalizationService.IsArabic ? "إغلاق المعاينة" : "Close Preview";
        CloseButton.Click += (_, _) => Close();
        foreach (var post in preview.Posts)
            FeedPanel.Children.Add(ParticipantFeedCardFactory.Create(
                post,
                CreatePreviewOptions(post)));
        if (preview.Posts.Count == 0)
            FeedPanel.Children.Add(CreateEmptyState());
    }

    private ParticipantFeedCardOptions CreatePreviewOptions(RuntimePostPresentation post) => new()
    {
        IsPreview = true,
        OpenRequestedAsync = OpenFocusedAsync,
        CommentRequestedAsync = OpenFocusedAsync
    };

    private Task OpenFocusedAsync(RuntimePostPresentation post)
    {
        FocusedHost.Content = ParticipantFeedCardFactory.CreateFocused(
            post,
            CreatePreviewOptions(post),
            CloseFocusedAsync);
        FeedScrollViewer.IsVisible = false;
        FocusedPanel.IsVisible = true;
        return Task.CompletedTask;
    }

    private Task CloseFocusedAsync()
    {
        FocusedHost.Content = null;
        FocusedPanel.IsVisible = false;
        FeedScrollViewer.IsVisible = true;
        return Task.CompletedTask;
    }

    private static Control CreateEmptyState() => new Border
    {
        MinHeight = 240,
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.Parse("#D9E0E9")),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Child = new TextBlock
        {
            Text = LocalizationService.IsArabic
                ? "لا توجد مواد معينة لهذه المجموعة والشرط. أضف محتوى من منشئ التجربة قبل المعاينة"
                : "No content is assigned to this group and condition. Add library content in Experiment Builder before previewing.",
            MaxWidth = 420,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#65748A")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }
    };
}
