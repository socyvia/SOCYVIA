using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SOCYVIA.Models;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

public partial class StudyBuilderView : UserControl
{
    public event EventHandler? CancelRequested;

    public event EventHandler<Study>? StudyCreated;


    private ResearcherProfile? _researcher;

    private Study? _editingStudy;


    private int _currentStep =
        1;


    private bool _isSaving;

    private bool _existingStudyLoaded;
    private bool _autosaveEventsAttached;
    private StudySaveCoordinator? _saveCoordinator;


    private readonly FontFamily _englishFont =
        new("avares://SOCYVIA/Assets/Fonts#IBM Plex Sans");


    private readonly FontFamily _arabicFont =
        new("avares://SOCYVIA/Assets/Fonts#IBM Plex Sans Arabic");


    // =========================================================
    // CONSTRUCTORS
    // =========================================================

    public StudyBuilderView()
    {
        InitializeComponent();

        SetupEvents();

        ConfigureLanguage();

        UpdateStep();
    }


    public StudyBuilderView(
        ResearcherProfile researcher)
        : this()
    {
        _researcher =
            researcher;
    }


    public StudyBuilderView(
        ResearcherProfile researcher,
        Study study)
        : this()
    {
        _researcher =
            researcher;

        _editingStudy =
            study;


        ConfigureLanguage();


        AttachedToVisualTree +=
            async (_, _) =>
            {
                await LoadExistingStudyAsync();
            };
    }


    // =========================================================
    // MODE
    // =========================================================

    private bool IsEditMode()
    {
        return _editingStudy is not null;
    }


    // =========================================================
    // EVENTS
    // =========================================================

    private void SetupEvents()
    {
        CancelTopButton.Click +=
            async (_, _) =>
            {
                if (!await FlushAutosaveAsync()) return;
                CancelRequested?.Invoke(
                    this,
                    EventArgs.Empty);
            };


        BackButton.Click +=
            (_, _) =>
            {
                if (_currentStep <= 1)
                {
                    return;
                }


                _currentStep--;


                ClearError();

                UpdateStep();
            };


        NextButton.Click +=
            async (_, _) =>
            {
                await HandleNextAsync();
            };


        UsesPhysiologyCheckBox
            .IsCheckedChanged +=
            (_, _) =>
            {
                UpdatePhysiologyState();
            };


        // GROUPS

        GroupMinusButton.Click +=
            (_, _) =>
            {
                StepNumber(
                    GroupCountBox,
                    -1,
                    1,
                    12);
            };


        GroupPlusButton.Click +=
            (_, _) =>
            {
                StepNumber(
                    GroupCountBox,
                    1,
                    1,
                    12);
            };


        // SAMPLE

        SampleMinusButton.Click +=
            (_, _) =>
            {
                StepNumber(
                    TargetSampleBox,
                    -1,
                    1,
                    100000);
            };


        SamplePlusButton.Click +=
            (_, _) =>
            {
                StepNumber(
                    TargetSampleBox,
                    1,
                    1,
                    100000);
            };


        // DURATION

        DurationMinusButton.Click +=
            (_, _) =>
            {
                StepNumber(
                    ExpectedDurationBox,
                    -1,
                    1,
                    1440);
            };


        DurationPlusButton.Click +=
            (_, _) =>
            {
                StepNumber(
                    ExpectedDurationBox,
                    1,
                    1,
                    1440);
            };
    }


    // =========================================================
    // LOAD EXISTING STUDY
    // =========================================================

    private async Task LoadExistingStudyAsync()
    {
        if (_editingStudy is null ||
            _existingStudyLoaded)
        {
            return;
        }


        _existingStudyLoaded =
            true;


        StudyTitleBox.Text =
            _editingStudy.Title;


        StudyDescriptionBox.Text =
            _editingStudy.Description
            ?? string.Empty;


        ResearchQuestionBox.Text =
            _editingStudy.ResearchQuestion
            ?? string.Empty;


        HypothesisBox.Text =
            _editingStudy.Hypothesis
            ?? string.Empty;


        StudyTypeComboBox.SelectedIndex =
            _editingStudy.StudyType switch
            {
                "Observational" => 1,
                "Survey" => 2,
                "Mixed" => 3,
                _ => 0
            };


        DesignTypeComboBox.SelectedIndex =
            _editingStudy.DesignType switch
            {
                "WithinSubjects" => 1,
                "Mixed" => 2,
                "SingleGroup" => 3,
                _ => 0
            };


        AssignmentMethodComboBox.SelectedIndex =
            _editingStudy.AssignmentMethod switch
            {
                "Random" => 1,
                "BalancedRandom" => 2,
                "Imported" => 3,
                _ => 0
            };


        RandomizeStimuliCheckBox.IsChecked =
            _editingStudy.RandomizeStimuli;


        TargetSampleBox.Text =
            (_editingStudy.TargetSampleSize
             ?? 90)
            .ToString();


        ExpectedDurationBox.Text =
            (_editingStudy.ExpectedSessionDurationMinutes
             ?? 30)
            .ToString();


        UsesStimuliCheckBox.IsChecked =
            _editingStudy.UsesStimuli;


        UsesQuestionnaireCheckBox.IsChecked =
            _editingStudy.UsesQuestionnaires;


        UsesPhysiologyCheckBox.IsChecked =
            _editingStudy.UsesPhysiologicalData;


        EegCheckBox.IsChecked =
            _editingStudy.EegEnabled;


        GsrCheckBox.IsChecked =
            _editingStudy.GsrEnabled;


        ConsentCheckBox.IsChecked =
            _editingStudy.RequireParticipantConsent;


        var groups =
            await StudyService.GetGroupsAsync(
                _editingStudy.Id);


        GroupCountBox.Text =
            Math.Max(
                    1,
                    groups.Count)
                .ToString();


        // We do not change group count here after creation.
        GroupCountBox.IsEnabled =
            false;

        GroupMinusButton.IsEnabled =
            false;

        GroupPlusButton.IsEnabled =
            false;


        UpdatePhysiologyState();

        ConfigureLanguage();

        UpdateStep();

        AttachAutosave();
    }

