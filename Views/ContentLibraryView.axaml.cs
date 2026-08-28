using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SOCYVIA.Models;
using SOCYVIA.Repositories;
using SOCYVIA.Services;
using SOCYVIA.Services.ContentAcquisition;

namespace SOCYVIA.Views;

public partial class ContentLibraryView : UserControl
{
    private readonly ResearcherProfile? _researcher;
    private readonly string? _initialContentItemId;
    private readonly ContentAcquisitionService _acquisition = new();
    private ContentItem? _editingItem;
    private bool _isUnsavedAcquisition;
    private ManagedMediaAsset? _pendingMediaAsset;
    private System.Collections.Generic.IReadOnlyList<ContentItem> _items =
        Array.Empty<ContentItem>();

    public ContentLibraryView()
    {
        InitializeComponent();
        SetupEvents();
        ConfigureLanguage();
    }

    public ContentLibraryView(ResearcherProfile researcher, string? initialContentItemId = null) : this()
    {
        _researcher = researcher;
        _initialContentItemId = initialContentItemId;
        AttachedToVisualTree += async (_, _) =>
        {
            await ReloadAsync();
            await OpenInitialContentItemAsync();
        };
    }

    private async Task OpenInitialContentItemAsync()
    {
        if (string.IsNullOrWhiteSpace(_initialContentItemId)) return;
        var item = _items.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, _initialContentItemId, StringComparison.Ordinal));
        if (item is null) return;
        Edit(item, await EngagementObservationRepository.GetLatestAsync(item.Id));
        PublishedMediaUrlBox.Focus();
    }

    public async Task ReloadAsync()
    {
        if (_researcher is null) return;
        _items = (await ContentLibraryService.GetAsync(_researcher.Id, true))
            .Where(item => !item.IsDemo)
            .ToArray();
        ItemCountText.Text = _items.Count.ToString(CultureInfo.InvariantCulture);
        await RenderAsync();
    }

    private void SetupEvents()
    {
        ManualButton.Click += (_, _) => ClearEditor();
        UploadFileButton.Click += async (_, _) =>
        {
            ClearEditor();
            await BrowseMediaAsync();
        };
        ClearEditorButton.Click += (_, _) => ClearEditor();
        ImportButton.Click += async (_, _) => await ImportLegacyAsync();
        AcquireButton.Click += async (_, _) => await AcquireAsync();
        SaveButton.Click += async (_, _) => await SaveAsync();
        ObservationButton.Click += async (_, _) => await AddObservationAsync();
        BrowseMediaButton.Click += async (_, _) => await BrowseMediaAsync();
        SearchBox.TextChanged += async (_, _) => await RenderAsync();
        TypeFilter.SelectionChanged += async (_, _) => await RenderAsync();
    }

    private async Task RenderAsync()
    {
        ItemsPanel.Children.Clear();
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        var type = (TypeFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var filtered = _items.Where(item =>
            (type is "All" or null || item.ContentType == type) &&
            (query.Length == 0 ||
             item.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             item.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             item.Platform.Contains(query, StringComparison.CurrentCultureIgnoreCase)));

        foreach (var item in filtered)
        {
            var observation = await EngagementObservationRepository.GetLatestAsync(item.Id);
            ItemsPanel.Children.Add(CreateCard(item, observation));
        }

        if (ItemsPanel.Children.Count == 0)
        {
            ItemsPanel.Children.Add(new Border
            {
                Classes = { "emptyState" },
                Child = new TextBlock
                {
                    Text = Text(
                        "لا يوجد محتوى بعد. أضف مادة أصلية مرة واحدة ثم أعد استخدامها في تجاربك.",
                        "No content yet. Capture source material once, then reuse it across experiments."),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = IsArabic ? TextAlignment.Right : TextAlignment.Left
                }
            });
        }
    }

    private Control CreateCard(ContentItem item, EngagementObservation? observation)
    {
        var card = new Button
        {
            Classes = { "quiet" },
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Avalonia.Thickness(12),
            IsEnabled = true
        };
        var title = new TextBlock
        {
            Text = item.Title,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("#22344D"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var captured = observation?.CapturedAtUtc ?? item.CapturedAtUtc;
        var meta = new TextBlock
        {
            Text = $"{item.Id[..Math.Min(14, item.Id.Length)]}  ·  {item.ContentType}  ·  {item.Platform}",
            FontSize = 8,
            Foreground = Brush("#7C889A")
        };
        var observed = new TextBlock
        {
            Text = observation is null
                ? Text("لا توجد ملاحظة تفاعل", "No engagement observation")
                : $"{Text("مرصود عند الالتقاط", "Observed at capture")}: " +
                  $"{Text("إعجاب", "Likes")} {Format(observation.Likes)} · " +
                  $"{Text("تعليق", "Comments")} {Format(observation.Comments)} · " +
                  captured.ToLocalTime().ToString("g"),
            FontSize = 8,
            Foreground = Brush("#596A82"),
            TextWrapping = TextWrapping.Wrap
        };
        var details = new StackPanel { Spacing = 4, Children = { title, meta, observed } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("76,12,*") };
        grid.Children.Add(CreateThumbnail(item));
        Grid.SetColumn(details, 2);
        grid.Children.Add(details);
        if (item.IsDemo)
        {
            var badge = new Border
            {
                Padding = new Avalonia.Thickness(7, 3),
                Background = Brush("#EAF1FF"),
                CornerRadius = new Avalonia.CornerRadius(7),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = "DEMO · SYNTHETIC", FontSize = 6.5, FontWeight = FontWeight.SemiBold,
                    Foreground = Brush("#2563EB"), TextAlignment = TextAlignment.Center
                }
            };
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }
        card.Content = grid;
        card.Click += (_, _) => Edit(item, observation);
        return card;
    }

    private void Edit(ContentItem item, EngagementObservation? observation)
    {
        _editingItem = item;
        _pendingMediaAsset = null;
        _isUnsavedAcquisition = false;
        TitleBox.Text = item.Title;
        BodyBox.Text = item.BodyText;
        SelectCombo(ContentTypeBox, item.ContentType);
        PlatformBox.Text = item.Platform;
        AuthorBox.Text = item.AuthorName;
        SourceNameBox.Text = item.SourceName;
        PublishedAtBox.Text = item.PublishedAtUtc?.ToLocalTime().ToString("g");
        AcquiredAtBox.Text = item.CapturedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
        OriginalUrlBox.Text = item.OriginalUrl;
        MediaPathBox.Text = item.MediaPath;
        ThumbnailPathBox.Text = item.ThumbnailPath;
        PublishedMediaUrlBox.Text = item.PublishedMediaUrl;
        CategoryBox.Text = item.Category;
        TopicBox.Text = item.Topic;
        TagsBox.Text = item.Tags;
        NotesBox.Text = item.ResearcherNotes;
        LikesBox.Text = observation?.Likes?.ToString(CultureInfo.InvariantCulture);
        CommentsBox.Text = observation?.Comments?.ToString(CultureInfo.InvariantCulture);
        SharesBox.Text = observation?.Shares?.ToString(CultureInfo.InvariantCulture);
        SavesBox.Text = observation?.Saves?.ToString(CultureInfo.InvariantCulture);
        ViewsBox.Text = observation?.Views?.ToString(CultureInfo.InvariantCulture);
        SetEditorReadOnly(item.IsDemo);
        EditorStatusText.Text = item.IsDemo
            ? Text(
                "مادة تجريبية اصطناعية للعرض فقط. أعد ضبط تجربة SOCYVIA لاستعادتها",
                "Synthetic DEMO content is read-only. Reset the SOCYVIA Demo to restore it.")
            : Text(
                "تحرير البيانات الوصفية لا يغير ملاحظات التفاعل السابقة.",
                "Editing metadata does not change earlier engagement observations.");
    }

    private void ClearEditor()
    {
        _editingItem = null;
        _pendingMediaAsset = null;
        _isUnsavedAcquisition = false;
        SetEditorReadOnly(false);
        TitleBox.Text = string.Empty; BodyBox.Text = string.Empty;
        ContentTypeBox.SelectedIndex = 0; PlatformBox.Text = "Generic";
        AuthorBox.Text = string.Empty; SourceNameBox.Text = string.Empty;
        PublishedAtBox.Text = string.Empty; AcquiredAtBox.Text = string.Empty;
        OriginalUrlBox.Text = string.Empty;
        MediaPathBox.Text = string.Empty; ThumbnailPathBox.Text = string.Empty;
        PublishedMediaUrlBox.Text = string.Empty;
        CategoryBox.Text = string.Empty; TopicBox.Text = string.Empty;
        TagsBox.Text = string.Empty; NotesBox.Text = string.Empty; LikesBox.Text = string.Empty;
        CommentsBox.Text = string.Empty; SharesBox.Text = string.Empty;
        SavesBox.Text = string.Empty; ViewsBox.Text = string.Empty;
        EditorStatusText.Text = string.Empty;
    }

    private async Task SaveAsync()
    {
        if (_researcher is null) return;
        try
        {
            var isNew = _editingItem is null || _isUnsavedAcquisition;
            var item = _editingItem ?? new ContentItem
            {
                ResearcherId = _researcher.Id,
                CapturedAtUtc = DateTime.UtcNow,
                AcquisitionProvider = "Manual",
                AcquisitionStatus = "Manual"
            };
            item.Title = TitleBox.Text?.Trim() ?? string.Empty;
            item.BodyText = BodyBox.Text ?? string.Empty;
            item.ContentType = Selected(ContentTypeBox, "Text");
            item.Platform = string.IsNullOrWhiteSpace(PlatformBox.Text) ? "Generic" : PlatformBox.Text.Trim();
            item.AuthorName = NullIfWhiteSpace(AuthorBox.Text);
            item.SourceName = NullIfWhiteSpace(SourceNameBox.Text);
            item.PublishedAtUtc = ParseDate(PublishedAtBox.Text);
            item.OriginalUrl = NullIfWhiteSpace(OriginalUrlBox.Text);
            item.MediaPath = NullIfWhiteSpace(MediaPathBox.Text);
            item.ThumbnailPath = NullIfWhiteSpace(ThumbnailPathBox.Text);
            var publishedMediaUrl = NullIfWhiteSpace(PublishedMediaUrlBox.Text);
            Uri? validatedMediaUri = null;
            var requiresDirectMedia = item.ContentType is "Image" or "Video" or "Audio" or "Mixed";
            var validPublishedSource = publishedMediaUrl is null ||
                (requiresDirectMedia
                    ? PublishedMediaUrlValidator.TryValidateDirectMedia(publishedMediaUrl, out validatedMediaUri, out _)
                    : PublishedMediaUrlValidator.TryValidate(publishedMediaUrl, out validatedMediaUri, out _));
            if (!validPublishedSource)
            {
                var externalContent = PublishedMediaUrlValidator.TryValidate(
                    publishedMediaUrl, out var pageUri, out _) &&
                    PublishedMediaUrlValidator.IsExternalContentPage(pageUri!);
                EditorStatusText.Text = Text(
                    externalContent
                        ? "هذا رابط لمحتوى خارجي أو منصة اجتماعية. استخدم إضافة رابط خارجي بدلا من مصدر وسائط مباشر."
                        : "أدخل رابط HTTPS عاما وصالحا لمصدر الوسائط عند النشر. لا يمكن استخدام ملف محلي أو localhost.",
                    externalContent
                        ? "This is external or social content. Use Add External Link instead of a direct media source."
                        : "Enter a valid public HTTPS published media source. Local files and localhost cannot be used.");
                PublishedMediaUrlBox.Focus();
                return;
            }
            item.PublishedMediaUrl = validatedMediaUri?.AbsoluteUri;
            item.Category = NullIfWhiteSpace(CategoryBox.Text);
            item.Topic = NullIfWhiteSpace(TopicBox.Text);
            item.Tags = NullIfWhiteSpace(TagsBox.Text);
            item.ResearcherNotes = NullIfWhiteSpace(NotesBox.Text);
            if (isNew)
            {
                await ContentLibraryService.CreateAsync(item, BuildObservation(item.CapturedAtUtc));
            }
            else
            {
                await ContentLibraryService.UpdateAsync(item);
            }
            if (_pendingMediaAsset is not null)
            {
                await ManagedMediaService.PersistAsync(_pendingMediaAsset, item.Id);
                _pendingMediaAsset = null;
            }
            _editingItem = item;
            _isUnsavedAcquisition = false;
            EditorStatusText.Text = Text("تم حفظ المادة البحثية.", "Research content saved.");
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Save content library item");
            EditorStatusText.Text = Text(
                "تعذر حفظ المادة. تم تسجيل تقرير فني محلي ولم تتغير السجلات السابقة.",
                "The content item could not be saved. A local diagnostic report was recorded and earlier records were not changed.");
        }
    }

    private async Task AddObservationAsync()
    {
        if (_editingItem is null)
        {
            EditorStatusText.Text = Text("احفظ المادة أولا.", "Save the content item first.");
            return;
        }
        await ContentLibraryService.AddObservationAsync(
            _editingItem.Id, BuildObservation(DateTime.UtcNow));
        EditorStatusText.Text = Text(
            "تمت إضافة ملاحظة جديدة دون استبدال القيم السابقة.",
            "A new observation was appended without replacing earlier values.");
        await ReloadAsync();
    }

    private async Task AcquireAsync()
    {
        AcquisitionStatusText.Text = Text("جار فحص المصدر...", "Inspecting source...");
        var result = await _acquisition.AcquireAsync(UrlTextBox.Text ?? string.Empty);
        AcquisitionStatusText.Text = LocalizeAcquisition(result);
        if (result.Metadata is not { } metadata) return;
        ClearEditor();
        TitleBox.Text = metadata.Title ?? string.Empty;
        BodyBox.Text = metadata.BodyText ?? string.Empty;
        AuthorBox.Text = metadata.AuthorName ?? string.Empty;
        SourceNameBox.Text = metadata.SourceName ?? string.Empty;
        PublishedAtBox.Text = metadata.PublishedAtUtc?.ToLocalTime().ToString("g") ?? string.Empty;
        AcquiredAtBox.Text = result.AcquiredAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
        OriginalUrlBox.Text = metadata.OriginalUrl;
        PlatformBox.Text = metadata.Platform ?? "Web";
        SelectCombo(ContentTypeBox, metadata.ContentType ?? "Link");
        _editingItem = new ContentItem
        {
            ResearcherId = _researcher?.Id ?? string.Empty,
            CapturedAtUtc = result.AcquiredAtUtc,
            AcquisitionProvider = result.ProviderId,
            AcquisitionStatus = result.Status.ToString(),
            SourceName = metadata.SourceName,
            PublishedAtUtc = metadata.PublishedAtUtc,
            SourceMetadataJson = metadata.SourceMetadataJson
        };
        _isUnsavedAcquisition = true;
        EditorStatusText.Text = Text(
            "راجع الحقول المتاحة وأكمل البيانات غير المتاحة يدويا قبل الحفظ.",
            "Review available fields and complete unavailable data manually before saving.");
    }

    private async Task ImportLegacyAsync()
    {
        if (_researcher is null) return;
        await LegacyContentCompatibilityService.SynchronizeResearcherAsync(_researcher.Id);
        AcquisitionStatusText.Text = Text(
            "تم ربط محفزات الدراسات الحالية بالمكتبة دون حذف النسخ الأصلية.",
            "Existing study stimuli were linked into the library without removing originals.");
        await ReloadAsync();
    }

    private async Task BrowseMediaAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = Text("اختر ملف وسائط", "Choose media file")
        });
        if (files.Count == 0 || _researcher is null) return;
        try
        {
            BrowseMediaButton.IsEnabled = false;
            EditorStatusText.Text = Text("جار إنشاء نسخة بحثية محلية...", "Creating managed research copy...");
            _pendingMediaAsset = await ManagedMediaService.StageFileAsync(
                _researcher.Id,
                files[0].Path.LocalPath,
                Selected(ContentTypeBox, "File"));
            var managedPath = ManagedMediaService.ResolveAbsolutePath(_pendingMediaAsset);
            MediaPathBox.Text = managedPath;
            if (_pendingMediaAsset.MediaKind == "Image") ThumbnailPathBox.Text = managedPath;
            if (_editingItem is null)
            {
                _editingItem = new ContentItem
                {
                    Id = Guid.NewGuid().ToString(),
                    ResearcherId = _researcher.Id,
                    CapturedAtUtc = DateTime.UtcNow,
                    AcquisitionProvider = "LocalFile",
                    AcquisitionStatus = "ManagedCopy"
                };
                _isUnsavedAcquisition = true;
            }
            EditorStatusText.Text = Text(
                "تم تجهيز نسخة بحثية مدارة مع بصمة SHA-256. احفظ المادة لربطها بالسجل.",
                "Managed research copy prepared with a SHA-256 fingerprint. Save the content record to link it.");
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Import managed research media");
            EditorStatusText.Text = Text("تعذر إنشاء النسخة البحثية المحلية.", "The managed research copy could not be created.");
        }
        finally
        {
            BrowseMediaButton.IsEnabled = true;
        }
    }

    private Control CreateThumbnail(ContentItem item)
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
                    Width = 76, Height = 58, CornerRadius = new Avalonia.CornerRadius(9),
                    ClipToBounds = true, Background = Brush("#EEF2F6"),
                    Child = new Image { Source = new Bitmap(path), Stretch = Stretch.UniformToFill }
                };
            }
            catch (Exception exception)
            {
                ApplicationDiagnosticsService.LogException(exception, "Content Library thumbnail");
            }
        }
        return new Border
        {
            Width = 76, Height = 58, CornerRadius = new Avalonia.CornerRadius(9),
            Background = Brush("#EEF3F8"),
            Child = new TextBlock
            {
                Text = item.ContentType.ToUpperInvariant(), FontSize = 7,
                FontWeight = FontWeight.SemiBold, Foreground = Brush("#64748B"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
    }

    private EngagementObservation BuildObservation(DateTime capturedAt) => new()
    {
        Likes = ParseLong(LikesBox.Text), Comments = ParseLong(CommentsBox.Text),
        Shares = ParseLong(SharesBox.Text), Saves = ParseLong(SavesBox.Text),
        Views = ParseLong(ViewsBox.Text), CapturedAtUtc = capturedAt,
        ObservationSource = "Manual"
    };

    private void SetEditorReadOnly(bool readOnly)
    {
        foreach (var box in new[]
                 {
                     TitleBox, BodyBox, PlatformBox, AuthorBox, SourceNameBox,
                     PublishedAtBox, OriginalUrlBox, PublishedMediaUrlBox, CategoryBox, TopicBox,
                     TagsBox, NotesBox, LikesBox, CommentsBox, SharesBox,
                     SavesBox, ViewsBox
                 })
            box.IsReadOnly = readOnly;
        ContentTypeBox.IsEnabled = !readOnly;
        BrowseMediaButton.IsEnabled = !readOnly;
        SaveButton.IsEnabled = !readOnly;
        ObservationButton.IsEnabled = !readOnly;
    }

    private void ConfigureLanguage()
    {
        FlowDirection = IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        PageEyebrow.Text = Text("المحتوى", "CONTENT");
        PageTitle.Text = Text("المحتوى والوسائط", "Content & Media");
        PageSubtitle.Text = Text(
            "احفظ حقيقة المصدر ومصدره مرة واحدة، ثم أعد استخدامه في تصميمات تجريبية متعددة.",
            "Preserve source truth and provenance once, then reuse it across experimental designs.");
        AcquireTitle.Text = Text("إضافة محتوى", "Add content");
        ManualTitle.Text = Text("+ إضافة محتوى", "+ Add Content");
        ManualHint.Text = Text("أنشئ سجلا بحثيا قابلا لإعادة الاستخدام", "Create a reusable research record");
        UploadFileTitle.Text = Text("رفع ملف", "Upload File");
        UploadFileHint.Text = Text("نسخة محلية مدارة مع بصمة SHA-256", "Managed local copy with SHA-256 integrity");
        ImportTitle.Text = Text("استيراد قائمة حالية", "Import existing list");
        ImportHint.Text = Text("ربط محفزات الدراسات دون تغيير الأصل", "Link legacy study stimuli without changing originals");
        UrlTitle.Text = Text("استيراد من رابط", "Import from URL");
        AcquireButtonText.Text = Text("استيراد الرابط", "Import URL");
        SourceTruthText.Text = Text(
            "حقيقة المصدر ← نسخة بحثية ← عرض تجريبي. لا تستبدل SOCYVIA القيم المرصودة القديمة.",
            "Source truth → research copy → experimental presentation. SOCYVIA never replaces earlier observations.");
        EditorTitle.Text = Text("بيانات المادة", "Content record");
        TitleLabel.Text = Text("العنوان", "Title"); BodyLabel.Text = Text("النص أو الوصف", "Text or caption");
        TypeLabel.Text = Text("النوع", "Type"); PlatformLabel.Text = Text("المنصة أو المصدر", "Platform or source");
        AuthorLabel.Text = Text("المؤلف أو الحساب", "Author or account"); OriginalUrlLabel.Text = Text("الرابط الأصلي", "Original URL");
        SourceNameLabel.Text = Text("اسم المصدر", "Source name"); PublishedAtLabel.Text = Text("وقت النشر", "Published at");
        AcquiredAtLabel.Text = Text("وقت الحصول المسجل آليا", "System-recorded acquisition time");
        MediaLabel.Text = Text("ملف النسخة البحثية", "Local research copy"); BrowseMediaText.Text = Text("اختيار", "Browse");
        ThumbnailLabel.Text = Text("مسار الصورة المصغرة", "Thumbnail path");
        PublishedMediaUrlLabel.Text = Text("مصدر الوسائط عند النشر", "Published media source");
        PublishedMediaUrlHint.Text = Text(
            "يجب أن يكون الرابط متاحا للمشاركين دون تسجيل دخول.",
            "The URL must be accessible to participants without signing in.");
        CategoryLabel.Text = Text("الفئة", "Category"); TopicLabel.Text = Text("الموضوع", "Topic"); TagsLabel.Text = Text("الوسوم", "Tags");
        NotesLabel.Text = Text("ملاحظات الباحث", "Researcher notes");
        ObservationTitle.Text = Text("قيم التفاعل المرصودة", "Observed engagement values");
        ObservationHint.Text = Text(
            "تسجل كل إضافة مع وقت الالتقاط ولا تستبدل السجل السابق.",
            "Each entry is timestamped and appended; earlier observations remain intact.");
        SaveButtonText.Text = Text("حفظ المادة", "Save content");
        ObservationButtonText.Text = Text("إضافة ملاحظة", "Add observation");
        var alignment = IsArabic ? TextAlignment.Right : TextAlignment.Left;
        foreach (var box in new[] { TitleBox, BodyBox, PlatformBox, AuthorBox, SourceNameBox, PublishedAtBox, OriginalUrlBox, MediaPathBox, ThumbnailPathBox, PublishedMediaUrlBox, CategoryBox, TopicBox, TagsBox, NotesBox })
            box.TextAlignment = alignment;
    }

    private string LocalizeAcquisition(ContentAcquisitionResult result) => result.Status switch
    {
        ContentAcquisitionStatus.AuthenticationRequired => Text(
            "يتطلب هذا المصدر تكاملا مع مزود معتمد. تم حفظ الرابط، ويمكن إكمال البيانات يدويا.",
            result.CanonicalMessage),
        ContentAcquisitionStatus.Error => Text("تعذر جلب البيانات تلقائيا. استخدم الإدخال اليدوي.", result.CanonicalMessage),
        ContentAcquisitionStatus.Unsupported => Text("لا يدعم SOCYVIA هذا الرابط تلقائيا. استخدم الإدخال اليدوي.", result.CanonicalMessage),
        _ => Text("تم جلب البيانات العامة المتاحة. راجع الحقول قبل الحفظ.", result.CanonicalMessage)
    };

    private bool IsArabic => LocalizationService.IsArabic;
    private string Text(string arabic, string english) =>
        IsArabic ? UiTextService.Arabic(arabic) : english;
    private static string Selected(ComboBox box, string fallback) =>
        (box.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? fallback;
    private static void SelectCombo(ComboBox box, string value)
    {
        foreach (var candidate in box.Items.OfType<ComboBoxItem>())
            if (string.Equals(candidate.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            { box.SelectedItem = candidate; return; }
    }
    private static long? ParseLong(string? text) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static DateTime? ParseDate(string? text) =>
        DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var value)
            ? value.ToUniversalTime()
            : null;
    private static string Format(long? value) => value?.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
