using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SOCYVIA.Models;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

public partial class LoginView : UserControl
{
    public event EventHandler<ResearcherProfile>? LoginSucceeded;


    private bool _isEnglish;

    private bool _isNewResearcherMode;

    private bool _clearResearchersConfirmationPending;


    private List<ResearcherProfile> _profiles =
        new();


    private readonly FontFamily _englishFont =
        new("avares://SOCYVIA/Assets/Fonts#IBM Plex Sans");


    private readonly FontFamily _arabicFont =
        new("avares://SOCYVIA/Assets/Fonts#IBM Plex Sans Arabic");


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public LoginView()
    {
        InitializeComponent();


        ResearcherService.Initialize();


        _isEnglish =
            LocalizationService.CurrentLanguage ==
            AppLanguage.English;


        SetupEvents();

        LoadResearcherProfiles();

        ApplyLanguage();

        UpdatePasswordPlaceholder();

        UpdateModeVisuals();
    }


    // =========================================================
    // EVENTS
    // =========================================================

    private void SetupEvents()
    {
        ExistingModeButton.Click +=
            (_, _) =>
            {
                SetMode(false);
            };


        NewModeButton.Click +=
            (_, _) =>
            {
                SetMode(true);
            };


        EnterWorkspaceButton.Click +=
            (_, _) =>
            {
                TryEnterWorkspace();
            };


        EnglishLanguageButton.Click +=
            (_, _) =>
            {
                _isEnglish =
                    true;


                LocalizationService.SetLanguage(
                    AppLanguage.English);


                ResetClearResearchersConfirmation();

                ApplyLanguage();

                UpdatePasswordPlaceholder();
            };


        ArabicLanguageButton.Click +=
            (_, _) =>
            {
                _isEnglish =
                    false;


                LocalizationService.SetLanguage(
                    AppLanguage.Arabic);


                ResetClearResearchersConfirmation();

                ApplyLanguage();

                UpdatePasswordPlaceholder();
            };


        PasswordBox.TextChanged +=
            (_, _) =>
            {
                UpdatePasswordPlaceholder();
            };


        ResearcherProfileComboBox.SelectionChanged +=
            (_, _) =>
            {
                UpdateResearcherComboWidth();

                ResetClearResearchersConfirmation();
            };


        TogglePasswordButton.PointerPressed +=
            (_, _) =>
            {
                PasswordBox.RevealPassword =
                    !PasswordBox.RevealPassword;


                PasswordEyeIcon.Stroke =
                    new SolidColorBrush(
                        Color.Parse(
                            PasswordBox.RevealPassword
                                ? "#2563EB"
                                : "#8B97AB"));
            };


        ClearSavedResearchersButton.Click +=
            (_, _) =>
            {
                HandleClearSavedResearchers();
            };
    }


    // =========================================================
    // MODE
    // =========================================================

    private void SetMode(
        bool newResearcher)
    {
        _isNewResearcherMode =
            newResearcher;


        PasswordBox.Text =
            string.Empty;


        ConfirmPasswordBox.Text =
            string.Empty;


        RememberMeCheckBox.IsChecked =
            false;


        PrivacyCheckBox.IsChecked =
            false;


        ResetClearResearchersConfirmation();

        ClearError();

        UpdateModeVisuals();

        ApplyLanguage();

        UpdatePasswordPlaceholder();
    }


    private void UpdateModeVisuals()
    {
        ExistingModeButton.Classes.Remove(
            "selected");


        NewModeButton.Classes.Remove(
            "selected");


        if (_isNewResearcherMode)
        {
            NewModeButton.Classes.Add(
                "selected");


            ExistingResearcherPanel.IsVisible =
                false;


            NewResearcherPanel.IsVisible =
                true;


            ConfirmPasswordLabelText.IsVisible =
                true;


            ConfirmPasswordBox.IsVisible =
                true;


            PrivacyCheckBox.IsVisible =
                true;
        }
        else
        {
            ExistingModeButton.Classes.Add(
                "selected");


            ExistingResearcherPanel.IsVisible =
                true;


            NewResearcherPanel.IsVisible =
                false;


            ConfirmPasswordLabelText.IsVisible =
                false;


            ConfirmPasswordBox.IsVisible =
                false;


            PrivacyCheckBox.IsVisible =
                false;


            UpdateResearcherComboWidth();
        }
    }