    private void AttachAutosave()
    {
        if (_autosaveEventsAttached || _editingStudy is null) return;
        _autosaveEventsAttached = true;
        _saveCoordinator = StudySaveCoordinatorRegistry.ForStudy(_editingStudy.Id);
        _saveCoordinator.StateChanged += OnSaveStateChanged;
        AutosaveStateText.IsVisible = true;
        UpdateAutosaveState(_saveCoordinator.State);

        StudyTitleBox.TextChanged += (_, _) => ScheduleAutosave();
        StudyDescriptionBox.TextChanged += (_, _) => ScheduleAutosave();
        ResearchQuestionBox.TextChanged += (_, _) => ScheduleAutosave();
        HypothesisBox.TextChanged += (_, _) => ScheduleAutosave();
        TargetSampleBox.TextChanged += (_, _) => ScheduleAutosave();
        ExpectedDurationBox.TextChanged += (_, _) => ScheduleAutosave();
        StudyTypeComboBox.SelectionChanged += (_, _) => ScheduleAutosave();
        DesignTypeComboBox.SelectionChanged += (_, _) => ScheduleAutosave();
        AssignmentMethodComboBox.SelectionChanged += (_, _) => ScheduleAutosave();
        RandomizeStimuliCheckBox.IsCheckedChanged += (_, _) => ScheduleAutosave();
        UsesStimuliCheckBox.IsCheckedChanged += (_, _) => ScheduleAutosave();
        UsesQuestionnaireCheckBox.IsCheckedChanged += (_, _) => ScheduleAutosave();
        UsesPhysiologyCheckBox.IsCheckedChanged += (_, _) => ScheduleAutosave();
        EegCheckBox.IsCheckedChanged += (_, _) => ScheduleAutosave();
        GsrCheckBox.IsCheckedChanged += (_, _) => ScheduleAutosave();
        ConsentCheckBox.IsCheckedChanged += (_, _) => ScheduleAutosave();
    }

    private void ScheduleAutosave()
    {
        if (_editingStudy is null || _saveCoordinator is null) return;
        _saveCoordinator.MarkDirty(async cancellationToken =>
        {
            await Dispatcher.UIThread.InvokeAsync(() => PopulateStudyFromForm(_editingStudy));
            cancellationToken.ThrowIfCancellationRequested();
            await StudyService.UpdateStudyAsync(_editingStudy);
        });
    }

    private void OnSaveStateChanged(object? sender, StudySaveState state) =>
        Dispatcher.UIThread.Post(() => UpdateAutosaveState(state));

    private void UpdateAutosaveState(StudySaveState state)
    {
        AutosaveStateText.Text = state switch
        {
            StudySaveState.Saving => IsArabic() ? "جار الحفظ..." : "Saving...",
            StudySaveState.Saved => IsArabic() ? "تم الحفظ" : "Saved",
            StudySaveState.UnsavedChanges => IsArabic() ? "تغييرات غير محفوظة" : "Unsaved Changes",
            _ => IsArabic() ? "تعذر الحفظ" : "Save Failed"
        };
        AutosaveStateText.Foreground = Brush(state == StudySaveState.SaveFailed ? "#B4233E" : "#7A8799");
    }

    private async Task<bool> FlushAutosaveAsync()
    {
        if (_saveCoordinator is null) return true;
        var saved = await _saveCoordinator.FlushAsync();
        if (!saved) ShowError(IsArabic()
            ? "تعذر حفظ التغييرات. أصلح المشكلة قبل مغادرة الدراسة."
            : "Changes could not be saved. Resolve the problem before leaving the study.");
        return saved;
    }


    // =========================================================
    // NUMBER CONTROL
    // =========================================================

    private static void StepNumber(
        TextBox textBox,
        int delta,
        int minimum,
        int maximum)
    {
        var value =
            ParseNumber(
                textBox.Text,
                minimum);


        value =
            Math.Clamp(
                value + delta,
                minimum,
                maximum);


        textBox.Text =
            value.ToString();
    }


    private static int ParseNumber(
        string? text,
        int fallback)
    {
        if (!int.TryParse(
                text,
                out var value))
        {
            return fallback;
        }


        return value;
    }


    private static int ParseClampedNumber(
        TextBox textBox,
        int minimum,
        int maximum,
        int fallback)
    {
        var value =
            ParseNumber(
                textBox.Text,
                fallback);


        value =
            Math.Clamp(
                value,
                minimum,
                maximum);


        textBox.Text =
            value.ToString();


        return value;
    }


    // =========================================================
    // NEXT
    // =========================================================

