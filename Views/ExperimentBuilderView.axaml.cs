using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SOCYVIA.Models;
using SOCYVIA.Repositories;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

public partial class ExperimentBuilderView : UserControl
{
    private readonly Study? _study;
    private List<StudyGroup> _groups = new();
    private List<ExperimentalCondition> _conditions = new();
    private IReadOnlyList<ContentItem> _library = Array.Empty<ContentItem>();
    private ExperimentalFeed? _feed;
    private bool _loading;
    private bool IsGuidedDemo => DemoAccessPolicy.IsReadOnlyStudy(_study);

    public ExperimentBuilderView()
    {
        InitializeComponent(); SetupEvents(); ConfigureLanguage();
    }

    public ExperimentBuilderView(Study study) : this()
    {
        _study = study;
        if (DemoAccessPolicy.IsDemoStudy(study))
        {
            PageEyebrow.Text = Text("تجربة SOCYVIA · بيانات اصطناعية", "SOCYVIA DEMO · SYNTHETIC DATA");
            PageSubtitle.Text = Text(
                "استكشف ثلاث معالجات للمواد نفسها في عرض للقراءة والمعاينة فقط",
                "Explore three presentations of the same source content in a read-only guided experience");
        }
        AttachedToVisualTree += async (_, _) => await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        if (_study is null) return;
        _loading = true;
        try
        {
            _groups = await GroupRepository.GetByStudyAsync(_study.Id);
            _conditions = await ExperimentalConditionRepository.GetByStudyAsync(_study.Id);
            _library = (await ContentLibraryService.GetAsync(_study.ResearcherId, true))
                .Where(item => !item.IsDemo)
                .ToArray();
            PopulateSelectors();
            await LoadScopeAsync();
        }
        finally { _loading = false; }
    }

    private void SetupEvents()
    {
        GroupBox.SelectionChanged += async (_, _) => { if (!_loading) { PopulateConditions(); await LoadScopeAsync(); } };
        ConditionBox.SelectionChanged += async (_, _) => { if (!_loading) await LoadScopeAsync(); };
        SearchBox.TextChanged += async (_, _) => await RenderLibraryAsync();
        TypeFilter.SelectionChanged += async (_, _) => await RenderLibraryAsync();
        PreviewButton.Click += async (_, _) => await PreviewAsync();
    }