    // =========================================================
    // CLEAR SAVED RESEARCHERS
    // =========================================================

    private void HandleClearSavedResearchers()
    {
        if (_profiles.Count == 0)
        {
            return;
        }


        if (!_clearResearchersConfirmationPending)
        {
            _clearResearchersConfirmationPending =
                true;


            ClearSavedResearchersText.Text =
                IsArabic()
                    ? "اضغط مرة أخرى لتأكيد المسح"
                    : "Click again to confirm removal";


            ClearSavedResearchersButton.Foreground =
                new SolidColorBrush(
                    Color.Parse("#D84A5B"));


            return;
        }


        var success =
            ResearcherService.ClearAllResearchers();


        if (!success)
        {
            ShowError(
                IsArabic()
                    ? "تعذر مسح الباحثين المحفوظين"
                    : "Saved researchers could not be removed");


            ResetClearResearchersConfirmation();

            return;
        }


        _profiles.Clear();


        ResearcherProfileComboBox.ItemsSource =
            Array.Empty<string>();


        ResearcherProfileComboBox.SelectedIndex =
            -1;


        ResearcherProfileComboBox.Width =
            150;


        _isNewResearcherMode =
            true;


        PasswordBox.Text =
            string.Empty;


        ConfirmPasswordBox.Text =
            string.Empty;


        RememberMeCheckBox.IsChecked =
            false;


        PrivacyCheckBox.IsChecked =
            false;


        ResetClearResearchersConfirmation();

        ClearError();

        UpdateModeVisuals();

        ApplyLanguage();

        UpdatePasswordPlaceholder();
    }


    private void ResetClearResearchersConfirmation()
    {
        _clearResearchersConfirmationPending =
            false;


        if (ClearSavedResearchersText is null)
        {
            return;
        }


        ClearSavedResearchersText.Text =
            IsArabic()
                ? "مسح الباحثين المحفوظين"
                : "Clear saved researchers";


        ClearSavedResearchersButton.Foreground =
            new SolidColorBrush(
                Color.Parse("#A15C67"));
    }


    // =========================================================
    // RESEARCHER COMBO WIDTH
    // =========================================================

    private void UpdateResearcherComboWidth()
    {
        var index =
            ResearcherProfileComboBox
                .SelectedIndex;


        if (index < 0 ||
            index >= _profiles.Count)
        {
            ResearcherProfileComboBox.Width =
                150;

            return;
        }


        var name =
            _profiles[index]
                .FullName;


        var estimatedWidth =
            112 +
            (name.Length * 7.2);


        ResearcherProfileComboBox.Width =
            Math.Clamp(
                estimatedWidth,
                150,
                290);
    }


    // =========================================================
    // PASSWORD PLACEHOLDER
    // =========================================================

    private void UpdatePasswordPlaceholder()
    {
        PasswordPlaceholderText.IsVisible =
            string.IsNullOrEmpty(
                PasswordBox.Text);
    }


    // =========================================================
    // LOAD RESEARCHERS
    // =========================================================

    private void LoadResearcherProfiles()
    {
        _profiles =
            ResearcherService.GetProfiles();


        ResearcherProfileComboBox.ItemsSource =
            _profiles
                .Select(
                    profile =>
                        profile.FullName)
                .ToList();


        if (_profiles.Count == 0)
        {
            _isNewResearcherMode =
                true;


            UpdateModeVisuals();

            return;
        }


        var activeId =
            ResearcherService
                .GetActiveResearcherId();


        var activeIndex =
            _profiles.FindIndex(
                profile =>
                    profile.Id == activeId);


        ResearcherProfileComboBox.SelectedIndex =
            activeIndex >= 0
                ? activeIndex
                : 0;


        _isNewResearcherMode =
            false;


        UpdateResearcherComboWidth();

        UpdateModeVisuals();
    }


    // =========================================================
    // ENTER
    // =========================================================

    private void TryEnterWorkspace()
    {
        ClearError();


        if (_isNewResearcherMode)
        {
            CreateNewResearcher();

            return;
        }


        EnterExistingResearcher();
    }