    private async Task HandleNextAsync()
    {
        if (_isSaving)
        {
            return;
        }


        ClearError();


        if (_currentStep == 1)
        {
            if (!ValidateBasics())
            {
                return;
            }


            _currentStep =
                2;


            UpdateStep();

            return;
        }


        if (_currentStep == 2)
        {
            ValidateDesignNumbers();


            _currentStep =
                3;


            UpdateStep();

            return;
        }


        if (_currentStep == 3)
        {
            if (!ValidateModules())
            {
                return;
            }


            PrepareReview();


            _currentStep =
                4;


            UpdateStep();

            return;
        }


        if (_currentStep == 4)
        {
            await SaveStudyAsync();
        }
    }


    // =========================================================
    // VALIDATION
    // =========================================================

    private bool ValidateBasics()
    {
        var title =
            StudyTitleBox.Text?
                .Trim()
            ?? string.Empty;


        if (string.IsNullOrWhiteSpace(
                title))
        {
            ShowError(
                IsArabic()
                    ? "أدخل عنوان الدراسة قبل المتابعة"
                    : "Enter a study title before continuing");


            StudyTitleBox.Focus();

            return false;
        }


        return true;
    }


    private void ValidateDesignNumbers()
    {
        ParseClampedNumber(
            GroupCountBox,
            1,
            12,
            3);


        ParseClampedNumber(
            TargetSampleBox,
            1,
            100000,
            90);


        ParseClampedNumber(
            ExpectedDurationBox,
            1,
            1440,
            30);
    }


    private bool ValidateModules()
    {
        var hasStimuli =
            UsesStimuliCheckBox.IsChecked ==
            true;


        var hasQuestionnaire =
            UsesQuestionnaireCheckBox.IsChecked ==
            true;


        var hasPhysiology =
            UsesPhysiologyCheckBox.IsChecked ==
            true;


        if (!hasStimuli &&
            !hasQuestionnaire &&
            !hasPhysiology)
        {
            ShowError(
                IsArabic()
                    ? "اختر مصدرا واحدا على الأقل من مصادر بيانات الدراسة"
                    : "Select at least one study data source");


            return false;
        }


        return true;
    }


    // =========================================================
    // SAVE
    // =========================================================

    private async Task SaveStudyAsync()
    {
        if (_isSaving)
        {
            return;
        }


        if (_researcher is null)
        {
            ShowError(
                IsArabic()
                    ? "تعذر تحديد الباحث الحالي"
                    : "The current researcher could not be identified");

            return;
        }


        try
        {
            _isSaving =
                true;


            NextButton.IsEnabled =
                false;


            NextButtonText.Text =
                IsArabic()
                    ? "جار الحفظ..."
                    : "Saving...";


            Study study;


            if (_editingStudy is null)
            {
                var numberOfGroups =
                    ParseClampedNumber(
                        GroupCountBox,
                        1,
                        12,
                        3);


                study =
                    await StudyService.CreateStudyAsync(
                        _researcher.Id,
                        StudyTitleBox.Text?.Trim()
                        ?? string.Empty,
                        NormalizeOptional(
                            StudyDescriptionBox.Text),
                        numberOfGroups);
            }
            else
            {
                study =
                    _editingStudy;
            }


            PopulateStudyFromForm(
                study);


            await StudyService.UpdateStudyAsync(
                study);


            StudyCreated?.Invoke(
                this,
                study);
        }
        catch (Exception exception)
        {
            ShowError(
                IsArabic()
                    ? $"تعذر حفظ الدراسة: {exception.Message}"
                    : $"Study could not be saved: {exception.Message}");
        }
        finally
        {
            _isSaving =
                false;


            NextButton.IsEnabled =
                true;


            UpdateNextButtonText();
        }
    }


    private void PopulateStudyFromForm(
        Study study)
    {
        study.Title =
            StudyTitleBox.Text?.Trim()
            ?? string.Empty;


        study.Description =
            NormalizeOptional(
                StudyDescriptionBox.Text);


        study.StudyType =
            GetStudyType();


        study.DesignType =
            GetDesignType();


        study.AssignmentMethod =
            GetAssignmentMethod();


        study.RandomizeStimuli =
            RandomizeStimuliCheckBox.IsChecked ==
            true;


        study.TargetSampleSize =
            ParseClampedNumber(
                TargetSampleBox,
                1,
                100000,
                90);


        study.ExpectedSessionDurationMinutes =
            ParseClampedNumber(
                ExpectedDurationBox,
                1,
                1440,
                30);


        study.ResearchQuestion =
            NormalizeOptional(
                ResearchQuestionBox.Text);


        study.Hypothesis =
            NormalizeOptional(
                HypothesisBox.Text);


        study.UsesStimuli =
            UsesStimuliCheckBox.IsChecked ==
            true;


        study.UsesQuestionnaires =
            UsesQuestionnaireCheckBox.IsChecked ==
            true;


        study.UsesPhysiologicalData =
            UsesPhysiologyCheckBox.IsChecked ==
            true;


        study.EegEnabled =
            study.UsesPhysiologicalData &&
            EegCheckBox.IsChecked ==
            true;


        study.GsrEnabled =
            study.UsesPhysiologicalData &&
            GsrCheckBox.IsChecked ==
            true;


        study.RequireParticipantConsent =
            ConsentCheckBox.IsChecked ==
            true;
    }


    // =========================================================
    // REVIEW
    // =========================================================

