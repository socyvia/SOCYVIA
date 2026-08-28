using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SOCYVIA.Models;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

public partial class StimulusPostImportWindow : Window
{
    private Study? _study;


    private IReadOnlyList<StudyGroup> _groups =
        Array.Empty<StudyGroup>();


    private ImportPreview? _preview;


    private bool _isImporting;


    private readonly FontFamily _englishFont =
        new(
            "avares://SOCYVIA/Assets/Fonts#IBM Plex Sans");


    private readonly FontFamily _arabicFont =
        new(
            "avares://SOCYVIA/Assets/Fonts#IBM Plex Sans Arabic");


    // =========================================================
    // EMPTY CONSTRUCTOR
    // =========================================================

    public StimulusPostImportWindow()
    {
        InitializeComponent();


        SetupEvents();


        ConfigureLanguage();


        WindowAppearanceService.ApplyAppIcon(
            this);
    }


    // =========================================================
    // STUDY CONSTRUCTOR
    // =========================================================

    public StimulusPostImportWindow(
        Study study,
        IReadOnlyList<StudyGroup> groups)
        : this()
    {
        _study =
            study;


        _groups =
            groups;


        ConfigureStudy();
    }


    // =========================================================
    // EVENTS
    // =========================================================

    private void SetupEvents()
    {
        CloseTopButton.Click +=
            (_, _) =>
            {
                Close(
                    false);
            };


        CancelButton.Click +=
            (_, _) =>
            {
                Close(
                    false);
            };


        DownloadTemplateButton.Click +=
            async (_, _) =>
            {
                await DownloadTemplateAsync();
            };


        SelectFileButton.Click +=
            async (_, _) =>
            {
                await SelectFileAsync();
            };


        ImportButton.Click +=
            async (_, _) =>
            {
                await ImportAsync();
            };
    }


    // =========================================================
    // STUDY
    // =========================================================

    private void ConfigureStudy()
    {
        if (_study is null)
        {
            return;
        }


        ImportSubtitle.Text =
            IsArabic()
                ? $"استيراد منشورات إلى دراسة: {_study.Title}"
                : $"Import posts into: {_study.Title}";
    }


    // =========================================================
    // DOWNLOAD TEMPLATE
    // =========================================================