    // =========================================================
    // EXISTING RESEARCHER
    // =========================================================

    private void EnterExistingResearcher()
    {
        var index =
            ResearcherProfileComboBox
                .SelectedIndex;


        if (index < 0 ||
            index >= _profiles.Count)
        {
            ShowError(
                IsArabic()
                    ? "اختر الباحث الذي تريد الدخول إلى مساحته"
                    : "Choose the researcher workspace you want to open");


            return;
        }


        var profile =
            _profiles[index];


        if (!ResearcherService.VerifyPassword(
                profile,
                PasswordBox.Text))
        {
            ShowError(
                IsArabic()
                    ? "كلمة المرور غير صحيحة"
                    : "Incorrect password");


            PasswordBox.Focus();

            return;
        }


        var remember =
            RememberMeCheckBox.IsChecked ==
            true;


        ResearcherService.UpdateLastAccess(
            profile,
            remember);


        LoginSucceeded?.Invoke(
            this,
            profile);
    }


    // =========================================================
    // CREATE NEW RESEARCHER
    // =========================================================

    private void CreateNewResearcher()
    {
        var fullName =
            ResearcherNameBox.Text?
                .Trim()
            ?? string.Empty;


        var password =
            PasswordBox.Text
            ?? string.Empty;


        var confirmPassword =
            ConfirmPasswordBox.Text
            ?? string.Empty;


        // =====================================================
        // NAME
        // =====================================================

        if (string.IsNullOrWhiteSpace(
                fullName))
        {
            ShowError(
                IsArabic()
                    ? "أدخل اسم الباحث قبل المتابعة"
                    : "Enter the researcher name before continuing");


            ResearcherNameBox.Focus();

            return;
        }


        // =====================================================
        // PRIVACY
        // =====================================================

        if (PrivacyCheckBox.IsChecked != true)
        {
            ShowError(
                IsArabic()
                    ? "يجب الموافقة على سياسة البيانات والخصوصية قبل إنشاء مساحة البحث"
                    : "You must agree to the Research Data and Privacy Policy before creating the workspace");


            PrivacyCheckBox.Focus();

            return;
        }


        // =====================================================
        // PASSWORD CONFIRMATION
        // =====================================================

        if (!string.Equals(
                password,
                confirmPassword,
                StringComparison.Ordinal))
        {
            ShowError(
                IsArabic()
                    ? "كلمتا المرور غير متطابقتين"
                    : "The passwords do not match");


            ConfirmPasswordBox.Focus();

            return;
        }


        // =====================================================
        // CREATE
        // =====================================================

        var remember =
            RememberMeCheckBox.IsChecked ==
            true;


        var profile =
            ResearcherService.CreateProfile(
                fullName,
                password,
                remember,
                privacyAccepted: true);


        LoginSucceeded?.Invoke(
            this,
            profile);
    }


    // =========================================================
    // ERRORS
    // =========================================================

    private void ShowError(
        string message)
    {
        LoginErrorText.Text =
            message;


        LoginErrorText.IsVisible =
            true;


        LoginErrorText.FontFamily =
            IsArabic()
                ? _arabicFont
                : _englishFont;


        LoginErrorText.FlowDirection =
            IsArabic()
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;


        LoginErrorText.TextAlignment =
            IsArabic()
                ? TextAlignment.Right
                : TextAlignment.Left;


        LoginErrorText.HorizontalAlignment =
            HorizontalAlignment.Stretch;
    }


    private void ClearError()
    {
        LoginErrorText.Text =
            string.Empty;


        LoginErrorText.IsVisible =
            false;
    }


    // =========================================================
    // LANGUAGE
    // =========================================================

    private void ApplyLanguage()
    {
        if (IsArabic())
        {
            ApplyArabic();
        }
        else
        {
            ApplyEnglish();
        }
    }


    private bool IsArabic()
    {
        return !_isEnglish;
    }


    // =========================================================
    // ARABIC
    // =========================================================