    private void PrepareReview()
    {
        ReviewStudyValue.Text =
            StudyTitleBox.Text?
                .Trim()
            ?? string.Empty;


        ReviewDesignValue.Text =
            IsArabic()
                ? GetArabicDesignSummary()
                : GetEnglishDesignSummary();


        ReviewGroupsValue.Text =
            ParseClampedNumber(
                    GroupCountBox,
                    1,
                    12,
                    3)
                .ToString();


        ReviewSampleValue.Text =
            ParseClampedNumber(
                    TargetSampleBox,
                    1,
                    100000,
                    90)
                .ToString();


        var modules =
            new List<string>();


        if (UsesStimuliCheckBox.IsChecked ==
            true)
        {
            modules.Add(
                IsArabic()
                    ? "المنشورات والمحفزات"
                    : "Posts & stimuli");
        }


        if (UsesQuestionnaireCheckBox.IsChecked ==
            true)
        {
            modules.Add(
                IsArabic()
                    ? "الاستبيانات"
                    : "Questionnaires");
        }


        if (UsesPhysiologyCheckBox.IsChecked ==
            true)
        {
            var physiology =
                IsArabic()
                    ? "القياسات الفسيولوجية"
                    : "Physiological measures";


            if (EegCheckBox.IsChecked ==
                true)
            {
                physiology +=
                    " · EEG";
            }


            if (GsrCheckBox.IsChecked ==
                true)
            {
                physiology +=
                    " · GSR";
            }


            modules.Add(
                physiology);
        }


        ReviewModulesValue.Text =
            string.Join(
                "  •  ",
                modules);


        ConfigureReviewDirection();
    }


    // =========================================================
    // STEP UI
    // =========================================================

    private void UpdateStep()
    {
        BasicsPanel.IsVisible =
            _currentStep == 1;


        DesignPanel.IsVisible =
            _currentStep == 2;


        ModulesPanel.IsVisible =
            _currentStep == 3;


        ReviewPanel.IsVisible =
            _currentStep == 4;


        BackButton.IsVisible =
            _currentStep > 1;


        UpdateNextButtonText();


        UpdateStepCircle(
            Step1Circle,
            Step1Number,
            Step1Label,
            1);


        UpdateStepCircle(
            Step2Circle,
            Step2Number,
            Step2Label,
            2);


        UpdateStepCircle(
            Step3Circle,
            Step3Number,
            Step3Label,
            3);


        UpdateStepCircle(
            Step4Circle,
            Step4Number,
            Step4Label,
            4);
    }


    private void UpdateNextButtonText()
    {
        if (_currentStep == 4)
        {
            NextButtonText.Text =
                IsEditMode()
                    ? IsArabic()
                        ? "حفظ التعديلات"
                        : "Save Changes"
                    : IsArabic()
                        ? "إنشاء الدراسة"
                        : "Create Study";

            return;
        }


        NextButtonText.Text =
            IsArabic()
                ? "التالي"
                : "Next";
    }


    private void UpdateStepCircle(
        Border circle,
        TextBlock number,
        TextBlock label,
        int step)
    {
        circle.Classes.Remove(
            "active");


        circle.Classes.Remove(
            "completed");


        if (step ==
            _currentStep)
        {
            circle.Classes.Add(
                "active");


            number.Text =
                step.ToString();


            number.Foreground =
                Brush(
                    "#FFFFFF");


            label.Foreground =
                Brush(
                    "#2563EB");


            label.FontWeight =
                FontWeight.SemiBold;


            return;
        }


        if (step <
            _currentStep)
        {
            circle.Classes.Add(
                "completed");


            number.Text =
                "✓";


            number.Foreground =
                Brush(
                    "#2563EB");
        }
        else
        {
            number.Text =
                step.ToString();


            number.Foreground =
                Brush(
                    "#8995A8");
        }


        label.Foreground =
            Brush(
                "#8995A8");


        label.FontWeight =
            FontWeight.Normal;
    }


    // =========================================================
    // PHYSIOLOGY
    // =========================================================