    private void PopulateSelectors()
    {
        var selectedGroup = SelectedGroup?.Id;
        GroupBox.Items.Clear();
        foreach (var group in _groups.Where(item => item.IsActive))
            GroupBox.Items.Add(new ComboBoxItem { Content = group.Name, Tag = group });
        GroupBox.SelectedItem = GroupBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => ((StudyGroup)item.Tag!).Id == selectedGroup) ?? GroupBox.Items.FirstOrDefault();
        PopulateConditions();
    }

    private void PopulateConditions()
    {
        var group = SelectedGroup;
        var selected = SelectedCondition?.Id;
        ConditionBox.Items.Clear();
        if (group is null) return;
        foreach (var condition in _conditions.Where(item => item.IsActive && (item.GroupId is null || item.GroupId == group.Id)))
            ConditionBox.Items.Add(new ComboBoxItem { Content = condition.Name, Tag = condition });
        ConditionBox.SelectedItem = ConditionBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => ((ExperimentalCondition)item.Tag!).Id == selected) ?? ConditionBox.Items.FirstOrDefault();
    }

    private async Task LoadScopeAsync()
    {
        var group = SelectedGroup; var condition = SelectedCondition;
        if (_study is null || group is null || condition is null)
        {
            _feed = null; ScopeStatusText.Text = Text("اختر مجموعة وشرطا", "Select group and condition");
            await RenderLibraryAsync(); FeedPanel.Children.Clear(); return;
        }
        _feed = await ExperimentalFeedRepository.GetForScopeAsync(_study.Id, group.Id, condition.Id);
        var manipulation = ConditionManipulationService.Deserialize(condition.ManipulationJson);
        ManipulationSummaryText.Text = ConditionPresentationTextService.EngagementMode(manipulation) + " • " +
            Text("ترتيب المحتوى: ", "Content order: ") + manipulation.ContentOrderMode;
        ScopeStatusText.Text = _feed is null ? Text("تكوين جديد", "New configuration") : Text("تكوين محفوظ", "Saved configuration");
        await RenderLibraryAsync(); await RenderFeedAsync();
    }

    private async Task RenderLibraryAsync()
    {
        LibraryPanel.Children.Clear();
        var assigned = _feed is null
            ? new HashSet<string>()
            : (await ExperimentalFeedRepository.GetItemsAsync(_feed.Id)).Select(item => item.ContentItemId).ToHashSet();
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        var type = (TypeFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        foreach (var item in _library.Where(item => item.IsActive && !assigned.Contains(item.Id) &&
                     (type is "All" or null || item.ContentType == type) &&
                     (query.Length == 0 || item.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) || item.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Platform.Contains(query, StringComparison.CurrentCultureIgnoreCase))))
            LibraryPanel.Children.Add(ItemRow(item, !IsGuidedDemo));
        if (LibraryPanel.Children.Count == 0)
            LibraryPanel.Children.Add(Empty(Text("لا توجد مواد متاحة لهذا المرشح.", "No content is available for this filter.")));
    }

    private async Task RenderFeedAsync()
    {
        FeedPanel.Children.Clear();
        if (_feed is null) { FeedPanel.Children.Add(Empty(Text("أضف أول مادة لإنشاء الخلاصة التجريبية.", "Add the first item to create this experimental feed."))); return; }
        var items = await ExperimentalFeedRepository.GetItemsAsync(_feed.Id);
        foreach (var feedItem in items)
        {
            var content = await ContentItemRepository.GetByIdAsync(feedItem.ContentItemId);
            if (content is null) continue;
            var observation = feedItem.EngagementObservationId is null
                ? null
                : await EngagementObservationRepository.GetByIdAsync(feedItem.EngagementObservationId);
            FeedPanel.Children.Add(FeedRow(feedItem, content, observation));
        }
        if (FeedPanel.Children.Count == 0) FeedPanel.Children.Add(Empty(Text("الخلاصة فارغة.", "The feed is empty.")));
    }

    private Control ItemRow(ContentItem item, bool canAdd)
    {
        var button = new Button { Classes = { "quiet" }, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Avalonia.Thickness(10) };
        var details = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = item.Title, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = $"{item.Id[..Math.Min(14, item.Id.Length)]} · {item.ContentType} · {item.Platform}", FontSize = 8, Foreground = Brush("#79869A") }
            }
        };
        button.Content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("58,9,*,Auto"),
            Children =
            {
                Thumbnail(item),
                AtColumn(details, 2),
                new TextBlock { Text = canAdd ? "+" : "", FontSize = 16, Foreground = Brush("#2563EB"), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, [Grid.ColumnProperty] = 3 }
            }
        };
        button.IsEnabled = canAdd;
        if (canAdd)
            button.Click += async (_, _) => await AddAsync(item);
        return button;
    }

    private Control FeedRow(
        ExperimentalFeedItem feedItem,
        ContentItem content,
        EngagementObservation? observation)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), ColumnSpacing = 7 };
        row.Children.Add(new Border { Classes = { "badge" }, Child = new TextBlock { Text = (feedItem.SortOrder + 1).ToString(), TextAlignment = TextAlignment.Center } });
        row.Children.Add(new StackPanel
        {
            Spacing = 2,
            [Grid.ColumnProperty] = 1,
            Children =
            {
                new TextBlock { Text = content.Title, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock
                {
                    Text = observation is null
                        ? $"{content.ContentType} · {content.Platform}"
                        : $"{content.ContentType} · {content.Platform} · {Text("التقاط", "capture")} {observation.CapturedAtUtc.ToLocalTime():g}",
                    FontSize = 8,
                    Foreground = Brush("#79869A"),
                    TextWrapping = TextWrapping.Wrap
                }
            }
        });
        var up = SmallIcon("M4,10 L8,6 L12,10 M8,6 L8,14", Text("نقل إلى أعلى", "Move up"));
        var down = SmallIcon("M4,10 L8,14 L12,10 M8,4 L8,14", Text("نقل إلى أسفل", "Move down"));
        var remove = SmallIcon("M4,4 L12,12 M12,4 L4,12", Text("إزالة", "Remove"));
        up.IsVisible = !IsGuidedDemo;
        down.IsVisible = !IsGuidedDemo;
        remove.IsVisible = !IsGuidedDemo;
        Grid.SetColumn(up, 2); Grid.SetColumn(down, 3); Grid.SetColumn(remove, 4);
        up.Click += async (_, _) => await MoveAsync(feedItem, -1);
        down.Click += async (_, _) => await MoveAsync(feedItem, 1);
        remove.Click += async (_, _) => await RemoveAsync(feedItem);
        row.Children.Add(up); row.Children.Add(down); row.Children.Add(remove);
        return new Border { Classes = { "dataRow" }, Padding = new Avalonia.Thickness(10), Child = row };
    }

    private async Task AddAsync(ContentItem item)
    {
        if (IsGuidedDemo) return;
        if (_study is null || SelectedGroup is not { } group || SelectedCondition is not { } condition) return;
        _feed ??= await ExperimentFeedService.GetOrCreateAsync(_study, group, condition);
        await ExperimentFeedService.AddContentAsync(_feed, item);
        await LoadScopeAsync();
    }

    private async Task RemoveAsync(ExperimentalFeedItem item)
    {
        if (IsGuidedDemo) return;
        await ExperimentFeedService.RemoveContentAsync(item.Id); await LoadScopeAsync();
    }
    private async Task MoveAsync(ExperimentalFeedItem item, int direction)
    {
        if (IsGuidedDemo) return;
        if (_feed is null) return; await ExperimentFeedService.MoveAsync(_feed.Id, item.Id, direction); await LoadScopeAsync();
    }
    private async Task PreviewAsync()
    {
        if (_study is null || SelectedGroup is not { } group || SelectedCondition is not { } condition)
        {
            ScopeStatusText.Text = Text("اختر مجموعة وشرطا لمعاينة التجربة الحالية.", "Select a group and condition to preview the current study.");
            return;
        }

        try
        {
            PreviewButton.IsEnabled = false;
            ScopeStatusText.Text = Text("جار فتح معاينة المشارك", "Opening participant preview");
            await BrowserParticipantPreviewService.OpenAsync(_study, group, condition);
            ScopeStatusText.Text = Text(
                "تم فتح العرض التجريبي في المستعرض الافتراضي",
                "A safe preview of the current study opened in your browser. No research data are recorded.");
        }
        catch (BrowserParticipantPreviewException exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Participant preview");
            ScopeStatusText.Text = exception.Failure switch
            {
                BrowserParticipantPreviewFailure.LocalMediaUnavailable => Text("تعذر العثور على أحد ملفات الوسائط المحلية.", "A local media file could not be found."),
                BrowserParticipantPreviewFailure.AssetsUnavailable => Text("تعذر العثور على ملفات واجهة المعاينة.", "The preview interface files could not be found."),
                BrowserParticipantPreviewFailure.LocalHostUnavailable => Text("تعذر تشغيل خادم المعاينة المحلي.", "The local preview server could not be started."),
                BrowserParticipantPreviewFailure.BrowserLaunchUnavailable => Text("تعذر فتح المتصفح الافتراضي.", "The default browser could not be opened."),
                _ => Text("تعذر تجهيز معاينة الدراسة الحالية.", "The current study could not be prepared for preview.")
            };
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Participant preview");
            ScopeStatusText.Text = Text("تعذر فتح المتصفح الافتراضي.", "The default browser could not be opened.");
        }
        finally
        {
            PreviewButton.IsEnabled = true;
        }

    }

    private StudyGroup? SelectedGroup => (GroupBox.SelectedItem as ComboBoxItem)?.Tag as StudyGroup;
    private ExperimentalCondition? SelectedCondition => (ConditionBox.SelectedItem as ComboBoxItem)?.Tag as ExperimentalCondition;
    private static Button SmallIcon(string data, string accessibleName)
    {
        var button = new Button
        {
            Content = new Path
            {
                Data = StreamGeometry.Parse(data),
                Stroke = Brush("#52647C"),
                StrokeThickness = 1.7,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform
            },
            Classes = { "quiet", "iconAction" },
            Width = 30,
            Height = 30,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        ToolTip.SetTip(button, accessibleName);
        AutomationProperties.SetName(button, accessibleName);
        return button;
    }
    private static Control Empty(string text) => new Border { Classes = { "emptyState" }, Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap } };
    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));

    private static Control Thumbnail(ContentItem item)
    {
        var path = !string.IsNullOrWhiteSpace(item.ThumbnailPath)
            ? item.ThumbnailPath
            : item.MediaPath;
        if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
        {
            try
            {
                return new Border
                {
                    Width = 58, Height = 44,
                    CornerRadius = new Avalonia.CornerRadius(7),
                    ClipToBounds = true,
                    Child = new Image { Source = new Bitmap(path), Stretch = Stretch.UniformToFill }
                };
            }
            catch (Exception exception)
            {
                ApplicationDiagnosticsService.LogException(exception, "Experiment Builder thumbnail");
            }
        }
        return new Border
        {
            Width = 58, Height = 44,
            Background = Brush("#EEF3F8"),
            CornerRadius = new Avalonia.CornerRadius(7),
            Child = new TextBlock
            {
                Text = item.ContentType.ToUpperInvariant(), FontSize = 6.5,
                Foreground = Brush("#64748B"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
    }

    private static T AtColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private void ConfigureLanguage()
    {
        FlowDirection = LocalizationService.IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        PageEyebrow.Text = Text("التجربة", "EXPERIMENT"); PageTitle.Text = Text("منشئ التجربة", "Experiment Builder");
        PageSubtitle.Text = Text("اختر محتوى المكتبة وحدد ما يظهر لكل مجموعة وشرط دون تكرار حقيقة المصدر.", "Select library content and define what each group and condition receives without duplicating source truth.");
        PreviewButtonText.Text = Text("معاينة كمشارك", "Preview as participant"); GroupLabel.Text = Text("المجموعة", "Group");
        ConditionLabel.Text = Text("الشرط", "Condition"); LibraryTitle.Text = Text("مكتبة المحتوى", "Content Library");
        FeedTitle.Text = Text("الخلاصة التجريبية", "Experimental feed"); FeedHint.Text = Text("الترتيب هنا مخصص للعرض التجريبي ولا يغير المحتوى الأصلي.", "This order belongs to experimental presentation and does not change original content.");
        SearchBox.PlaceholderText = Text("بحث بالعنوان أو المعرف أو المنصة", "Search title, ID, or platform");
        var typeLabels = new Dictionary<string, string>
        {
            ["All"] = Text("الكل", "All"),
            ["Text"] = Text("نص", "Text"),
            ["Image"] = Text("صورة", "Image"),
            ["Video"] = Text("فيديو", "Video"),
            ["Audio"] = Text("صوت", "Audio"),
            ["Link"] = Text("رابط", "Link"),
            ["Mixed"] = Text("مختلط", "Mixed")
        };
        foreach (var option in TypeFilter.Items.OfType<ComboBoxItem>())
            option.Content = typeLabels.TryGetValue(option.Tag?.ToString() ?? string.Empty, out var label) ? label : option.Tag;
    }
    private string Text(string ar, string en) => UiTextService.Localized(ar, en);
}