    private void ApplyArabic()
    {
        RootLogin.FontFamily =
            _arabicFont;


        // BRAND

        BrandSubtitleText.Text =
            SocyviaProductIdentity.ArabicPositioning;
        BrandSubtitleText.FontFamily = _arabicFont;
        BrandSubtitleText.FontSize = 12.5;
        BrandSubtitleText.FlowDirection = FlowDirection.RightToLeft;
        BrandSubtitleText.TextAlignment = TextAlignment.Center;


        // HERO

        HeroAccentLine.HorizontalAlignment =
            HorizontalAlignment.Center;


        HeroLine1Text.Text =
            "صمم التجارب • قس السلوك • تفحص الأدلة";

        HeroLine1Text.FontSize = 11.5;


        HeroLine2Text.Text =
            "تجربة المشارك ← البيانات";


        LoginDescriptionText.Text =
            "";


        ConfigureArabicText(
            HeroLine1Text);


        ConfigureArabicText(
            HeroLine2Text);


        ConfigureArabicText(
            LoginDescriptionText);

        HeroLine1Text.TextAlignment = TextAlignment.Center;
        HeroLine2Text.TextAlignment = TextAlignment.Center;
        LoginDescriptionText.TextAlignment = TextAlignment.Center;


        FeatureResearchDesignText.Text =
            "تصميم الدراسات وإدارة المشاركين";


        FeatureDigitalInteractionText.Text =
            "التفاعل الرقمي والبيانات السلوكية";


        FeatureAnalysisText.Text =
            "التحليل والتصور البصري والتقارير";


        FeatureResearchDesignRow.FlowDirection =
            FlowDirection.RightToLeft;


        FeatureDigitalInteractionRow.FlowDirection =
            FlowDirection.RightToLeft;


        FeatureAnalysisRow.FlowDirection =
            FlowDirection.RightToLeft;


        FeatureResearchDesignRow.HorizontalAlignment =
            HorizontalAlignment.Right;


        FeatureDigitalInteractionRow.HorizontalAlignment =
            HorizontalAlignment.Right;


        FeatureAnalysisRow.HorizontalAlignment =
            HorizontalAlignment.Right;


        // LANGUAGE

        LanguageContentPanel.FlowDirection =
            FlowDirection.RightToLeft;


        LanguageLabelText.Text =
            "اللغة";


        ArabicLanguageText.Text =
            "العربية";


        EnglishLanguageText.Text =
            "الإنجليزية";


        ArabicUnderline.Opacity =
            1;


        EnglishUnderline.Opacity =
            0;


        // CARD

        CardAccentLine.HorizontalAlignment =
            HorizontalAlignment.Right;


        ResearcherAccessTitleText.Text =
            "مساحة الباحث";


        ResearcherAccessSubtitleText.Text =
            "اختر باحثا محفوظا على هذا الجهاز أو أنشئ مساحة بحث جديدة";


        ConfigureArabicText(
            ResearcherAccessTitleText);


        ConfigureArabicText(
            ResearcherAccessSubtitleText);


        // MODES

        // Grid columns remain physically stable: registered researcher is on the RTL right.
        ResearcherModePanel.FlowDirection =
            FlowDirection.LeftToRight;
        Grid.SetColumn(ExistingModeButton, 2);
        Grid.SetColumn(NewModeButton, 0);


        ExistingModeText.Text =
            "باحث مسجل";


        NewModeText.Text =
            "باحث جديد";


        ExistingModeText.FontFamily =
            _arabicFont;


        NewModeText.FontFamily =
            _arabicFont;


        // SAVED RESEARCHER

        SavedResearcherLabel.Text =
            "الباحث";


        SavedResearcherLabel.FontFamily =
            _arabicFont;


        SavedResearcherLabel.FlowDirection =
            FlowDirection.RightToLeft;


        ResearcherProfileComboBox.FlowDirection =
            FlowDirection.RightToLeft;


        ResearcherProfileComboBox.HorizontalContentAlignment =
            HorizontalAlignment.Center;


        ClearSavedResearchersText.Text =
            _clearResearchersConfirmationPending
                ? "اضغط مرة أخرى لتأكيد المسح"
                : "مسح الباحثين المحفوظين";


        ClearSavedResearchersText.FontFamily =
            _arabicFont;


        // NEW RESEARCHER

        ResearcherNameLabelText.Text =
            "اسم الباحث";


        ConfigureArabicText(
            ResearcherNameLabelText);


        ResearcherNameBox.PlaceholderText =
            "أدخل اسم الباحث";


        ResearcherNameBox.FontFamily =
            _arabicFont;


        ResearcherNameBox.FlowDirection =
            FlowDirection.RightToLeft;


        ResearcherNameBox.TextAlignment =
            TextAlignment.Right;


        // PASSWORD HEADER

        PasswordHeader.FlowDirection =
            FlowDirection.LeftToRight;


        Grid.SetColumn(
            OptionalText,
            0);


        Grid.SetColumn(
            PasswordLabelText,
            1);


        PasswordLabelText.Text =
            "كلمة المرور";


        PasswordLabelText.FontFamily =
            _arabicFont;


        PasswordLabelText.FlowDirection =
            FlowDirection.RightToLeft;


        PasswordLabelText.TextAlignment =
            TextAlignment.Right;


        PasswordLabelText.HorizontalAlignment =
            HorizontalAlignment.Right;


        OptionalText.Text =
            _isNewResearcherMode
                ? "اختيارية"
                : string.Empty;


        OptionalText.FontFamily =
            _arabicFont;


        OptionalText.FlowDirection =
            FlowDirection.RightToLeft;


        OptionalText.HorizontalAlignment =
            HorizontalAlignment.Left;


        // PASSWORD INPUT

        PasswordPlaceholderText.Text =
            "أدخل كلمة المرور";


        PasswordPlaceholderText.FontFamily =
            _arabicFont;


        PasswordPlaceholderText.FlowDirection =
            FlowDirection.RightToLeft;


        PasswordPlaceholderText.TextAlignment =
            TextAlignment.Right;


        PasswordPlaceholderText.HorizontalAlignment =
            HorizontalAlignment.Stretch;


        PasswordPlaceholderText.Margin =
            new Thickness(
                44,
                0,
                13,
                0);


        PasswordBox.Padding =
            new Thickness(
                44,
                0,
                13,
                0);


        PasswordBox.FontFamily =
            _arabicFont;


        PasswordBox.FlowDirection =
            FlowDirection.RightToLeft;


        PasswordBox.TextAlignment =
            TextAlignment.Right;


        TogglePasswordButton.HorizontalAlignment =
            HorizontalAlignment.Left;


        // CONFIRM

        ConfirmPasswordLabelText.Text =
            "تأكيد كلمة المرور";


        ConfigureArabicText(
            ConfirmPasswordLabelText);


        ConfirmPasswordBox.PlaceholderText =
            "أعد كتابة كلمة المرور";


        ConfirmPasswordBox.FontFamily =
            _arabicFont;


        ConfirmPasswordBox.FlowDirection =
            FlowDirection.RightToLeft;


        ConfirmPasswordBox.TextAlignment =
            TextAlignment.Right;


        // REMEMBER

        RememberMeText.Text =
            "تذكر هذا الباحث على هذا الجهاز";


        RememberMeText.FontFamily =
            _arabicFont;


        RememberMeCheckBox.FlowDirection =
            FlowDirection.RightToLeft;


        RememberMeCheckBox.HorizontalAlignment =
            HorizontalAlignment.Right;


        // PRIVACY

        PrivacyText.Text =
            "أوافق على سياسة البيانات والخصوصية";


        PrivacyText.FontFamily =
            _arabicFont;


        PrivacyText.FlowDirection =
            FlowDirection.RightToLeft;


        PrivacyCheckBox.FlowDirection =
            FlowDirection.RightToLeft;


        PrivacyCheckBox.HorizontalAlignment =
            HorizontalAlignment.Right;


        // CTA

        EnterWorkspaceText.Text =
            _isNewResearcherMode
                ? "إنشاء مساحة البحث"
                : "تسجيل الدخول إلى مساحة البحث";


        EnterWorkspaceText.FontFamily =
            _arabicFont;
    }