    private void UpdatePhysiologyState()
    {
        var enabled =
            UsesPhysiologyCheckBox.IsChecked ==
            true;


        EegCheckBox.IsEnabled =
            enabled;


        GsrCheckBox.IsEnabled =
            enabled;


        if (!enabled)
        {
            EegCheckBox.IsChecked =
                false;


            GsrCheckBox.IsChecked =
                false;
        }
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


        ConfigureComboBoxes();

        ConfigureProgressDirection();

        ConfigureFieldDirection();

        ConfigureModuleDirection();

        ConfigureReviewDirection();

        UpdatePhysiologyState();

        UpdateNextButtonText();
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
        RootBuilder.FontFamily =
            _arabicFont;


        BuilderHeaderPanel.HorizontalAlignment =
            HorizontalAlignment.Right;


        BuilderTitle.Text =
            IsEditMode()
                ? "تعديل الدراسة"
                : "إنشاء دراسة جديدة";


        BuilderSubtitle.Text =
            IsEditMode()
                ? "عدل الإعدادات الأساسية للدراسة"
                : "حدد الأساس المنهجي للدراسة ثم أضف محتواها من مساحة الدراسة";


        SetArabicRight(
            BuilderTitle);


        SetArabicRight(
            BuilderSubtitle);


        CancelTopText.Text =
            "إلغاء";


        CancelTopText.FontFamily =
            _arabicFont;


        Step1Label.Text =
            "المعلومات";

        Step2Label.Text =
            "التصميم";

        Step3Label.Text =
            "مصادر البيانات";

        Step4Label.Text =
            "المراجعة";


        SetArabicCenter(
            Step1Label);

        SetArabicCenter(
            Step2Label);

        SetArabicCenter(
            Step3Label);

        SetArabicCenter(
            Step4Label);


        SetArabicRight(
            BasicsTitle,
            "معلومات الدراسة");


        SetArabicRight(
            BasicsSubtitle,
            "حدد هوية الدراسة وسؤالها البحثي");


        SetArabicRight(
            StudyTitleLabel,
            "عنوان الدراسة");


        SetArabicRight(
            StudyDescriptionLabel,
            "وصف مختصر");


        SetArabicRight(
            ResearchQuestionLabel,
            "السؤال البحثي");


        SetArabicRight(
            HypothesisLabel,
            "الفرضية");


        ConfigureArabicTextBox(
            StudyTitleBox,
            "أدخل عنوان الدراسة");


        ConfigureArabicTextBox(
            StudyDescriptionBox,
            "اكتب وصفا موجزا للدراسة");


        ConfigureArabicTextBox(
            ResearchQuestionBox,
            "ما السؤال الذي تحاول الدراسة الإجابة عنه؟");


        ConfigureArabicTextBox(
            HypothesisBox,
            "الفرضية المتوقعة إن وجدت");


        SetArabicRight(
            DesignTitle,
            "التصميم المنهجي");


        SetArabicRight(
            DesignSubtitle,
            "حدد بنية الدراسة وطريقة توزيع المشاركين");


        SetArabicCenter(
            StudyTypeLabel,
            "نوع الدراسة");


        SetArabicCenter(
            DesignTypeLabel,
            "التصميم التجريبي");


        SetArabicCenter(
            AssignmentMethodLabel,
            "طريقة توزيع المشاركين");


        SetArabicCenter(
            GroupCountLabel,
            IsEditMode()
                ? "عدد المجموعات"
                : "عدد المجموعات");


        SetArabicCenter(
            TargetSampleLabel,
            "حجم العينة المستهدف");


        SetArabicCenter(
            ExpectedDurationLabel,
            "مدة الجلسة المتوقعة بالدقائق");


        RandomizeStimuliCheckBox.Content =
            "ترتيب المنشورات أو المحفزات عشوائيا داخل الجلسة";


        RandomizeStimuliCheckBox.FlowDirection =
            FlowDirection.RightToLeft;


        RandomizeStimuliCheckBox.HorizontalAlignment =
            HorizontalAlignment.Right;


        SetArabicRight(
            ModulesTitle,
            "مصادر بيانات الدراسة");


        SetArabicRight(
            ModulesSubtitle,
            "اختر فقط الوحدات التي يحتاجها بروتوكولك");


        SetArabicRight(
            StimuliModuleTitle,
            "المنشورات والمحفزات الرقمية");


        SetArabicRight(
            StimuliModuleDescription,
            "نصوص، صور، فيديوهات، منشورات شبكات اجتماعية وبيانات التفاعل");


        SetArabicRight(
            QuestionnaireModuleTitle,
            "الاستبيانات");


        SetArabicRight(
            QuestionnaireModuleDescription,
            "استبيانات قبل التجربة أو بعدها أو كدراسة مستقلة");


        SetArabicRight(
            PhysiologyModuleTitle,
            "القياسات الفسيولوجية");


        SetArabicRight(
        PhysiologyModuleDescription,
            "وحدة اختيارية لربط البيانات السلوكية بالإشارات الفسيولوجية");

        EegCheckBox.Content = "التخطيط الكهربائي للدماغ (EEG) · OpenBCI";
        GsrCheckBox.Content = "الاستجابة الجلدية الكهربائية (GSR / EDA) · EmotiBit";


        ConsentCheckBox.Content =
            "اشتراط موافقة المشارك قبل بدء جمع البيانات";


        ConsentCheckBox.FlowDirection =
            FlowDirection.RightToLeft;


        ConsentCheckBox.HorizontalAlignment =
            HorizontalAlignment.Right;


        SetArabicRight(
            ReviewTitle,
            IsEditMode()
                ? "مراجعة التعديلات"
                : "مراجعة إعداد الدراسة");


        SetArabicRight(
            ReviewSubtitle,
            IsEditMode()
                ? "تحقق من التغييرات قبل حفظها"
                : "تحقق من الإعدادات قبل إنشاء مساحة الدراسة");


        ReviewStudyLabel.Text =
            "الدراسة";


        ReviewDesignLabel.Text =
            "التصميم";


        ReviewGroupsLabel.Text =
            "عدد المجموعات";


        ReviewSampleLabel.Text =
            "العينة المستهدفة";


        ReviewModulesLabel.Text =
            "مصادر البيانات";


        BackButtonText.Text =
            "السابق";


        BackButtonText.FontFamily =
            _arabicFont;
    }


    // =========================================================
    // ENGLISH
    // =========================================================

