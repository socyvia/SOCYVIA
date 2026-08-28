using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SOCYVIA.Models;
using SOCYVIA.Repositories;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

public partial class StudiesView : UserControl
{
    public event EventHandler? NewStudyRequested;

    public event EventHandler<Study>? OpenStudyRequested;

    public event EventHandler<Study>? EditStudyRequested;

    public event EventHandler<Study>? ArchiveStudyRequested;

    public event EventHandler<Study>? RestoreStudyRequested;

    public event EventHandler<Study>? DeleteStudyRequested;

    public event EventHandler<Study>? DuplicateStudyRequested;


    private ResearcherProfile? _researcher;


    private List<Study> _studies =
        new();

    private readonly Dictionary<string, ExperimentReadinessResult> _readinessByStudy =
        new(StringComparer.Ordinal);


    private bool _showArchived;


    private readonly FontFamily _englishFont =
        new("avares://SOCYVIA/Assets/Fonts#IBM Plex Sans");


    private readonly FontFamily _arabicFont =
        new("avares://SOCYVIA/Assets/Fonts#IBM Plex Sans Arabic");


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public StudiesView()
    {
        InitializeComponent();


        SetupEvents();

        ConfigureLanguage();

        UpdateModeVisuals();
    }


    public StudiesView(
        ResearcherProfile researcher)
        : this()
    {
        _researcher =
            researcher;


        AttachedToVisualTree +=
            async (_, _) =>
            {
                await ReloadAsync();
            };
    }


    // =========================================================
    // EVENTS
    // =========================================================

    private void SetupEvents()
    {
        NewStudyButton.Click +=
            (_, _) =>
            {
                NewStudyRequested?.Invoke(
                    this,
                    EventArgs.Empty);
            };


        EmptyCreateButton.Click +=
            (_, _) =>
            {
                NewStudyRequested?.Invoke(
                    this,
                    EventArgs.Empty);
            };


        ActiveStudiesButton.Click +=
            async (_, _) =>
            {
                if (!_showArchived)
                {
                    return;
                }


                _showArchived =
                    false;


                UpdateModeVisuals();


                await ReloadAsync();
            };


        ArchivedStudiesButton.Click +=
            async (_, _) =>
            {
                if (_showArchived)
                {
                    return;
                }


                _showArchived =
                    true;


                UpdateModeVisuals();


                await ReloadAsync();
            };


        SearchBox.TextChanged +=
            (_, _) =>
            {
                RenderStudies();
            };
    }


    // =========================================================
    // LOAD
    // =========================================================

    public async Task ReloadAsync()
    {
        if (_researcher is null)
        {
            return;
        }


        try
        {
            if (_showArchived)
            {
                _studies =
                    await ArchivedStudyRepository
                        .GetByResearcherAsync(
                            _researcher.Id);
            }
            else
            {
                _studies =
                    await StudyService
                        .GetStudiesAsync(
                            _researcher.Id);
            }

            _studies = _studies
                .Where(study => !DemoAccessPolicy.IsDemoStudy(study))
                .ToList();

            _readinessByStudy.Clear();
            if (!_showArchived)
            {
                var readinessTasks = _studies.Select(async study =>
                    (study.Id, Result: await ExperimentReadinessService.EvaluateAsync(study)));
                foreach (var item in await Task.WhenAll(readinessTasks))
                {
                    _readinessByStudy[item.Id] = item.Result;
                }
            }


            RenderStudies();
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Studies loading error: {exception}");
        }
    }


    // =========================================================
    // ACTIVE / ARCHIVED MODE
    // =========================================================

    private void UpdateModeVisuals()
    {
        ActiveStudiesButton.Classes.Remove(
            "selected");


        ArchivedStudiesButton.Classes.Remove(
            "selected");


        if (_showArchived)
        {
            ArchivedStudiesButton.Classes.Add(
                "selected");
        }
        else
        {
            ActiveStudiesButton.Classes.Add(
                "selected");
        }


        // No reason to create a study from Archived mode.
        NewStudyButton.IsVisible =
            !_showArchived;


        EmptyCreateButton.IsVisible =
            !_showArchived;


        SearchBox.PlaceholderText =
            IsArabic()
                ? _showArchived
                    ? "ابحث في الدراسات المؤرشفة"
                    : "ابحث في الدراسات"
                : _showArchived
                    ? "Search archived studies"
                    : "Search studies";
    }


    // =========================================================
    // RENDER
    // =========================================================