    // =========================================================
    // ENGLISH
    // =========================================================

    private void ApplyEnglish()
    {
        RootLogin.FontFamily =
            _englishFont;


        // BRAND

        BrandSubtitleText.Text =
            SocyviaProductIdentity.EnglishPositioning;
        BrandSubtitleText.FontFamily = _englishFont;
        // The English copy is longer, so only these two approved brand-copy
        // lines receive a minimal optical adjustment inside the shared rows.
        BrandSubtitleText.FontSize = 12;
        BrandSubtitleText.FlowDirection = FlowDirection.LeftToRight;
        BrandSubtitleText.TextAlignment = TextAlignment.Center;


        // HERO

        HeroAccentLine.HorizontalAlignment =
            HorizontalAlignment.Center;


        HeroLine1Text.Text =
            "Design Experiments • Measure Behavior • Examine Evidence";

        HeroLine1Text.FontSize = 10.8;


        HeroLine2Text.Text =
            "Participant experience → Data";


        LoginDescriptionText.Text =
            "";


        ConfigureEnglishText(
            HeroLine1Text);


        ConfigureEnglishText(
            HeroLine2Text);


        ConfigureEnglishText(
            LoginDescriptionText);

        HeroLine1Text.TextAlignment = TextAlignment.Center;
        HeroLine2Text.TextAlignment = TextAlignment.Center;
        LoginDescriptionText.TextAlignment = TextAlignment.Center;


        FeatureResearchDesignText.Text =
            "Research design and participant management";


        FeatureDigitalInteractionText.Text =
            "Digital interaction and behavioral data";


        FeatureAnalysisText.Text =
            "Analysis, visualisation and research reporting";


        FeatureResearchDesignRow.FlowDirection =
            FlowDirection.LeftToRight;


        FeatureDigitalInteractionRow.FlowDirection =
            FlowDirection.LeftToRight;


        FeatureAnalysisRow.FlowDirection =
            FlowDirection.LeftToRight;


        FeatureResearchDesignRow.HorizontalAlignment =
            HorizontalAlignment.Left;


        FeatureDigitalInteractionRow.HorizontalAlignment =
            HorizontalAlignment.Left;


        FeatureAnalysisRow.HorizontalAlignment =
            HorizontalAlignment.Left;


        // LANGUAGE

        LanguageContentPanel.FlowDirection =
            FlowDirection.LeftToRight;


        LanguageLabelText.Text =
            "Language";


        EnglishLanguageText.Text =
            "English";


        ArabicLanguageText.Text =
            "Arabic";


        EnglishUnderline.Opacity =
            1;


        ArabicUnderline.Opacity =
            0;


        // CARD

        CardAccentLine.HorizontalAlignment =
            HorizontalAlignment.Left;


        ResearcherAccessTitleText.Text =
            "Researcher Workspace";


        ResearcherAccessSubtitleText.Text =
            "Choose a saved researcher or create a new workspace";


        ConfigureEnglishText(
            ResearcherAccessTitleText);


        ConfigureEnglishText(
            ResearcherAccessSubtitleText);


        // MODES

        ResearcherModePanel.FlowDirection =
            FlowDirection.LeftToRight;
        Grid.SetColumn(ExistingModeButton, 0);
        Grid.SetColumn(NewModeButton, 2);


        ExistingModeText.Text =
            "Registered Researcher";


        NewModeText.Text =
            "New Researcher";


        ExistingModeText.FontFamily =
            _englishFont;


        NewModeText.FontFamily =
            _englishFont;


        // SAVED RESEARCHER

        SavedResearcherLabel.Text =
            "Researcher";


        SavedResearcherLabel.FontFamily =
            _englishFont;


        SavedResearcherLabel.FlowDirection =
            FlowDirection.LeftToRight;


        ResearcherProfileComboBox.FlowDirection =
            FlowDirection.LeftToRight;


        ResearcherProfileComboBox.HorizontalContentAlignment =
            HorizontalAlignment.Center;


        ClearSavedResearchersText.Text =
            _clearResearchersConfirmationPending
                ? "Click again to confirm removal"
                : "Clear saved researchers";


        ClearSavedResearchersText.FontFamily =
            _englishFont;


        // NEW RESEARCHER

        ResearcherNameLabelText.Text =
            "Researcher name";


        ConfigureEnglishText(
            ResearcherNameLabelText);


        ResearcherNameBox.PlaceholderText =
            "Enter your full name";


        ResearcherNameBox.FontFamily =
            _englishFont;


        ResearcherNameBox.FlowDirection =
            FlowDirection.LeftToRight;


        ResearcherNameBox.TextAlignment =
            TextAlignment.Left;


        // PASSWORD HEADER

        PasswordHeader.FlowDirection =
            FlowDirection.LeftToRight;


        Grid.SetColumn(
            PasswordLabelText,
            0);


        Grid.SetColumn(
            OptionalText,
            1);


        PasswordLabelText.Text =
            "Password";


        ConfigureEnglishText(
            PasswordLabelText);


        OptionalText.Text =
            _isNewResearcherMode
                ? "Optional"
                : string.Empty;


        OptionalText.FontFamily =
            _englishFont;


        OptionalText.FlowDirection =
            FlowDirection.LeftToRight;


        OptionalText.HorizontalAlignment =
            HorizontalAlignment.Right;


        // PASSWORD INPUT

        PasswordPlaceholderText.Text =
            "Enter password";


        PasswordPlaceholderText.FontFamily =
            _englishFont;


        PasswordPlaceholderText.FlowDirection =
            FlowDirection.LeftToRight;


        PasswordPlaceholderText.TextAlignment =
            TextAlignment.Left;


        PasswordPlaceholderText.HorizontalAlignment =
            HorizontalAlignment.Stretch;


        PasswordPlaceholderText.Margin =
            new Thickness(
                13,
                0,
                44,
                0);


        PasswordBox.Padding =
            new Thickness(
                13,
                0,
                44,
                0);


        PasswordBox.FontFamily =
            _englishFont;


        PasswordBox.FlowDirection =
            FlowDirection.LeftToRight;


        PasswordBox.TextAlignment =
            TextAlignment.Left;


        TogglePasswordButton.HorizontalAlignment =
            HorizontalAlignment.Right;


        // CONFIRM

        ConfirmPasswordLabelText.Text =
            "Confirm password";


        ConfigureEnglishText(
            ConfirmPasswordLabelText);


        ConfirmPasswordBox.PlaceholderText =
            "Repeat password";


        ConfirmPasswordBox.FontFamily =
            _englishFont;


        ConfirmPasswordBox.FlowDirection =
            FlowDirection.LeftToRight;


        ConfirmPasswordBox.TextAlignment =
            TextAlignment.Left;


        // REMEMBER

        RememberMeText.Text =
            "Remember this researcher on this device";


        RememberMeText.FontFamily =
            _englishFont;


        RememberMeCheckBox.FlowDirection =
            FlowDirection.LeftToRight;


        RememberMeCheckBox.HorizontalAlignment =
            HorizontalAlignment.Left;


        // PRIVACY

        PrivacyText.Text =
            "I agree to the Research Data and Privacy Policy";


        PrivacyText.FontFamily =
            _englishFont;


        PrivacyText.FlowDirection =
            FlowDirection.LeftToRight;


        PrivacyCheckBox.FlowDirection =
            FlowDirection.LeftToRight;


        PrivacyCheckBox.HorizontalAlignment =
            HorizontalAlignment.Left;


        // CTA

        EnterWorkspaceText.Text =
            _isNewResearcherMode
                ? "Create Workspace"
                : "Enter Workspace";


        EnterWorkspaceText.FontFamily =
            _englishFont;
    }


    // =========================================================
    // HELPERS
    // =========================================================

    private void ConfigureArabicText(
        TextBlock textBlock)
    {
        textBlock.FontFamily =
            _arabicFont;


        textBlock.FlowDirection =
            FlowDirection.RightToLeft;


        textBlock.TextAlignment =
            TextAlignment.Right;


        textBlock.HorizontalAlignment =
            HorizontalAlignment.Stretch;
    }


    private void ConfigureEnglishText(
        TextBlock textBlock)
    {
        textBlock.FontFamily =
            _englishFont;


        textBlock.FlowDirection =
            FlowDirection.LeftToRight;


        textBlock.TextAlignment =
            TextAlignment.Left;


        textBlock.HorizontalAlignment =
            HorizontalAlignment.Stretch;
    }
}