    private void ApplyEnglish()
    {
        RootBuilder.FontFamily =
            _englishFont;


        BuilderHeaderPanel.HorizontalAlignment =
            HorizontalAlignment.Left;


        BuilderTitle.Text =
            IsEditMode()
                ? "Edit study"
                : "Create a new study";


        BuilderSubtitle.Text =
            IsEditMode()
                ? "Edit the core study settings"
                : "Define the research foundation, then manage content inside the study workspace";


        SetEnglishLeft(
            BuilderTitle);


        SetEnglishLeft(
            BuilderSubtitle);


        CancelTopText.Text =
            "Cancel";


        CancelTopText.FontFamily =
            _englishFont;


        Step1Label.Text =
            "Basics";

        Step2Label.Text =
            "Design";

        Step3Label.Text =
            "Data Sources";

        Step4Label.Text =
            "Review";


        SetEnglishCenter(
            Step1Label);

        SetEnglishCenter(
            Step2Label);

        SetEnglishCenter(
            Step3Label);

        SetEnglishCenter(
            Step4Label);


        SetEnglishLeft(
            BasicsTitle,
            "Study information");


        SetEnglishLeft(
            BasicsSubtitle,
            "Define the study identity and research focus");


        SetEnglishLeft(
            StudyTitleLabel,
            "Study title");


        SetEnglishLeft(
            StudyDescriptionLabel,
            "Short description");


        SetEnglishLeft(
            ResearchQuestionLabel,
            "Research question");


        SetEnglishLeft(
            HypothesisLabel,
            "Hypothesis");


        ConfigureEnglishTextBox(
            StudyTitleBox,
            "Enter the study title");


        ConfigureEnglishTextBox(
            StudyDescriptionBox,
            "Briefly describe the study");


        ConfigureEnglishTextBox(
            ResearchQuestionBox,
            "What question is this study trying to answer?");


        ConfigureEnglishTextBox(
            HypothesisBox,
            "Expected hypothesis, if applicable");


        SetEnglishLeft(
            DesignTitle,
            "Research design");


        SetEnglishLeft(
            DesignSubtitle,
            "Configure groups, allocation and experimental structure");


        SetEnglishCenter(
            StudyTypeLabel,
            "Study type");


        SetEnglishCenter(
            DesignTypeLabel,
            "Experimental design");


        SetEnglishCenter(
            AssignmentMethodLabel,
            "Participant assignment");


        SetEnglishCenter(
            GroupCountLabel,
            "Number of groups");


        SetEnglishCenter(
            TargetSampleLabel,
            "Target sample size");


        SetEnglishCenter(
            ExpectedDurationLabel,
            "Expected session duration (minutes)");


        RandomizeStimuliCheckBox.Content =
            "Randomize posts or stimuli during the session";


        RandomizeStimuliCheckBox.FlowDirection =
            FlowDirection.LeftToRight;


        RandomizeStimuliCheckBox.HorizontalAlignment =
            HorizontalAlignment.Left;


        SetEnglishLeft(
            ModulesTitle,
            "Study data sources");


        SetEnglishLeft(
            ModulesSubtitle,
            "Use only the modules required by your protocol");


        SetEnglishLeft(
            StimuliModuleTitle,
            "Digital posts & stimuli");


        SetEnglishLeft(
            StimuliModuleDescription,
            "Text, images, videos, social posts and behavioural interaction data");


        SetEnglishLeft(
            QuestionnaireModuleTitle,
            "Questionnaires");


        SetEnglishLeft(
            QuestionnaireModuleDescription,
            "Pre-test, post-test, in-study surveys or questionnaire-only studies");


        SetEnglishLeft(
            PhysiologyModuleTitle,
            "Physiological measures");


        SetEnglishLeft(
        PhysiologyModuleDescription,
            "Optional synchronization with physiological data streams");

        EegCheckBox.Content = "EEG · OpenBCI";
        GsrCheckBox.Content = "GSR / EDA · EmotiBit";


        ConsentCheckBox.Content =
            "Require participant consent before data collection";


        ConsentCheckBox.FlowDirection =
            FlowDirection.LeftToRight;


        ConsentCheckBox.HorizontalAlignment =
            HorizontalAlignment.Left;


        SetEnglishLeft(
            ReviewTitle,
            IsEditMode()
                ? "Review changes"
                : "Review study setup");


        SetEnglishLeft(
            ReviewSubtitle,
            IsEditMode()
                ? "Check the changes before saving"
                : "Check the foundation before creating the study workspace");


        ReviewStudyLabel.Text =
            "Study";


        ReviewDesignLabel.Text =
            "Design";


        ReviewGroupsLabel.Text =
            "Groups";


        ReviewSampleLabel.Text =
            "Target sample";


        ReviewModulesLabel.Text =
            "Data sources";


        BackButtonText.Text =
            "Back";


        BackButtonText.FontFamily =
            _englishFont;
    }


    // =========================================================
    // RTL / LTR FIELD ORDER
    // =========================================================

    private void ConfigureFieldDirection()
    {
        if (IsArabic())
        {
            // First logical field goes to the RIGHT.
            Grid.SetColumn(
                ResearchQuestionPanel,
                2);

            Grid.SetColumn(
                HypothesisPanel,
                0);


            Grid.SetColumn(
                StudyTypePanel,
                2);

            Grid.SetColumn(
                DesignTypePanel,
                0);


            Grid.SetColumn(
                AssignmentMethodPanel,
                2);

            Grid.SetColumn(
                GroupCountPanel,
                0);


            Grid.SetColumn(
                TargetSamplePanel,
                2);

            Grid.SetColumn(
                ExpectedDurationPanel,
                0);


            StudyTypeComboBox.FlowDirection =
                FlowDirection.RightToLeft;

            DesignTypeComboBox.FlowDirection =
                FlowDirection.RightToLeft;

            AssignmentMethodComboBox.FlowDirection =
                FlowDirection.RightToLeft;


            StudyTypeComboBox.HorizontalContentAlignment =
                HorizontalAlignment.Center;

            DesignTypeComboBox.HorizontalContentAlignment =
                HorizontalAlignment.Center;

            AssignmentMethodComboBox.HorizontalContentAlignment =
                HorizontalAlignment.Center;
        }
        else
        {
            Grid.SetColumn(
                ResearchQuestionPanel,
                0);

            Grid.SetColumn(
                HypothesisPanel,
                2);


            Grid.SetColumn(
                StudyTypePanel,
                0);

            Grid.SetColumn(
                DesignTypePanel,
                2);


            Grid.SetColumn(
                AssignmentMethodPanel,
                0);

            Grid.SetColumn(
                GroupCountPanel,
                2);


            Grid.SetColumn(
                TargetSamplePanel,
                0);

            Grid.SetColumn(
                ExpectedDurationPanel,
                2);


            StudyTypeComboBox.FlowDirection =
                FlowDirection.LeftToRight;

            DesignTypeComboBox.FlowDirection =
                FlowDirection.LeftToRight;

            AssignmentMethodComboBox.FlowDirection =
                FlowDirection.LeftToRight;


            StudyTypeComboBox.HorizontalContentAlignment =
                HorizontalAlignment.Center;

            DesignTypeComboBox.HorizontalContentAlignment =
                HorizontalAlignment.Center;

            AssignmentMethodComboBox.HorizontalContentAlignment =
                HorizontalAlignment.Center;
        }
    }