    private void RenderStudies()
    {
        StudiesContainer
            .Children
            .Clear();


        var query =
            SearchBox.Text?
                .Trim()
            ?? string.Empty;


        var filtered =
            string.IsNullOrWhiteSpace(
                query)
                ? _studies
                : _studies
                    .Where(
                        study =>
                            study.Title.Contains(
                                query,
                                StringComparison.CurrentCultureIgnoreCase)
                            ||
                            (
                                study.Description?
                                    .Contains(
                                        query,
                                        StringComparison.CurrentCultureIgnoreCase)
                                ?? false
                            )
                            ||
                            study.Status.Contains(
                                query,
                                StringComparison.OrdinalIgnoreCase))
                    .ToList();


        StudyCountText.Text =
            GetStudyCountText(
                _studies.Count);


        EmptyPanel.IsVisible =
            _studies.Count == 0;


        NoResultsPanel.IsVisible =
            _studies.Count > 0 &&
            filtered.Count == 0;


        // =====================================================
        // EMPTY STATE
        // =====================================================

        if (_studies.Count == 0)
        {
            if (IsArabic())
            {
                EmptyTitle.Text =
                    _showArchived
                        ? "لا توجد دراسات مؤرشفة"
                        : "لا توجد دراسات بعد";


                EmptyDescription.Text =
                    _showArchived
                        ? "ستظهر هنا الدراسات التي تختار أرشفتها، ويمكن استرجاعها في أي وقت"
                        : "أنشئ أول دراسة للبدء في إدارة البيانات والمشاركين والتجربة";
            }
            else
            {
                EmptyTitle.Text =
                    _showArchived
                        ? "No archived studies"
                        : "No studies yet";


                EmptyDescription.Text =
                    _showArchived
                        ? "Studies you archive will appear here and can be restored at any time"
                        : "Create your first study to manage data, participants and research sessions";
            }


            return;
        }


        if (filtered.Count == 0)
        {
            return;
        }


        foreach (var study in filtered)
        {
            StudiesContainer
                .Children
                .Add(
                    CreateStudyCard(
                        study));
        }
    }


    // =========================================================
    // STUDY CARD
    // =========================================================

    private Control CreateStudyCard(
        Study study)
    {
        var isArabic =
            IsArabic();


        // =====================================================
        // TITLE
        //
        // Arabic  -> far right
        // English -> far left
        // =====================================================

        var title =
            new TextBlock
            {
                Text =
                    study.Title,

                FontFamily =
                    isArabic
                        ? _arabicFont
                        : _englishFont,

                FontSize =
                    11,

                FontWeight =
                    FontWeight.SemiBold,

                Foreground =
                    Brush(
                        "#263855"),

                FlowDirection =
                    isArabic
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight,

                TextAlignment =
                    isArabic
                        ? TextAlignment.Right
                        : TextAlignment.Left,

                HorizontalAlignment =
                    HorizontalAlignment.Stretch,

                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };


        // =====================================================
        // DESCRIPTION
        //
        // Long semantic content follows language direction.
        // =====================================================

        var description =
            new TextBlock
            {
                Text =
                    string.IsNullOrWhiteSpace(
                        study.Description)
                        ? isArabic
                            ? "لا يوجد وصف"
                            : "No description"
                        : study.Description,

                FontFamily =
                    isArabic
                        ? _arabicFont
                        : _englishFont,

                FontSize =
                    8.3,

                Foreground =
                    Brush(
                        "#8D99AB"),

                FlowDirection =
                    isArabic
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight,

                TextAlignment =
                    isArabic
                        ? TextAlignment.Right
                        : TextAlignment.Left,

                HorizontalAlignment =
                    HorizontalAlignment.Stretch,

                TextWrapping =
                    TextWrapping.NoWrap,

                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };


        var textPanel =
            new StackPanel
            {
                Spacing =
                    4,

                HorizontalAlignment =
                    HorizontalAlignment.Stretch,

                VerticalAlignment =
                    VerticalAlignment.Center
            };


        textPanel.Children.Add(
            title);


        textPanel.Children.Add(
            description);


        // =====================================================
        // STATUS BADGE
        //
        // Small floating item = always centered.
        // =====================================================

        var statusText =
            new TextBlock
            {
                Text =
                    GetLocalizedStatus(
                        study.Status),

                FontFamily =
                    isArabic
                        ? _arabicFont
                        : _englishFont,

                FontSize =
                    7.7,

                FontWeight =
                    FontWeight.SemiBold,

                Foreground =
                    Brush(
                        "#2563EB"),

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center,

                TextAlignment =
                    TextAlignment.Center
            };


        var statusBadge =
            new Border
            {
                MinWidth =
                    68,

                Height =
                    25,

                Padding =
                    new Thickness(
                        10,
                        0),

                Background =
                    Brush(
                        "#F0F1FF"),

                CornerRadius =
                    new CornerRadius(8),

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center,

                Child =
                    statusText
            };


        // =====================================================
        // DATE
        // =====================================================

        var updatedText =
            new TextBlock
            {
                Text =
                    GetUpdatedText(
                        study),

                FontFamily =
                    isArabic
                        ? _arabicFont
                        : _englishFont,

                FontSize =
                    7.8,

                Foreground =
                    Brush(
                        "#9AA5B7"),

                VerticalAlignment =
                    VerticalAlignment.Center
            };


        // =====================================================
        // SAMPLE
        // =====================================================

        TextBlock? sampleText =
            null;


        if (study.TargetSampleSize.HasValue)
        {
            sampleText =
                new TextBlock
                {
                    Text =
                        isArabic
                            ? $"العينة: {study.TargetSampleSize.Value}"
                            : $"Sample: {study.TargetSampleSize.Value}",

                    FontFamily =
                        isArabic
                            ? _arabicFont
                            : _englishFont,

                    FontSize =
                        7.8,

                    Foreground =
                        Brush(
                            "#9AA5B7"),

                    VerticalAlignment =
                        VerticalAlignment.Center
                };
        }


        // =====================================================
        // METADATA
        //
        // All small pieces stay visually compact.
        // =====================================================

        var metadataPanel =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,

                Spacing =
                    9,

                HorizontalAlignment =
                    isArabic
                        ? HorizontalAlignment.Right
                        : HorizontalAlignment.Left,

                VerticalAlignment =
                    VerticalAlignment.Center,

                FlowDirection =
                    isArabic
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight
            };