    private async Task DownloadTemplateAsync()
    {
        try
        {
            if (!StorageProvider.CanSave)
            {
                ShowGeneralError(
                    IsArabic()
                        ? "تعذر فتح نافذة حفظ الملف على هذا الجهاز."
                        : "The save dialog is not available on this device.");

                return;
            }


            var file =
                await StorageProvider
                    .SaveFilePickerAsync(
                        new FilePickerSaveOptions
                        {
                            Title =
                                IsArabic()
                                    ? "حفظ قالب SOCYVIA"
                                    : "Save SOCYVIA template",

                            SuggestedFileName =
                                "SOCYVIA_Posts_Template.csv",

                            DefaultExtension =
                                "csv",

                            ShowOverwritePrompt =
                                true,

                            FileTypeChoices =
                                new[]
                                {
                                    new FilePickerFileType(
                                        "CSV")
                                    {
                                        Patterns =
                                            new[]
                                            {
                                                "*.csv"
                                            },

                                        MimeTypes =
                                            new[]
                                            {
                                                "text/csv"
                                            }
                                    }
                                }
                        });


            if (file is null)
            {
                return;
            }


            var content =
                StimulusPostImportService
                    .CreateTemplateCsv();


            await using var stream =
                await file
                    .OpenWriteAsync();


            stream.SetLength(
                0);


            await using var writer =
                new StreamWriter(
                    stream,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: true));


            await writer
                .WriteAsync(
                    content);


            await writer
                .FlushAsync();


            SelectedFilePanel.IsVisible =
                true;


            SelectedFileNameText.Text =
                "SOCYVIA_Posts_Template.csv";


            SelectedFileStatusText.Text =
                IsArabic()
                    ? "تم حفظ القالب. افتحه في Excel واملأ بياناتك."
                    : "Template saved. Open it in Excel and enter your data.";


            SelectedFileStatusText.Foreground =
                Brush(
                    "#27846A");
        }
        catch (Exception exception)
        {
            ShowGeneralError(
                IsArabic()
                    ? $"تعذر حفظ القالب: {exception.Message}"
                    : $"Template could not be saved: {exception.Message}");
        }
    }


    // =========================================================
    // SELECT FILE
    // =========================================================

    private async Task SelectFileAsync()
    {
        if (_study is null)
        {
            ShowGeneralError(
                IsArabic()
                    ? "تعذر تحديد الدراسة الحالية."
                    : "The current study could not be identified.");

            return;
        }


        try
        {
            if (!StorageProvider.CanOpen)
            {
                ShowGeneralError(
                    IsArabic()
                        ? "تعذر فتح نافذة اختيار الملفات."
                        : "The file picker is not available.");

                return;
            }


            var files =
                await StorageProvider
                    .OpenFilePickerAsync(
                        new FilePickerOpenOptions
                        {
                            Title =
                                IsArabic()
                                    ? "اختيار ملف بيانات SOCYVIA"
                                    : "Select SOCYVIA data file",

                            AllowMultiple =
                                false,

                            FileTypeFilter =
                                new[]
                                {
                                    new FilePickerFileType(
                                        "CSV")
                                    {
                                        Patterns =
                                            new[]
                                            {
                                                "*.csv"
                                            },

                                        MimeTypes =
                                            new[]
                                            {
                                                "text/csv",
                                                "text/plain"
                                            }
                                    }
                                }
                        });


            if (files.Count == 0)
            {
                return;
            }


            var file =
                files[0];


            SelectedFilePanel.IsVisible =
                true;


            SelectedFileNameText.Text =
                file.Name;


            SelectedFileStatusText.Text =
                IsArabic()
                    ? "جار فحص الملف..."
                    : "Validating file...";


            SelectedFileStatusText.Foreground =
                Brush(
                    "#8E9AAD");


            ClearPreview();


            await using var stream =
                await file
                    .OpenReadAsync();


            _preview =
                await StimulusPostImportService
                    .ParseAsync(
                        stream,
                        _study.Id,
                        _groups);


            RenderPreview();
        }
        catch (Exception exception)
        {
            _preview =
                null;


            ImportButton.IsEnabled =
                false;


            ShowGeneralError(
                IsArabic()
                    ? $"تعذر قراءة الملف: {exception.Message}"
                    : $"The file could not be read: {exception.Message}");
        }
    }


    // =========================================================
    // RENDER PREVIEW
    // =========================================================

    private void RenderPreview()
    {
        if (_preview is null)
        {
            return;
        }


        PreviewSummaryPanel.IsVisible =
            true;


        PreviewCard.IsVisible =
            true;


        TotalRowsValue.Text =
            _preview.TotalRows
                .ToString();


        ValidRowsValue.Text =
            _preview.ValidRows
                .ToString();


        InvalidRowsValue.Text =
            _preview.InvalidRows
                .ToString();


        if (_preview.GeneralErrors.Count > 0)
        {
            ShowGeneralError(
                string.Join(
                    Environment.NewLine,
                    _preview.GeneralErrors));


            SelectedFileStatusText.Text =
                IsArabic()
                    ? "الملف غير صالح للاستيراد"
                    : "The file cannot be imported";


            SelectedFileStatusText.Foreground =
                Brush(
                    "#D84A5B");
        }
        else
        {
            HideGeneralError();


            SelectedFileStatusText.Text =
                _preview.InvalidRows == 0
                    ? IsArabic()
                        ? "الملف صالح للاستيراد"
                        : "File is ready to import"
                    : IsArabic()
                        ? "تم العثور على بعض الصفوف التي تحتاج إلى تصحيح"
                        : "Some rows need correction";


            SelectedFileStatusText.Foreground =
                _preview.InvalidRows == 0
                    ? Brush(
                        "#27846A")
                    : Brush(
                        "#D08A2F");
        }


        ImportButton.IsEnabled =
            _preview.CanImport;


        PreviewRowsContainer
            .Children
            .Clear();


        // Keep preview manageable.
        var rows =
            _preview.Rows
                .Take(
                    100)
                .ToList();


        foreach (var row in rows)
        {
            PreviewRowsContainer
                .Children
                .Add(
                    CreatePreviewRow(
                        row));
        }


        if (_preview.Rows.Count > 100)
        {
            PreviewRowsContainer
                .Children
                .Add(
                    new TextBlock
                    {
                        Text =
                            IsArabic()
                                ? $"يتم عرض أول 100 صف من أصل {_preview.Rows.Count}"
                                : $"Showing the first 100 of {_preview.Rows.Count} rows",

                        FontFamily =
                            IsArabic()
                                ? _arabicFont
                                : _englishFont,

                        FontSize =
                            8,

                        Foreground =
                            Brush(
                                "#929EB1"),

                        HorizontalAlignment =
                            HorizontalAlignment.Center,

                        TextAlignment =
                            TextAlignment.Center,

                        Margin =
                            new Thickness(
                                0,
                                6)
                    });
        }
    }


    // =========================================================
    // PREVIEW ROW
    // =========================================================

    private Control CreatePreviewRow(
        ImportRowResult result)
    {
        var valid =
            result.IsValid;


        var rowNumber =
            new Border
            {
                Width =
                    36,

                Height =
                    28,

                Background =
                    valid
                        ? Brush(
                            "#ECF8F4")
                        : Brush(
                            "#FFF1F3"),

                CornerRadius =
                    new CornerRadius(
                        7),

                VerticalAlignment =
                    VerticalAlignment.Center,

                Child =
                    new TextBlock
                    {
                        Text =
                            result.SourceRowNumber
                                .ToString(),

                        FontFamily =
                            _englishFont,

                        FontSize =
                            8,

                        FontWeight =
                            FontWeight.SemiBold,

                        Foreground =
                            valid
                                ? Brush(
                                    "#27846A")
                                : Brush(
                                    "#D84A5B"),

                        HorizontalAlignment =
                            HorizontalAlignment.Center,

                        VerticalAlignment =
                            VerticalAlignment.Center,

                        TextAlignment =
                            TextAlignment.Center
                    }
            };


        var statusBadge =
            new Border
            {
                MinWidth =
                    64,

                Height =
                    26,

                Padding =
                    new Thickness(
                        8,
                        0),

                Background =
                    valid
                        ? Brush(
                            "#ECF8F4")
                        : Brush(
                            "#FFF1F3"),

                CornerRadius =
                    new CornerRadius(
                        7),

                VerticalAlignment =
                    VerticalAlignment.Center,

                Child =
                    new TextBlock
                    {
                        Text =
                            valid
                                ? IsArabic()
                                    ? "صالح"
                                    : "Valid"
                                : IsArabic()
                                    ? "خطأ"
                                    : "Error",

                        FontFamily =
                            IsArabic()
                                ? _arabicFont
                                : _englishFont,

                        FontSize =
                            7.5,

                        FontWeight =
                            FontWeight.SemiBold,

                        Foreground =
                            valid
                                ? Brush(
                                    "#27846A")
                                : Brush(
                                    "#D84A5B"),

                        HorizontalAlignment =
                            HorizontalAlignment.Center,

                        VerticalAlignment =
                            VerticalAlignment.Center,

                        TextAlignment =
                            TextAlignment.Center
                    }
            };


        var title =
            new TextBlock
            {
                Text =
                    result.Post?.Title
                    ?? (IsArabic()
                        ? "بدون عنوان"
                        : "Untitled"),

                FontFamily =
                    IsArabic()
                        ? _arabicFont
                        : _englishFont,

                FontSize =
                    9,

                FontWeight =
                    FontWeight.SemiBold,

                Foreground =
                    Brush(
                        "#354762"),

                TextTrimming =
                    TextTrimming.CharacterEllipsis,

                FlowDirection =
                    IsArabic()
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight,

                TextAlignment =
                    IsArabic()
                        ? TextAlignment.Right
                        : TextAlignment.Left
            };


        var details =
            new TextBlock
            {
                Text =
                    valid
                        ? BuildValidRowDescription(
                            result.Post)
                        : string.Join(
                            " • ",
                            result.Errors),

                FontFamily =
                    IsArabic()
                        ? _arabicFont
                        : _englishFont,

                FontSize =
                    7.8,

                Foreground =
                    valid
                        ? Brush(
                            "#8B97AA")
                        : Brush(
                            "#B15D68"),

                TextWrapping =
                    TextWrapping.Wrap,

                FlowDirection =
                    IsArabic()
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight,

                TextAlignment =
                    IsArabic()
                        ? TextAlignment.Right
                        : TextAlignment.Left
            };


        var textPanel =
            new StackPanel
            {
                Spacing =
                    3,

                VerticalAlignment =
                    VerticalAlignment.Center,

                HorizontalAlignment =
                    HorizontalAlignment.Stretch
            };


        textPanel.Children.Add(
            title);


        textPanel.Children.Add(
            details);


        var grid =
            new Grid();


        if (IsArabic())
        {
            grid.ColumnDefinitions =
                new ColumnDefinitions(
                    "Auto,12,*,12,Auto");


            Grid.SetColumn(
                rowNumber,
                0);


            Grid.SetColumn(
                textPanel,
                2);


            Grid.SetColumn(
                statusBadge,
                4);
        }
        else
        {
            grid.ColumnDefinitions =
                new ColumnDefinitions(
                    "Auto,12,*,12,Auto");


            Grid.SetColumn(
                statusBadge,
                0);


            Grid.SetColumn(
                textPanel,
                2);


            Grid.SetColumn(
                rowNumber,
                4);
        }


        grid.Children.Add(
            rowNumber);


        grid.Children.Add(
            textPanel);


        grid.Children.Add(
            statusBadge);


        return new Border
        {
            Classes =
            {
                "previewRow"
            },

            Padding =
                new Thickness(
                    13,
                    9),

            BorderBrush =
                valid
                    ? Brush(
                        "#E7EBF2")
                    : Brush(
                        "#F0CDD2"),

            Background =
                valid
                    ? Brushes.White
                    : Brush(
                        "#FFFAFB"),

            Child =
                grid
        };
    }


    // =========================================================
    // VALID DESCRIPTION
    // =========================================================

    private string BuildValidRowDescription(
        StimulusPost? post)
    {
        if (post is null)
        {
            return string.Empty;
        }


        var parts =
            new List<string>();


        parts.Add(
            LocalizePlatform(
                post.Platform));


        parts.Add(
            LocalizeContentType(
                post.ContentType));


        if (!string.IsNullOrWhiteSpace(
                post.AuthorName))
        {
            parts.Add(
                post.AuthorName);
        }


        return string.Join(
            "  •  ",
            parts);
    }


    // =========================================================
    // IMPORT
    // =========================================================

    private async Task ImportAsync()
    {
        if (_isImporting ||
            _study is null ||
            _preview is null ||
            !_preview.CanImport)
        {
            return;
        }


        try
        {
            _isImporting =
                true;


            ImportButton.IsEnabled =
                false;


            ImportButtonText.Text =
                IsArabic()
                    ? "جار الاستيراد..."
                    : "Importing...";


            var result =
                await StimulusPostImportService
                    .ImportValidRowsAsync(
                        _preview,
                        _study.Id);


            if (result.ImportedRows > 0)
            {
                await ShowResultAsync(
                    result);


                Close(
                    true);

                return;
            }


            ShowGeneralError(
                IsArabic()
                    ? "لم يتم استيراد أي صف."
                    : "No rows were imported.");
        }
        catch (Exception exception)
        {
            ShowGeneralError(
                IsArabic()
                    ? $"تعذر استيراد البيانات: {exception.Message}"
                    : $"Import failed: {exception.Message}");
        }
        finally
        {
            _isImporting =
                false;


            if (IsVisible)
            {
                ImportButton.IsEnabled =
                    _preview?.CanImport ==
                    true;


                ImportButtonText.Text =
                    IsArabic()
                        ? "استيراد البيانات"
                        : "Import data";
            }
        }
    }


    // =========================================================
    // RESULT
    // =========================================================

    private async Task ShowResultAsync(
        ImportCommitResult result)
    {
        var dialog =
            new Window
            {
                Width =
                    390,

                Height =
                    220,

                CanResize =
                    false,

                ShowInTaskbar =
                    false,

                WindowStartupLocation =
                    WindowStartupLocation.CenterOwner,

                Background =
                    Brush(
                        "#F7F9FD"),

                Title =
                    "SOCYVIA"
            };


        WindowAppearanceService.ApplyAppIcon(
            dialog);


        var title =
            new TextBlock
            {
                Text =
                    IsArabic()
                        ? "اكتمل الاستيراد"
                        : "Import complete",

                FontFamily =
                    IsArabic()
                        ? _arabicFont
                        : _englishFont,

                FontSize =
                    14,

                FontWeight =
                    FontWeight.SemiBold,

                Foreground =
                    Brush(
                        "#263855"),

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                TextAlignment =
                    TextAlignment.Center
            };


        var body =
            new TextBlock
            {
                Text =
                    IsArabic()
                        ? $"عدد المنشورات المستوردة بنجاح: {result.ImportedRows}.\nعدد الصفوف غير الصالحة التي تم تجاوزها: {result.SkippedRows}."
                        : $"{result.ImportedRows} posts were imported successfully.\n{result.SkippedRows} invalid rows were skipped.",

                FontFamily =
                    IsArabic()
                        ? _arabicFont
                        : _englishFont,

                FontSize =
                    9,

                Foreground =
                    Brush(
                        "#718097"),

                TextAlignment =
                    TextAlignment.Center,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                TextWrapping =
                    TextWrapping.Wrap
            };


        var button =
            new Button
            {
                Height =
                    38,

                MinWidth =
                    92,

                Content =
                    IsArabic()
                        ? "تم"
                        : "Done",

                FontFamily =
                    IsArabic()
                        ? _arabicFont
                        : _englishFont,

                Background =
                    Brush(
                        "#2563EB"),

                Foreground =
                    Brushes.White,

                BorderThickness =
                    new Thickness(0),

                CornerRadius =
                    new CornerRadius(
                        9),

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                HorizontalContentAlignment =
                    HorizontalAlignment.Center,

                VerticalContentAlignment =
                    VerticalAlignment.Center
            };


        button.Click +=
            (_, _) =>
            {
                dialog.Close();
            };


        dialog.Content =
            new Border
            {
                Margin =
                    new Thickness(
                        16),

                Padding =
                    new Thickness(
                        22),

                Background =
                    Brushes.White,

                BorderBrush =
                    Brush(
                        "#E3E9F3"),

                BorderThickness =
                    new Thickness(
                        1),

                CornerRadius =
                    new CornerRadius(
                        13),

                Child =
                    new StackPanel
                    {
                        Spacing =
                            16,

                        Children =
                        {
                            title,
                            body,
                            button
                        }
                    }
            };


        await dialog
            .ShowDialog(
                this);
    }


    // =========================================================
    // CLEAR PREVIEW
    // =========================================================

    private void ClearPreview()
    {
        _preview =
            null;


        PreviewSummaryPanel.IsVisible =
            false;


        PreviewCard.IsVisible =
            false;


        GeneralErrorPanel.IsVisible =
            false;


        PreviewRowsContainer
            .Children
            .Clear();


        ImportButton.IsEnabled =
            false;
    }


    // =========================================================
    // GENERAL ERROR
    // =========================================================

    private void ShowGeneralError(
        string message)
    {
        GeneralErrorPanel.IsVisible =
            true;


        GeneralErrorText.Text =
            message;
    }


    private void HideGeneralError()
    {
        GeneralErrorPanel.IsVisible =
            false;


        GeneralErrorText.Text =
            string.Empty;
    }


    // =========================================================
    // LANGUAGE
    // =========================================================

    private bool IsArabic()
    {
        return LocalizationService
            .IsArabic;
    }


    private void ConfigureLanguage()
    {
        if (IsArabic())
        {
            ApplyArabic();
        }
        else
        {
            ApplyEnglish();
        }


        ConfigureDirection();
    }


    // =========================================================
    // ARABIC
    // =========================================================

    private void ApplyArabic()
    {
        RootImportWindow.FontFamily =
            _arabicFont;


        Title =
            "SOCYVIA · استيراد البيانات";


        SetCenter(
            CloseTopText,
            "إغلاق");


        SetRight(
            ImportTitle,
            "استيراد المنشورات");


        SetRight(
            ImportSubtitle,
            "حمل قالب SOCYVIA أو اختر ملف CSV جاهزا للاستيراد");


        SetRight(
            TemplateTitle,
            "إعداد ملف البيانات");


        SetRight(
            TemplateDescription,
            "الأفضل استعمال قالب SOCYVIA لضمان أن البرنامج يفهم أسماء الحقول وبنية البيانات بشكل صحيح.");


        SetCenter(
            DownloadTemplateText,
            "تحميل قالب SOCYVIA");

        SetCenter(
            SelectFileText,
            "انقر هنا لاستيراد أو رفع ملف CSV");


        SetRight(
            PreviewTitle,
            "معاينة وفحص البيانات");


        SetRight(
            PreviewDescription,
            "SOCYVIA يفحص الصفوف قبل إدخالها إلى الدراسة");


        SetCenter(
            TotalRowsLabel,
            "إجمالي الصفوف");


        SetCenter(
            ValidRowsLabel,
            "صالحة");


        SetCenter(
            InvalidRowsLabel,
            "أخطاء");


        SetRight(
            GeneralErrorTitle,
            "تعذر اعتماد الملف");


        SetRight(
            PreviewRowsTitle,
            "الصفوف");


        PreviewHintText.Text =
            "يتم استيراد الصفوف الصالحة فقط";


        PreviewHintText.FontFamily =
            _arabicFont;


        PreviewHintText.FlowDirection =
            FlowDirection.RightToLeft;


        SetCenter(
            CancelText,
            "إلغاء");


        SetCenter(
            ImportButtonText,
            "استيراد البيانات");
    }


    // =========================================================
    // ENGLISH
    // =========================================================

    private void ApplyEnglish()
    {
        RootImportWindow.FontFamily =
            _englishFont;


        Title =
            "SOCYVIA · Import data";


        SetCenter(
            CloseTopText,
            "Close");


        SetLeft(
            ImportTitle,
            "Import posts");


        SetLeft(
            ImportSubtitle,
            "Download the SOCYVIA template or select a CSV dataset");


        SetLeft(
            TemplateTitle,
            "Prepare your dataset");


        SetLeft(
            TemplateDescription,
            "Using the SOCYVIA template is recommended so field names and data structure can be validated correctly.");


        SetCenter(
            DownloadTemplateText,
            "Download SOCYVIA template");

        SetCenter(
            SelectFileText,
            "Click here to import or upload a CSV file");


        SetLeft(
            PreviewTitle,
            "Data preview & validation");


        SetLeft(
            PreviewDescription,
            "SOCYVIA checks each row before adding it to the study");


        SetCenter(
            TotalRowsLabel,
            "Total rows");


        SetCenter(
            ValidRowsLabel,
            "Valid");


        SetCenter(
            InvalidRowsLabel,
            "Errors");


        SetLeft(
            GeneralErrorTitle,
            "Dataset cannot be used");


        SetLeft(
            PreviewRowsTitle,
            "Rows");


        PreviewHintText.Text =
            "Only valid rows will be imported";


        PreviewHintText.FontFamily =
            _englishFont;


        PreviewHintText.FlowDirection =
            FlowDirection.LeftToRight;


        SetCenter(
            CancelText,
            "Cancel");


        SetCenter(
            ImportButtonText,
            "Import data");
    }


    // =========================================================
    // DIRECTION
    // =========================================================

    private void ConfigureDirection()
    {
        if (IsArabic())
        {
            Grid.SetColumn(
                CloseTopButton,
                0);


            Grid.SetColumn(
                HeaderTextPanel,
                2);


            HeaderTextPanel.HorizontalAlignment =
                HorizontalAlignment.Right;


            Grid.SetColumn(
                CancelButton,
                0);


            Grid.SetColumn(
                ImportButton,
                2);


            SelectedFileTextPanel.HorizontalAlignment =
                HorizontalAlignment.Right;


            TemplateHeaderPanel.HorizontalAlignment =
                HorizontalAlignment.Right;


            PreviewHeaderPanel.HorizontalAlignment =
                HorizontalAlignment.Right;
        }
        else
        {
            Grid.SetColumn(
                HeaderTextPanel,
                0);


            Grid.SetColumn(
                CloseTopButton,
                2);


            HeaderTextPanel.HorizontalAlignment =
                HorizontalAlignment.Left;


            Grid.SetColumn(
                ImportButton,
                0);


            Grid.SetColumn(
                CancelButton,
                2);


            SelectedFileTextPanel.HorizontalAlignment =
                HorizontalAlignment.Left;


            TemplateHeaderPanel.HorizontalAlignment =
                HorizontalAlignment.Left;


            PreviewHeaderPanel.HorizontalAlignment =
                HorizontalAlignment.Left;
        }


        SelectedFileStatusText.FontFamily =
            IsArabic()
                ? _arabicFont
                : _englishFont;


        SelectedFileStatusText.FlowDirection =
            IsArabic()
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;


        SelectedFileStatusText.TextAlignment =
            IsArabic()
                ? TextAlignment.Right
                : TextAlignment.Left;


        GeneralErrorText.FontFamily =
            IsArabic()
                ? _arabicFont
                : _englishFont;


        GeneralErrorText.FlowDirection =
            IsArabic()
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;


        GeneralErrorText.TextAlignment =
            IsArabic()
                ? TextAlignment.Right
                : TextAlignment.Left;
    }


    // =========================================================
    // LOCALIZATION HELPERS
    // =========================================================

    private void SetRight(
        TextBlock block,
        string text)
    {
        block.Text =
            text;


        block.FontFamily =
            _arabicFont;


        block.FlowDirection =
            FlowDirection.RightToLeft;


        block.TextAlignment =
            TextAlignment.Right;


        block.HorizontalAlignment =
            HorizontalAlignment.Right;
    }


    private void SetLeft(
        TextBlock block,
        string text)
    {
        block.Text =
            text;


        block.FontFamily =
            _englishFont;


        block.FlowDirection =
            FlowDirection.LeftToRight;


        block.TextAlignment =
            TextAlignment.Left;


        block.HorizontalAlignment =
            HorizontalAlignment.Left;
    }


    private void SetCenter(
        TextBlock block,
        string text)
    {
        block.Text =
            text;


        block.FontFamily =
            IsArabic()
                ? _arabicFont
                : _englishFont;


        block.FlowDirection =
            IsArabic()
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;


        block.TextAlignment =
            TextAlignment.Center;


        block.HorizontalAlignment =
            HorizontalAlignment.Center;
    }


    // =========================================================
    // LOCALIZED VALUES
    // =========================================================

    private string LocalizePlatform(
        string platform)
    {
        if (!IsArabic())
        {
            return platform;
        }


        return platform switch
        {
            "Generic" =>
                "عام",

            "Facebook" =>
                "فيسبوك",

            "Instagram" =>
                "إنستغرام",

            "TikTok" =>
                "تيك توك",

            "X" =>
                "X",

            "YouTube" =>
                "يوتيوب",

            "News" =>
                "موقع إخباري",

            "Custom" =>
                "مخصص",

            _ =>
                platform
        };
    }


    private string LocalizeContentType(
        string type)
    {
        if (!IsArabic())
        {
            return type;
        }


        return type switch
        {
            "Text" =>
                "نص",

            "Image" =>
                "صورة",

            "Video" =>
                "فيديو",

            "Audio" =>
                "صوت",

            "Link" =>
                "رابط",

            "Mixed" =>
                "مختلط",

            _ =>
                type
        };
    }


    // =========================================================
    // BRUSH
    // =========================================================

    private static IBrush Brush(
        string hex)
    {
        return new SolidColorBrush(
            Color.Parse(
                hex));
    }
}