    // =========================================================
    // PROGRESS RTL / LTR
    // =========================================================

    private void ConfigureProgressDirection()
    {
        if (IsArabic())
        {
            Grid.SetColumn(
                Step1Panel,
                6);

            Grid.SetColumn(
                StepConnector1,
                5);

            Grid.SetColumn(
                Step2Panel,
                4);

            Grid.SetColumn(
                StepConnector2,
                3);

            Grid.SetColumn(
                Step3Panel,
                2);

            Grid.SetColumn(
                StepConnector3,
                1);

            Grid.SetColumn(
                Step4Panel,
                0);
        }
        else
        {
            Grid.SetColumn(
                Step1Panel,
                0);

            Grid.SetColumn(
                StepConnector1,
                1);

            Grid.SetColumn(
                Step2Panel,
                2);

            Grid.SetColumn(
                StepConnector2,
                3);

            Grid.SetColumn(
                Step3Panel,
                4);

            Grid.SetColumn(
                StepConnector3,
                5);

            Grid.SetColumn(
                Step4Panel,
                6);
        }
    }


    // =========================================================
    // MODULE RTL / LTR
    // =========================================================

    private void ConfigureModuleDirection()
    {
        ConfigureModuleRow(
            StimuliModuleGrid,
            StimuliModuleTextPanel,
            UsesStimuliCheckBox);


        ConfigureModuleRow(
            QuestionnaireModuleGrid,
            QuestionnaireModuleTextPanel,
            UsesQuestionnaireCheckBox);


        ConfigureModuleRow(
            PhysiologyModuleGrid,
            PhysiologyModuleTextPanel,
            UsesPhysiologyCheckBox);


        PhysiologyOptionsPanel.FlowDirection =
            IsArabic()
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;


        PhysiologyOptionsPanel.HorizontalAlignment =
            HorizontalAlignment.Center;
    }


    private void ConfigureModuleRow(
        Grid grid,
        Control textPanel,
        Control checkBox)
    {
        if (IsArabic())
        {
            grid.ColumnDefinitions =
                new ColumnDefinitions(
                    "Auto,*");


            Grid.SetColumn(
                checkBox,
                0);


            Grid.SetColumn(
                textPanel,
                1);
        }
        else
        {
            grid.ColumnDefinitions =
                new ColumnDefinitions(
                    "*,Auto");


            Grid.SetColumn(
                textPanel,
                0);


            Grid.SetColumn(
                checkBox,
                1);
        }
    }


    // =========================================================
    // REVIEW RTL / LTR
    // =========================================================

    private void ConfigureReviewDirection()
    {
        ConfigureReviewRow(
            ReviewStudyRow,
            ReviewStudyLabel,
            ReviewStudyValue);


        ConfigureReviewRow(
            ReviewDesignRow,
            ReviewDesignLabel,
            ReviewDesignValue);


        ConfigureReviewRow(
            ReviewGroupsRow,
            ReviewGroupsLabel,
            ReviewGroupsValue);


        ConfigureReviewRow(
            ReviewSampleRow,
            ReviewSampleLabel,
            ReviewSampleValue);


        ConfigureReviewRow(
            ReviewModulesRow,
            ReviewModulesLabel,
            ReviewModulesValue);
    }


    private void ConfigureReviewRow(
        Grid grid,
        TextBlock label,
        TextBlock value)
    {
        if (IsArabic())
        {
            grid.ColumnDefinitions =
                new ColumnDefinitions(
                    "*,150");


            Grid.SetColumn(
                value,
                0);


            Grid.SetColumn(
                label,
                1);


            SetArabicRight(
                label);


            SetArabicRight(
                value);
        }
        else
        {
            grid.ColumnDefinitions =
                new ColumnDefinitions(
                    "150,*");


            Grid.SetColumn(
                label,
                0);


            Grid.SetColumn(
                value,
                1);


            SetEnglishLeft(
                label);


            SetEnglishLeft(
                value);
        }
    }


    // =========================================================
    // COMBOS
    // =========================================================