        metadataPanel.Children.Add(
            statusBadge);

        if (_readinessByStudy.TryGetValue(study.Id, out var readiness))
        {
            var readinessBadge = new Border
            {
                Padding = new Thickness(9, 4),
                CornerRadius = new CornerRadius(8),
                Child = new TextBlock
                {
                    Text = readiness.IsReady
                        ? (isArabic ? "جاهزة" : "Ready")
                        : (isArabic
                            ? $"{readiness.ErrorCount} تحتاج معالجة"
                            : $"{readiness.ErrorCount} need attention"),
                    FontFamily = isArabic ? _arabicFont : _englishFont,
                    FontSize = 7.5,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brush(readiness.IsReady ? "#177A5B" : "#9A650E"),
                    TextAlignment = TextAlignment.Center
                }
            };
            readinessBadge.Classes.Add("badge");
            readinessBadge.Classes.Add(readiness.IsReady ? "success" : "warning");
            metadataPanel.Children.Add(CreateDividerDot());
            metadataPanel.Children.Add(readinessBadge);
        }


        metadataPanel.Children.Add(
            CreateDividerDot());


        metadataPanel.Children.Add(
            updatedText);


        if (sampleText is not null)
        {
            metadataPanel.Children.Add(
                CreateDividerDot());


            metadataPanel.Children.Add(
                sampleText);
        }


        // =====================================================
        // STUDY INFO
        // =====================================================

        var studyInfoPanel =
            new StackPanel
            {
                Spacing =
                    8,

                HorizontalAlignment =
                    HorizontalAlignment.Stretch,

                VerticalAlignment =
                    VerticalAlignment.Center
            };


        studyInfoPanel.Children.Add(
            textPanel);


        studyInfoPanel.Children.Add(
            metadataPanel);


        // =====================================================
        // CLICKABLE CONTENT
        // =====================================================

        var openButton =
            new Button
            {
                Background =
                    Brushes.Transparent,

                BorderThickness =
                    new Thickness(0),

                Padding =
                    new Thickness(
                        18,
                        12),

                HorizontalAlignment =
                    HorizontalAlignment.Stretch,

                HorizontalContentAlignment =
                    HorizontalAlignment.Stretch,

                VerticalContentAlignment =
                    VerticalAlignment.Center,

                Cursor =
                    new Cursor(
                        StandardCursorType.Hand),

                Content =
                    studyInfoPanel
            };


        // Archived studies are not opened directly.
        if (!_showArchived)
        {
            openButton.Click +=
                (_, _) =>
                {
                    OpenStudyRequested?.Invoke(
                        this,
                        study);
                };
        }


        var menuButton =
            CreateMenuButton(
                study);


        // =====================================================
        // CARD STRUCTURE
        //
        // Arabic:
        //
        // [ ⋯ ] ...................... [ STUDY INFO ]
        //
        // English:
        //
        // [ STUDY INFO ] ............. [ ⋯ ]
        //
        // =====================================================

        var cardGrid =
            new Grid();


        if (isArabic)
        {
            cardGrid.ColumnDefinitions =
                new ColumnDefinitions(
                    "52,*");


            Grid.SetColumn(
                menuButton,
                0);


            Grid.SetColumn(
                openButton,
                1);
        }
        else
        {
            cardGrid.ColumnDefinitions =
                new ColumnDefinitions(
                    "*,52");


            Grid.SetColumn(
                openButton,
                0);


            Grid.SetColumn(
                menuButton,
                1);
        }


        cardGrid.Children.Add(
            openButton);


        cardGrid.Children.Add(
            menuButton);


        return new Border
        {
            MinHeight =
                86,

            Background =
                Brush(
                    "#FAFBFD"),

            BorderBrush =
                Brush(
                    "#E8ECF3"),

            BorderThickness =
                new Thickness(1),

            CornerRadius =
                new CornerRadius(11),

            HorizontalAlignment =
                HorizontalAlignment.Stretch,

            Child =
                cardGrid
        };
    }


    // =========================================================
    // MENU BUTTON
    // =========================================================

    private Button CreateMenuButton(
        Study study)
    {
        var button =
            new Button
            {
                Width =
                    38,

                Height =
                    38,

                Padding =
                    new Thickness(0),

                Margin =
                    new Thickness(
                        7,
                        0),

                Background =
                    Brushes.Transparent,

                BorderThickness =
                    new Thickness(0),

                CornerRadius =
                    new CornerRadius(8),

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center,

                HorizontalContentAlignment =
                    HorizontalAlignment.Center,

                VerticalContentAlignment =
                    VerticalAlignment.Center,

                Cursor =
                    new Cursor(
                        StandardCursorType.Hand),

                Content =
                    new TextBlock
                    {
                        Text =
                            "⋯",

                        FontFamily =
                            _englishFont,

                        FontSize =
                            18,

                        Foreground =
                            Brush(
                                "#7C899D"),

                        HorizontalAlignment =
                            HorizontalAlignment.Center,

                        VerticalAlignment =
                            VerticalAlignment.Center,

                        TextAlignment =
                            TextAlignment.Center
                    }
            };


        var panel =
            new StackPanel
            {
                Width =
                    180,

                Spacing =
                    3
            };


        // =====================================================
        // ARCHIVED MODE
        // =====================================================

        if (_showArchived)
        {
            var restoreButton =
                CreateMenuItem(
                    IsArabic()
                        ? "استرجاع الدراسة"
                        : "Restore study");


            restoreButton.Click +=
                (_, _) =>
                {
                    RestoreStudyRequested?.Invoke(
                        this,
                        study);


                    button.Flyout?.Hide();
                };


            var deleteButton =
                CreateMenuItem(
                    IsArabic()
                        ? "حذف نهائي"
                        : "Delete permanently",

                    destructive: true);


            deleteButton.Click +=
                (_, _) =>
                {
                    DeleteStudyRequested?.Invoke(
                        this,
                        study);


                    button.Flyout?.Hide();
                };


            panel.Children.Add(
                restoreButton);


            panel.Children.Add(
                CreateMenuDivider());


            panel.Children.Add(
                deleteButton);
        }

        // =====================================================
        // ACTIVE MODE
        // =====================================================

        else
        {
            var openButton =
                CreateMenuItem(
                    IsArabic()
                        ? "فتح الدراسة"
                        : "Open study");


            openButton.Click +=
                (_, _) =>
                {
                    OpenStudyRequested?.Invoke(
                        this,
                        study);


                    button.Flyout?.Hide();
                };


            var editButton =
                CreateMenuItem(
                    IsArabic()
                        ? "تعديل الدراسة"
                        : "Edit study");


            editButton.Click +=
                (_, _) =>
                {
                    EditStudyRequested?.Invoke(
                        this,
                        study);


                    button.Flyout?.Hide();
                };

            var duplicateButton =
                CreateMenuItem(
                    IsArabic()
                        ? "نسخ الدراسة"
                        : "Duplicate Study");

            duplicateButton.Click +=
                (_, _) =>
                {
                    DuplicateStudyRequested?.Invoke(
                        this,
                        study);

                    button.Flyout?.Hide();
                };


            var archiveButton =
                CreateMenuItem(
                    IsArabic()
                        ? "أرشفة الدراسة"
                        : "Archive study");


            archiveButton.Click +=
                (_, _) =>
                {
                    ArchiveStudyRequested?.Invoke(
                        this,
                        study);


                    button.Flyout?.Hide();
                };


            var deleteButton =
                CreateMenuItem(
                    IsArabic()
                        ? "حذف نهائي"
                        : "Delete permanently",

                    destructive: true);


            deleteButton.Click +=
                (_, _) =>
                {
                    DeleteStudyRequested?.Invoke(
                        this,
                        study);


                    button.Flyout?.Hide();
                };


            panel.Children.Add(
                openButton);


            panel.Children.Add(
                editButton);


            panel.Children.Add(
                duplicateButton);


            panel.Children.Add(
                CreateMenuDivider());


            panel.Children.Add(
                archiveButton);


            panel.Children.Add(
                deleteButton);
        }


        button.Flyout =
            new Flyout
            {
                Content =
                    new Border
                    {
                        Padding =
                            new Thickness(7),

                        Background =
                            Brushes.White,

                        CornerRadius =
                            new CornerRadius(11),

                        Child =
                            panel
                    }
            };


        return button;
    }


    // =========================================================
    // MENU ITEM
    //
    // Small control = centered
    // =========================================================

    private Button CreateMenuItem(
        string text,
        bool destructive = false)
    {
        return new Button
        {
            Height =
                34,

            Padding =
                new Thickness(
                    10,
                    0),

            Background =
                Brushes.Transparent,

            BorderThickness =
                new Thickness(0),

            CornerRadius =
                new CornerRadius(8),

            HorizontalContentAlignment =
                HorizontalAlignment.Center,

            VerticalContentAlignment =
                VerticalAlignment.Center,

            Cursor =
                new Cursor(
                    StandardCursorType.Hand),

            Content =
                new TextBlock
                {
                    Text =
                        text,

                    FontFamily =
                        IsArabic()
                            ? _arabicFont
                            : _englishFont,

                    FontSize =
                        9,

                    Foreground =
                        Brush(
                            destructive
                                ? "#D84A5B"
                                : "#455671"),

                    FlowDirection =
                        IsArabic()
                            ? FlowDirection.RightToLeft
                            : FlowDirection.LeftToRight,

                    TextAlignment =
                        TextAlignment.Center,

                    HorizontalAlignment =
                        HorizontalAlignment.Center,

                    VerticalAlignment =
                        VerticalAlignment.Center
                }
        };
    }


    private Border CreateMenuDivider()
    {
        return new Border
        {
            Height =
                1,

            Margin =
                new Thickness(
                    4),

            Background =
                Brush(
                    "#EDF0F5")
        };
    }


    // =========================================================
    // LANGUAGE
    // =========================================================

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


        UpdateModeVisuals();
    }


    private bool IsArabic()
    {
        return LocalizationService.IsArabic;
    }


    // =========================================================
    // ARABIC
    // =========================================================

    private void ApplyArabic()
    {
        RootStudiesView.FontFamily =
            _arabicFont;


        // =====================================================
        // HEADER
        //
        // Long title on right.
        // Small action button on opposite side.
        // =====================================================

        Grid.SetColumn(
            HeaderTextPanel,
            2);


        Grid.SetColumn(
            NewStudyButton,
            0);


        HeaderTextPanel.HorizontalAlignment =
            HorizontalAlignment.Right;


        SetArabicRight(
            PageTitle,
            "مكتبة الدراسات");


        SetArabicRight(
            PageSubtitle,
            "ابحث في مشاريعك النشطة والمؤرشفة وافتح مساحة العمل البحثية");


        NewStudyButtonText.Text =
            "+ دراسة جديدة";


        NewStudyButtonText.FontFamily =
            _arabicFont;


        NewStudyButtonText.FlowDirection =
            FlowDirection.RightToLeft;


        NewStudyButtonText.TextAlignment =
            TextAlignment.Center;


        NewStudyButtonText.HorizontalAlignment =
            HorizontalAlignment.Center;


        // =====================================================
        // TOOLBAR
        //
        // Arabic:
        //
        // [ count ] [ tabs ] .............. [ search ]
        //
        // Search is long semantic input -> right.
        // =====================================================

        ToolbarGrid.ColumnDefinitions =
            new ColumnDefinitions(
                "Auto,14,Auto,*,260");


        if (StudyCountText.Parent is Control arabicCountContainer)
        {
            Grid.SetColumn(
                arabicCountContainer,
                0);
        }


        Grid.SetColumn(
            ModeTabsBorder,
            2);


        Grid.SetColumn(
            SearchBox,
            4);


        SearchBox.FontFamily =
            _arabicFont;


        SearchBox.FlowDirection =
            FlowDirection.RightToLeft;


        SearchBox.TextAlignment =
            TextAlignment.Right;


        ModeTabsPanel.FlowDirection =
            FlowDirection.RightToLeft;


        ActiveStudiesText.Text =
            "النشطة";


        ArchivedStudiesText.Text =
            "المؤرشفة";


        ConfigureArabicCenter(
            ActiveStudiesText);


        ConfigureArabicCenter(
            ArchivedStudiesText);


        StudyCountText.FontFamily =
            _arabicFont;


        StudyCountText.FlowDirection =
            FlowDirection.RightToLeft;


        StudyCountText.TextAlignment =
            TextAlignment.Center;


        StudyCountText.HorizontalAlignment =
            HorizontalAlignment.Center;


        // =====================================================
        // EMPTY
        // =====================================================

        SetArabicCenter(
            EmptyTitle,
            "لا توجد دراسات بعد");


        SetArabicCenter(
            EmptyDescription,
            "أنشئ أول دراسة للبدء في إدارة البيانات والمشاركين والتجربة");


        EmptyCreateButtonText.Text =
            "+ إنشاء دراسة";


        ConfigureArabicCenter(
            EmptyCreateButtonText);


        SetArabicCenter(
            NoResultsTitle,
            "لم يتم العثور على نتائج");


        SetArabicCenter(
            NoResultsDescription,
            "جرب كلمة بحث أخرى");
    }


    // =========================================================
    // ENGLISH
    // =========================================================

    private void ApplyEnglish()
    {
        RootStudiesView.FontFamily =
            _englishFont;


        // =====================================================
        // HEADER
        // =====================================================

        Grid.SetColumn(
            HeaderTextPanel,
            0);


        Grid.SetColumn(
            NewStudyButton,
            2);


        HeaderTextPanel.HorizontalAlignment =
            HorizontalAlignment.Left;


        SetEnglishLeft(
            PageTitle,
            "Research library");


        SetEnglishLeft(
            PageSubtitle,
            "Search active and archived projects, then enter their research workspace");


        NewStudyButtonText.Text =
            "+ New Study";


        NewStudyButtonText.FontFamily =
            _englishFont;


        NewStudyButtonText.FlowDirection =
            FlowDirection.LeftToRight;


        NewStudyButtonText.TextAlignment =
            TextAlignment.Center;


        NewStudyButtonText.HorizontalAlignment =
            HorizontalAlignment.Center;


        // =====================================================
        // TOOLBAR
        //
        // English:
        //
        // [ search ] .............. [ tabs ] [ count ]
        // =====================================================

        ToolbarGrid.ColumnDefinitions =
            new ColumnDefinitions(
                "260,*,Auto,14,Auto");


        Grid.SetColumn(
            SearchBox,
            0);


        Grid.SetColumn(
            ModeTabsBorder,
            2);


        if (StudyCountText.Parent is Control englishCountContainer)
        {
            Grid.SetColumn(
                englishCountContainer,
                4);
        }


        SearchBox.FontFamily =
            _englishFont;


        SearchBox.FlowDirection =
            FlowDirection.LeftToRight;


        SearchBox.TextAlignment =
            TextAlignment.Left;


        ModeTabsPanel.FlowDirection =
            FlowDirection.LeftToRight;


        ActiveStudiesText.Text =
            "Active";


        ArchivedStudiesText.Text =
            "Archived";


        ConfigureEnglishCenter(
            ActiveStudiesText);


        ConfigureEnglishCenter(
            ArchivedStudiesText);


        StudyCountText.FontFamily =
            _englishFont;


        StudyCountText.FlowDirection =
            FlowDirection.LeftToRight;


        StudyCountText.TextAlignment =
            TextAlignment.Center;


        StudyCountText.HorizontalAlignment =
            HorizontalAlignment.Center;


        // =====================================================
        // EMPTY
        // =====================================================

        SetEnglishCenter(
            EmptyTitle,
            "No studies yet");


        SetEnglishCenter(
            EmptyDescription,
            "Create your first study to manage data, participants and research sessions");


        EmptyCreateButtonText.Text =
            "+ Create Study";


        ConfigureEnglishCenter(
            EmptyCreateButtonText);


        SetEnglishCenter(
            NoResultsTitle,
            "No results found");


        SetEnglishCenter(
            NoResultsDescription,
            "Try another search term");
    }


    // =========================================================
    // STATUS
    // =========================================================

    private string GetLocalizedStatus(
        string status)
    {
        if (!IsArabic())
        {
            return status;
        }


        return status switch
        {
            "Draft" =>
                "مسودة",

            "Ready" =>
                "جاهزة",

            "Running" =>
                "قيد التنفيذ",

            "Paused" =>
                "متوقفة مؤقتا",

            "Completed" =>
                "مكتملة",

            "Archived" =>
                "مؤرشفة",

            _ =>
                status
        };
    }


    // =========================================================
    // UPDATED
    // =========================================================

    private string GetUpdatedText(
        Study study)
    {
        var date =
            study.UpdatedAtUtc
                .ToLocalTime()
                .ToString(
                    "dd MMM yyyy");


        return IsArabic()
            ? $"آخر تحديث: {date}"
            : $"Updated: {date}";
    }


    // =========================================================
    // COUNT
    // =========================================================

    private string GetStudyCountText(
        int count)
    {
        if (IsArabic())
        {
            return $"{count} دراسة";
        }


        return count == 1
            ? "1 study"
            : $"{count} studies";
    }


    // =========================================================
    // HELPERS
    // =========================================================

    private static IBrush Brush(
        string hex)
    {
        return new SolidColorBrush(
            Color.Parse(
                hex));
    }


    private static TextBlock CreateDividerDot()
    {
        return new TextBlock
        {
            Text =
                "•",

            FontSize =
                7,

            Foreground =
                new SolidColorBrush(
                    Color.Parse(
                        "#C1C8D3")),

            HorizontalAlignment =
                HorizontalAlignment.Center,

            VerticalAlignment =
                VerticalAlignment.Center,

            TextAlignment =
                TextAlignment.Center
        };
    }


    private void SetArabicRight(
        TextBlock textBlock,
        string text)
    {
        textBlock.Text =
            text;


        textBlock.FontFamily =
            _arabicFont;


        textBlock.FlowDirection =
            FlowDirection.RightToLeft;


        textBlock.TextAlignment =
            TextAlignment.Right;


        textBlock.HorizontalAlignment =
            HorizontalAlignment.Right;
    }


    private void SetEnglishLeft(
        TextBlock textBlock,
        string text)
    {
        textBlock.Text =
            text;


        textBlock.FontFamily =
            _englishFont;


        textBlock.FlowDirection =
            FlowDirection.LeftToRight;


        textBlock.TextAlignment =
            TextAlignment.Left;


        textBlock.HorizontalAlignment =
            HorizontalAlignment.Left;
    }


    private void SetArabicCenter(
        TextBlock textBlock,
        string text)
    {
        textBlock.Text =
            text;


        ConfigureArabicCenter(
            textBlock);
    }


    private void ConfigureArabicCenter(
        TextBlock textBlock)
    {
        textBlock.FontFamily =
            _arabicFont;


        textBlock.FlowDirection =
            FlowDirection.RightToLeft;


        textBlock.TextAlignment =
            TextAlignment.Center;


        textBlock.HorizontalAlignment =
            HorizontalAlignment.Center;
    }


    private void SetEnglishCenter(
        TextBlock textBlock,
        string text)
    {
        textBlock.Text =
            text;


        ConfigureEnglishCenter(
            textBlock);
    }


    private void ConfigureEnglishCenter(
        TextBlock textBlock)
    {
        textBlock.FontFamily =
            _englishFont;


        textBlock.FlowDirection =
            FlowDirection.LeftToRight;


        textBlock.TextAlignment =
            TextAlignment.Center;


        textBlock.HorizontalAlignment =
            HorizontalAlignment.Center;
    }
}