    private void ConfigureComboBoxes()
    {
        var currentStudyType =
            StudyTypeComboBox.SelectedIndex;


        var currentDesign =
            DesignTypeComboBox.SelectedIndex;


        var currentAssignment =
            AssignmentMethodComboBox.SelectedIndex;


        if (IsArabic())
        {
            StudyTypeComboBox.ItemsSource =
                new[]
                {
                    "تجريبية",
                    "رصدية",
                    "استبيان",
                    "مختلطة"
                };


            DesignTypeComboBox.ItemsSource =
                new[]
                {
                    "بين المجموعات",
                    "داخل المجموعة",
                    "تصميم مختلط",
                    "مجموعة واحدة"
                };


            AssignmentMethodComboBox.ItemsSource =
                new[]
                {
                    "يدوي",
                    "عشوائي",
                    "عشوائي متوازن",
                    "مستورد"
                };
        }
        else
        {
            StudyTypeComboBox.ItemsSource =
                new[]
                {
                    "Experimental",
                    "Observational",
                    "Survey",
                    "Mixed"
                };


            DesignTypeComboBox.ItemsSource =
                new[]
                {
                    "Between-subjects",
                    "Within-subjects",
                    "Mixed design",
                    "Single group"
                };


            AssignmentMethodComboBox.ItemsSource =
                new[]
                {
                    "Manual",
                    "Random",
                    "Balanced random",
                    "Imported"
                };
        }


        StudyTypeComboBox.SelectedIndex =
            currentStudyType >= 0
                ? currentStudyType
                : 0;


        DesignTypeComboBox.SelectedIndex =
            currentDesign >= 0
                ? currentDesign
                : 0;


        AssignmentMethodComboBox.SelectedIndex =
            currentAssignment >= 0
                ? currentAssignment
                : 0;
    }


    // =========================================================
    // CANONICAL VALUES
    // =========================================================

    private string GetStudyType()
    {
        return StudyTypeComboBox.SelectedIndex switch
        {
            1 => "Observational",
            2 => "Survey",
            3 => "Mixed",
            _ => "Experimental"
        };
    }


    private string GetDesignType()
    {
        return DesignTypeComboBox.SelectedIndex switch
        {
            1 => "WithinSubjects",
            2 => "Mixed",
            3 => "SingleGroup",
            _ => "BetweenSubjects"
        };
    }


    private string GetAssignmentMethod()
    {
        return AssignmentMethodComboBox.SelectedIndex switch
        {
            1 => "Random",
            2 => "BalancedRandom",
            3 => "Imported",
            _ => "Manual"
        };
    }


    private string GetArabicDesignSummary()
    {
        return DesignTypeComboBox.SelectedIndex switch
        {
            1 => "داخل المجموعة",
            2 => "تصميم مختلط",
            3 => "مجموعة واحدة",
            _ => "بين المجموعات"
        };
    }


    private string GetEnglishDesignSummary()
    {
        return DesignTypeComboBox.SelectedIndex switch
        {
            1 => "Within-subjects",
            2 => "Mixed design",
            3 => "Single group",
            _ => "Between-subjects"
        };
    }


    // =========================================================
    // ERROR
    // =========================================================

    private void ShowError(
        string message)
    {
        BuilderErrorText.Text =
            message;


        BuilderErrorText.FontFamily =
            IsArabic()
                ? _arabicFont
                : _englishFont;


        BuilderErrorText.FlowDirection =
            IsArabic()
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;


        BuilderErrorText.TextAlignment =
            IsArabic()
                ? TextAlignment.Right
                : TextAlignment.Left;


        BuilderErrorText.HorizontalAlignment =
            HorizontalAlignment.Stretch;


        BuilderErrorText.IsVisible =
            true;
    }


    private void ClearError()
    {
        BuilderErrorText.Text =
            string.Empty;


        BuilderErrorText.IsVisible =
            false;
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


    private static string? NormalizeOptional(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }


        return value.Trim();
    }


    private void SetArabicRight(
        TextBlock textBlock,
        string text)
    {
        textBlock.Text =
            text;


        SetArabicRight(
            textBlock);
    }


    private void SetArabicRight(
        TextBlock textBlock)
    {
        textBlock.FontFamily =
            _arabicFont;


        textBlock.FlowDirection =
            FlowDirection.RightToLeft;


        textBlock.TextAlignment =
            TextAlignment.Right;


        textBlock.HorizontalAlignment =
            HorizontalAlignment.Right;
    }


    private void SetArabicCenter(
        TextBlock textBlock,
        string text)
    {
        textBlock.Text =
            text;


        SetArabicCenter(
            textBlock);
    }


    private void SetArabicCenter(
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


    private void SetEnglishLeft(
        TextBlock textBlock,
        string text)
    {
        textBlock.Text =
            text;


        SetEnglishLeft(
            textBlock);
    }


    private void SetEnglishLeft(
        TextBlock textBlock)
    {
        textBlock.FontFamily =
            _englishFont;


        textBlock.FlowDirection =
            FlowDirection.LeftToRight;


        textBlock.TextAlignment =
            TextAlignment.Left;


        textBlock.HorizontalAlignment =
            HorizontalAlignment.Left;
    }


    private void SetEnglishCenter(
        TextBlock textBlock,
        string text)
    {
        textBlock.Text =
            text;


        SetEnglishCenter(
            textBlock);
    }


    private void SetEnglishCenter(
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


    private void ConfigureArabicTextBox(
        TextBox textBox,
        string placeholder)
    {
        textBox.FontFamily =
            _arabicFont;


        textBox.FlowDirection =
            FlowDirection.RightToLeft;


        textBox.TextAlignment =
            TextAlignment.Right;


        textBox.PlaceholderText =
            placeholder;
    }


    private void ConfigureEnglishTextBox(
        TextBox textBox,
        string placeholder)
    {
        textBox.FontFamily =
            _englishFont;


        textBox.FlowDirection =
            FlowDirection.LeftToRight;


        textBox.TextAlignment =
            TextAlignment.Left;


        textBox.PlaceholderText =
            placeholder;
    }
}
