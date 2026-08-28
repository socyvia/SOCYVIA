using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using SOCYVIA.Models;
using SOCYVIA.Repositories;
using SOCYVIA.Services;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace SOCYVIA.Views;

public partial class DashboardView : UserControl
{
    public event EventHandler? LogoutRequested;


    private ResearcherProfile? _researcher;


    private StudyBuilderView? _studyBuilderView;


    private StudyWorkspaceView? _studyWorkspaceView;


    private StudiesView? _studiesView;

    private ContentLibraryView? _contentLibraryView;
    private GuidedDemoView? _guidedDemoView;
    private SocyviaAiProductHelpView? _socyviaAiProductHelpView;


    private Study? _studyBeingEdited;

    private Study? _continueStudy;
    private string? _continueDestination;
    private string? _placeholderDestination;


    private readonly FontFamily _englishFont =
        new("avares://SOCYVIA/Assets/Fonts#IBM Plex Sans");


    private readonly FontFamily _arabicFont =
        new("avares://SOCYVIA/Assets/Fonts#IBM Plex Sans Arabic");

    private ConnectivityState _connectivityState = ConnectivityState.Checking;
    private bool _completingCloudflareOAuth;


    // =========================================================
    // CONSTRUCTORS
    // =========================================================

    public DashboardView()
    {
        InitializeComponent();

        SetupNavigation();

        SetupProfileMenu();

        SetupSettings();

        SetupConnectivity();

        SetupFooterLinks();

        ConfigureFooter();
    }


    public DashboardView(
        ResearcherProfile researcher)
        : this()
    {
        _researcher =
            researcher;

        ConfigureResearcher();

        ConfigureLanguage();

        AttachedToVisualTree +=
            async (_, _) =>
            {
                CloudflareOAuthCallbackInbox.CallbackCaptured += OnCloudflareCallbackCaptured;
                ConnectivityService.StateChanged += OnConnectivityStateChanged;
                ConnectivityService.StartMonitoring();
                ApplyConnectivitySnapshot(ConnectivityService.Current);
                await LoadDashboardDataAsync();
                await HandleCloudflareCallbackAsync();
                await ShowFirstRunOnboardingIfNeededAsync();
            };

        DetachedFromVisualTree += (_, _) =>
        {
            CloudflareOAuthCallbackInbox.CallbackCaptured -= OnCloudflareCallbackCaptured;
            ConnectivityService.StateChanged -= OnConnectivityStateChanged;
            ConnectivityService.StopMonitoring();
        };
    }


    // =========================================================
    // PROFILE
    // =========================================================

    private void ConfigureResearcher()
    {
        if (_researcher is null)
        {
            return;
        }

        ResearcherNameText.Text =
            _researcher.FullName;

        ProfileMenuNameText.Text =
            _researcher.FullName;

        ResearcherInitialsText.Text =
            GetInitials(
                _researcher.FullName);

        WelcomeNameText.Text =
            _researcher.FullName;
    }


    private void SetupProfileMenu()
    {
        LogoutButton.Click +=
            (_, _) =>
            {
                LogoutRequested?.Invoke(
                    this,
                    EventArgs.Empty);
            };

        ProfileSettingsButton.Click +=
            (_, _) =>
            {
                ShowSettings();
            };
    }


    private void SetupSettings()
    {
        SettingsEnglishButton.Click += async (_, _) =>
        {
            LocalizationService.SetLanguage(AppLanguage.English);
            ConfigureLanguage();
            await LoadDashboardDataAsync();
        };
        SettingsArabicButton.Click += async (_, _) =>
        {
            LocalizationService.SetLanguage(AppLanguage.Arabic);
            ConfigureLanguage();
            await LoadDashboardDataAsync();
        };
        ReplayTourButton.Click += async (_, _) =>
            await ShowOnboardingAsync();
        CloudSaveButton.Click += async (_, _) => await SaveCloudConfigurationAsync();
        CloudTestButton.Click += async (_, _) => await TestCloudConnectionAsync();
        CloudDisconnectButton.Click += async (_, _) => await DisconnectCloudAsync();
        CloudDisconnectQuickButton.Click += async (_, _) => await DisconnectCloudAsync();
        CloudConnectButton.Click += async (_, _) => await StartCloudflareConnectionAsync();
        CloudRetryButton.Click += async (_, _) => await TestCloudConnectionAsync();
        CloudMediaSetupButton.Click += async (_, _) => await SetupCloudflareMediaStorageAsync();
        CloudAdvancedSetupButton.Click += (_, _) =>
            CloudAdvancedPanel.IsVisible = !CloudAdvancedPanel.IsVisible;
        AiRefreshButton.Click += async (_, _) => await LoadAiServiceStatusAsync();
        CheckUpdatesButton.Click += (_, _) => ShowUpdateReadiness();
    }

    private async Task StartCloudflareConnectionAsync()
    {
        var reachability = await ConnectivityService.CheckAsync();
        if (reachability.State != ConnectivityState.Online)
        {
            SettingsCloudState.Text = Text("غير متصل", "Offline");
            CloudConnectionDetail.Text = Text(
                "يتطلب ربط Cloudflare اتصالا بالإنترنت. يظل SOCYVIA متاحا للعمل المحلي.",
                "Connecting Cloudflare requires internet access. SOCYVIA remains available for local work.");
            return;
        }
        var configuration = CloudflareOAuthClientConfiguration.LoadReleaseConfiguration();
        var request = await new CloudflareOAuthConnectionService().BeginAsync(configuration);
        if (request is null)
        {
            CloudConnectionDetail.Text = Text(
                "يتطلب ربط Cloudflare تسجيل عميل OAuth رسمي ونطاقات صلاحية موثقة قبل الإصدار. يظل الإعداد المتقدم الآمن متاحا مؤقتا.",
                "Cloudflare connection requires SOCYVIA's registered OAuth client and verified scope IDs before release. Secure Advanced Setup remains available in the meantime.");
            return;
        }

        try
        {
            SettingsCloudState.Text = Text("في انتظار التفويض", "Awaiting authorization");
            CloudConnectionDetail.Text = Text(
                "أكمل التفويض في متصفحك. لن يعرض SOCYVIA رمز التفويض أو بيانات الاعتماد.",
                "Complete authorization in your browser. SOCYVIA will never display the authorization code or credentials.");
            SocyviaProductUrls.OpenInDefaultBrowser(request.AuthorizationUri);
        }
        catch (Exception)
        {
            // The browser exception may contain the authorization URL; never persist it.
            await new CloudflareOAuthPendingStore().ClearAsync();
            CloudConnectionDetail.Text = Text(
                "تعذر فتح المتصفح الافتراضي لربط Cloudflare.",
                "The default browser could not be opened for Cloudflare connection.");
        }
    }

    private async Task HandleCloudflareCallbackAsync()
    {
        if (_completingCloudflareOAuth) return;
        var callback = CloudflareOAuthCallbackInbox.Take();
        if (callback is null) return;
        _completingCloudflareOAuth = true;
        SettingsCloudState.Text = Text("جار إكمال الاتصال", "Completing connection");
        try
        {
            var result = await new CloudflareOAuthConnectionService().CompleteAsync(
                CloudflareOAuthClientConfiguration.LoadReleaseConfiguration(), callback, SelectCloudflareAccountAsync);
            CloudConnectionDetail.Text = result.Success
                ? Text("تم ربط Cloudflare. أكمل إعداد موارد البحث إذا لزم.", result.Message)
                : result.Message;
            if (result.Configuration is { } configuration)
            {
                ApplyCloudConfigurationToUi(configuration);
                SettingsCloudState.Text = Text("تم استلام التفويض", "Authorization received");
                CloudDisconnectQuickButton.IsVisible = true;
                CloudConnectButton.Content = Text("إعادة ربط Cloudflare", "Reconnect Cloudflare");
                await PrepareCloudEnvironmentAsync(configuration);
                if (_studyWorkspaceView is not null) await _studyWorkspaceView.RefreshPublishAsync();
            }
            else SettingsCloudState.Text = Text("يحتاج إلى اهتمام", "Needs Attention");
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Cloudflare OAuth completion");
            SettingsCloudState.Text = Text("يحتاج إلى اهتمام", "Needs Attention");
            CloudConnectionDetail.Text = Text(
                "تعذر إكمال اتصال Cloudflare. أعد الاتصال لبدء طلب آمن جديد.",
                "Cloudflare connection could not be completed. Reconnect to start a fresh secure request.");
        }
        finally
        {
            _completingCloudflareOAuth = false;
        }
    }

    private void OnCloudflareCallbackCaptured(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(async () => await HandleCloudflareCallbackAsync());

    private async Task<CloudflareOAuthAccount?> SelectCloudflareAccountAsync(
        IReadOnlyList<CloudflareOAuthAccount> accounts,
        CancellationToken cancellationToken)
    {
        if (VisualRoot is not Window owner || accounts.Count == 0) return null;
        var list = new ListBox
        {
            ItemsSource = accounts.Select(account => account.Name).ToArray(),
            SelectedIndex = 0,
            MinHeight = 120
        };
        var choose = new Button
        {
            Content = Text("استخدام الحساب", "Use Account"),
            Classes = { "primary" },
            MinWidth = 126
        };
        var cancel = new Button
        {
            Content = Text("إلغاء", "Cancel"),
            Classes = { "secondary" },
            MinWidth = 96
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Children = { cancel, choose }
        };
        var dialog = new Window
        {
            Title = "SOCYVIA · Cloudflare",
            Width = 460,
            Height = 330,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Padding = new Thickness(28),
                Background = new SolidColorBrush(Color.Parse("#F9FCFF")),
                BorderBrush = new SolidColorBrush(Color.Parse("#C7D8ED")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Child = new StackPanel
                {
                    Spacing = 16,
                    FlowDirection = LocalizationService.IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = Text("اختر حساب Cloudflare", "Choose a Cloudflare Account"),
                            FontSize = 20,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = new SolidColorBrush(Color.Parse("#183153"))
                        },
                        new TextBlock
                        {
                            Text = Text("اختر مساحة البحث التي سيستخدمها SOCYVIA. لن تعرض بيانات الاعتماد.", "Choose the research workspace SOCYVIA should use. Credentials are never displayed."),
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.Parse("#526883"))
                        },
                        list,
                        actions
                    }
                }
            }
        };
        WindowAppearanceService.ApplyAppIcon(dialog);
        choose.Click += (_, _) => dialog.Close(list.SelectedIndex >= 0 ? accounts[list.SelectedIndex] : null);
        cancel.Click += (_, _) => dialog.Close(null);
        cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(null)));
        return await dialog.ShowDialog<CloudflareOAuthAccount?>(owner);
    }

    private CloudflareProviderConfiguration CloudConfigurationFromFields() => new()
    {
        AccountId = CloudAccountIdBox.Text?.Trim() ?? string.Empty,
        D1DatabaseId = CloudD1IdBox.Text?.Trim() ?? string.Empty,
        R2BucketName = CloudR2BucketBox.Text?.Trim() ?? string.Empty,
        WorkerEndpoint = CloudWorkerEndpointBox.Text?.Trim() ?? string.Empty,
        ConnectionMode = CloudflareConnectionMode.Manual
    };

    private async Task SaveCloudConfigurationAsync()
    {
        var configuration = CloudConfigurationFromFields();
        var token = CloudTokenBox.Text?.Trim();
        if (!configuration.HasRequiredTextRuntimeIdentity || string.IsNullOrWhiteSpace(token)) { CloudConnectionDetail.Text = Text("أدخل معرف الحساب وقاعدة بيانات البحث ونقطة تشغيل التجربة ورمز API محدود الصلاحيات. تخزين الوسائط اختياري للتجارب النصية.", "Enter the Account ID, research database, runtime endpoint, and a scoped API token. Media storage is optional for text-only experiments."); return; }
        try
        {
            var store = new CloudflareProviderConfigurationStore();
            var previous = await store.LoadAsync();
            if (previous?.ConnectionMode == CloudflareConnectionMode.OAuth)
                await new CloudflareOAuthConnectionService().DisconnectAsync(previous, CloudflareOAuthClientConfiguration.LoadReleaseConfiguration());
            else if (previous?.ConnectionMode == CloudflareConnectionMode.Manual && previous.CredentialKey != configuration.CredentialKey)
                await SecureCredentialStoreFactory.Create().RemoveAsync(previous.CredentialKey);
            await SecureCredentialStoreFactory.Create().StoreAsync(configuration.CredentialKey, token);
            await store.SaveAsync(configuration);
            CloudTokenBox.Text = string.Empty; SettingsCloudState.Text = Text("محفوظ بأمان", "Saved securely"); CloudConnectionDetail.Text = Text("تم تفعيل وضع الإعداد اليدوي وحفظ الرمز في مخزن بيانات اعتماد نظام التشغيل، وليس في SQLite أو ملف الإعدادات.", "Manual mode is active. The token was saved in OS credential storage, never SQLite or the settings file.");
        }
        catch (Exception) { CloudConnectionDetail.Text = Text("تعذر حفظ الرمز بأمان على هذا الجهاز.", "The token could not be securely stored on this device."); }
    }

    private async Task TestCloudConnectionAsync()
    {
        var configuration = await new CloudflareProviderConfigurationStore().LoadAsync() ?? CloudConfigurationFromFields();
        var token = await new CloudflareOAuthConnectionService().GetAccessTokenAsync(
            configuration, CloudflareOAuthClientConfiguration.LoadReleaseConfiguration());
        if (string.IsNullOrWhiteSpace(token))
        {
            CloudConnectionDetail.Text = configuration.ConnectionMode == CloudflareConnectionMode.OAuth
                ? Text("انتهت صلاحية الاتصال. أعد ربط Cloudflare لبدء تفويض آمن جديد.", "The connection expired. Reconnect Cloudflare to start a fresh secure authorization.")
                : Text("احفظ رمز API محدود الصلاحيات أولا.", "Save a scoped API token first.");
            return;
        }
        if (configuration.ConnectionMode == CloudflareConnectionMode.OAuth &&
            (!configuration.HasRequiredTextRuntimeIdentity ||
             configuration.ProviderStatus is CloudflareProviderConnectionState.ConnectionFailed or
                 CloudflareProviderConnectionState.NeedsAttention or
                 CloudflareProviderConnectionState.Checking or
                 CloudflareProviderConnectionState.ConfigurationRequired))
        {
            await PrepareCloudEnvironmentAsync(configuration, token);
            return;
        }
        SettingsCloudState.Text = Text("جار التحقق", "Checking");
        var result = await new CloudflareConnectionService().InspectAsync(configuration, token);
        SettingsCloudState.Text = result.State switch
        {
            CloudflareProviderConnectionState.Ready => Text("جاهز", "Ready"),
            CloudflareProviderConnectionState.Checking => Text("جار إتمام الاتصال الآمن...", "Finalizing secure connection…"),
            _ => Text("يحتاج إلى اهتمام", "Needs Attention")
        };
        CloudConnectionDetail.Text = result.State switch
        {
            CloudflareProviderConnectionState.Ready => Text("بيئة البحث البعيدة جاهزة. تخزين الوسائط اختياري.", "The remote research environment is ready. Media storage is optional."),
            CloudflareProviderConnectionState.Checking => Text("بيئة SOCYVIA جاهزة في Cloudflare بينما يكتمل انتشار نقطة الاتصال الآمنة. أعد المحاولة بعد قليل.", "The SOCYVIA environment is ready in Cloudflare while its secure endpoint finishes propagating. Retry shortly."),
            _ => Text("بيئة البحث البعيدة ليست جاهزة بعد. أعد المحاولة أو افتح التشخيصات المتقدمة.", "Remote environment is not ready yet. Retry setup or open Advanced diagnostics.")
        };
        var verified = configuration with { ProviderStatus = result.State, LastVerifiedAtUtc = DateTime.UtcNow };
        await new CloudflareProviderConfigurationStore().SaveAsync(verified);
        ApplyCloudConfigurationToUi(verified);
    }

    private async Task PrepareCloudEnvironmentAsync(CloudflareProviderConfiguration configuration, string? token = null)
    {
        token ??= await new CloudflareOAuthConnectionService().GetAccessTokenAsync(
            configuration, CloudflareOAuthClientConfiguration.LoadReleaseConfiguration());
        if (string.IsNullOrWhiteSpace(token))
        {
            SettingsCloudState.Text = Text("يلزم إعادة الاتصال", "Reconnection required");
            CloudConnectionDetail.Text = Text("تعذر استعادة تفويض Cloudflare الآمن.", "The secure Cloudflare authorization could not be restored.");
            return;
        }

        CloudConnectButton.IsEnabled = false;
        CloudRetryButton.IsVisible = false;
        CloudTestButton.IsEnabled = false;
        var progress = new Progress<CloudflareEnvironmentProgress>(update =>
        {
            SettingsCloudState.Text = update.Stage switch
            {
                CloudflareEnvironmentStage.PreparingEnvironment => Text("جار إعداد بيئة السحابة...", "Preparing cloud environment..."),
                CloudflareEnvironmentStage.CheckingAccount => Text("جار التحقق من الحساب...", "Checking account..."),
                CloudflareEnvironmentStage.CheckingDatabase => Text("جار التحقق من قاعدة البيانات...", "Checking database..."),
                CloudflareEnvironmentStage.CheckingRuntime => Text("جار التحقق من بيئة التشغيل...", "Checking runtime..."),
                CloudflareEnvironmentStage.TestingConnection => Text("جار اختبار الاتصال...", "Testing connection..."),
                CloudflareEnvironmentStage.Ready => Text("جاهز", "Ready"),
                _ => Text("تم استلام التفويض", "Authorization received")
            };
            CloudConnectionDetail.Text = SettingsCloudState.Text;
        });

        try
        {
            var account = new CloudflareOAuthAccount(configuration.AccountId, configuration.AccountDisplayName);
            var setup = await new CloudflareResearchEnvironmentService().PrepareAsync(account, token, progress);
            var prepared = setup.Configuration with { OAuthExpiresAtUtc = configuration.OAuthExpiresAtUtc };
            await new CloudflareProviderConfigurationStore().SaveAsync(prepared);
            ApplyCloudConfigurationToUi(prepared);
            var finalizing = prepared.ProviderStatus == CloudflareProviderConnectionState.Checking;
            SettingsCloudState.Text = setup.Succeeded
                ? Text("جاهز", "Ready")
                : finalizing
                    ? Text("جار إتمام الاتصال الآمن...", "Finalizing secure connection…")
                    : Text("مشكلة في الاتصال", "Connection problem");
            CloudConnectionDetail.Text = setup.Succeeded
                ? Text("تم إعداد بيئة البحث البعيدة تلقائيا. تخزين الوسائط اختياري.", "The remote research environment was prepared automatically. Media storage is optional.")
                : finalizing
                    ? Text("تم إعداد قاعدة البيانات وبيئة التشغيل، ويجري إتمام انتشار نقطة الاتصال الآمنة. أعد المحاولة بعد قليل.", "The database and runtime are prepared while the secure endpoint finishes propagating. Retry shortly.")
                    : Text("تعذر إكمال إعداد Cloudflare تلقائيا. لم يتم حذف أو تعديل بياناتك الحالية. يمكنك إعادة المحاولة أو فتح الإعداد المتقدم للتشخيص.", "SOCYVIA could not complete Cloudflare setup automatically. Your existing data was not deleted or modified. Retry or open Advanced setup for diagnostics.");
            CloudDiagnosticDetail.Text = setup.Message;
            CloudRetryButton.IsVisible = !setup.Succeeded;
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Cloudflare automatic environment setup");
            var discovered = await new CloudflareApiClient().DiscoverResearchResourcesAsync(
                configuration.AccountId, token, cancellationToken: default);
            var failed = configuration with
            {
                D1DatabaseId = discovered.D1DatabaseId,
                D1DatabaseName = discovered.D1DatabaseName,
                WorkerName = discovered.WorkerName,
                WorkerEndpoint = discovered.WorkerEndpoint,
                R2BucketName = discovered.R2BucketName,
                ProviderStatus = CloudflareProviderConnectionState.ConnectionFailed
            };
            await new CloudflareProviderConfigurationStore().SaveAsync(failed);
            ApplyCloudConfigurationToUi(failed);
            SettingsCloudState.Text = Text("مشكلة في الاتصال", "Connection problem");
            CloudConnectionDetail.Text = Text("تعذر إكمال إعداد Cloudflare تلقائيا. لم يتم حذف أو تعديل بياناتك الحالية. يمكنك إعادة المحاولة أو فتح الإعداد المتقدم للتشخيص.", "SOCYVIA could not complete Cloudflare setup automatically. Your existing data was not deleted or modified. Retry or open Advanced setup for diagnostics.");
            CloudDiagnosticDetail.Text = exception.Message;
            CloudRetryButton.IsVisible = true;
        }
        finally
        {
            CloudConnectButton.IsEnabled = true;
            CloudTestButton.IsEnabled = true;
        }
    }

    private void ApplyCloudConfigurationToUi(CloudflareProviderConfiguration configuration)
    {
        CloudAccountIdBox.Text = configuration.AccountId;
        CloudD1IdBox.Text = configuration.D1DatabaseId;
        CloudR2BucketBox.Text = configuration.R2BucketName;
        CloudWorkerEndpointBox.Text = configuration.WorkerEndpoint;
        CloudAccountSummary.Text = string.IsNullOrWhiteSpace(configuration.AccountDisplayName) ? Text("غير محدد", "Not selected") : configuration.AccountDisplayName;
        var ready = configuration.ProviderStatus == CloudflareProviderConnectionState.Ready;
        CloudRetryButton.IsVisible = configuration.ProviderStatus is CloudflareProviderConnectionState.ConnectionFailed or CloudflareProviderConnectionState.NeedsAttention or CloudflareProviderConnectionState.Checking or CloudflareProviderConnectionState.ConfigurationRequired;
        CloudDatabaseSummary.Text = string.IsNullOrWhiteSpace(configuration.D1DatabaseId)
            ? Text("يلزم الإعداد", "Needs setup")
            : ready ? Text("جاهزة", "Ready") : Text("مكتشفة · يلزم التحقق", "Detected · Check required");
        CloudRuntimeSummary.Text = !Uri.TryCreate(configuration.WorkerEndpoint, UriKind.Absolute, out _)
            ? Text("يلزم الإعداد", "Needs setup")
            : ready ? Text("جاهزة", "Ready") : Text("مكتشفة · يلزم التحقق", "Detected · Check required");
        CloudMediaSummary.Text = string.IsNullOrWhiteSpace(configuration.R2BucketName) ? Text("اختياري · غير مهيأ", "Optional · Not configured") : Text("مهيأ", "Configured");
        CloudMediaSetupButton.IsVisible = configuration.ConnectionMode != CloudflareConnectionMode.None &&
                                          string.IsNullOrWhiteSpace(configuration.R2BucketName);
    }

    private async Task SetupCloudflareMediaStorageAsync()
    {
        ShowSettings();
        var configuration = await new CloudflareProviderConfigurationStore().LoadAsync();
        if (configuration is null || configuration.ConnectionMode == CloudflareConnectionMode.None)
        {
            SettingsCloudState.Text = Text("غير متصل", "Not connected");
            CloudConnectionDetail.Text = Text(
                "اربط Cloudflare أولا قبل إعداد تخزين الوسائط.",
                "Connect Cloudflare before setting up media storage.");
            return;
        }

        var token = await new CloudflareOAuthConnectionService().GetAccessTokenAsync(
            configuration, CloudflareOAuthClientConfiguration.LoadReleaseConfiguration());
        if (string.IsNullOrWhiteSpace(token))
        {
            SettingsCloudState.Text = Text("يلزم إعادة التفويض", "Reauthorization required");
            CloudConnectionDetail.Text = Text(
                "تعذر استعادة تفويض Cloudflare الآمن. أعد ربط Cloudflare ثم حاول إعداد الوسائط مجددا.",
                "The secure Cloudflare authorization could not be restored. Reconnect Cloudflare, then retry media setup.");
            return;
        }

        CloudMediaSetupButton.IsEnabled = false;
        SettingsCloudState.Text = Text("جار إعداد تخزين الوسائط...", "Preparing media storage...");
        CloudConnectionDetail.Text = Text(
            "يتحقق SOCYVIA من المورد الحالي أولا، ثم يعيد استخدامه أو ينشئ موردا واحدا عند توفر الصلاحية.",
            "SOCYVIA first verifies existing resources, then reuses one or creates it once when authorized.");
        var progress = new Progress<CloudflareEnvironmentProgress>(update =>
        {
            SettingsCloudState.Text = update.Stage switch
            {
                CloudflareEnvironmentStage.CheckingRuntime => Text("جار التحقق من بيئة التشغيل...", "Checking runtime..."),
                CloudflareEnvironmentStage.TestingConnection => Text("جار اختبار تخزين الوسائط...", "Testing media storage..."),
                CloudflareEnvironmentStage.Ready => Text("جاهز", "Ready"),
                _ => Text("جار إعداد تخزين الوسائط...", "Preparing media storage...")
            };
        });
        try
        {
            var result = await new CloudflareResearchEnvironmentService()
                .PrepareMediaStorageAsync(configuration, token, progress);
            CloudDiagnosticDetail.Text = result.Message;
            if (!result.Succeeded)
            {
                SettingsCloudState.Text = Text("تعذر إعداد تخزين الوسائط", "Media setup could not be completed");
                CloudConnectionDetail.Text = Text(
                    "لم يتم حذف بيانات البحث أو تغيير اتصال Cloudflare الحالي. أعد المحاولة أو افتح التشخيص المتقدم.",
                    "No research data was deleted and the current Cloudflare connection was not changed. Retry or open Advanced diagnostics.");
                return;
            }

            await new CloudflareProviderConfigurationStore().SaveAsync(result.Configuration);
            ApplyCloudConfigurationToUi(result.Configuration);
            SettingsCloudState.Text = Text("جاهز", "Ready");
            CloudConnectionDetail.Text = Text(
                "تم إعداد تخزين الوسائط والتحقق من بيئة التجربة دون تغيير بيانات البحث الحالية.",
                "Media storage and the experiment runtime were verified without changing existing research data.");
            if (_studyWorkspaceView is not null) await _studyWorkspaceView.RefreshPublishAsync();
        }
        catch (CloudflareApiException exception) when (RequiresR2ActivationOrAuthorization(exception))
        {
            ApplicationDiagnosticsService.LogException(exception, "Cloudflare media storage activation required");
            SettingsCloudState.Text = Text("متصل — يلزم تفعيل تخزين الوسائط", "Connected — Media activation required");
            CloudConnectionDetail.Text = Text(
                "يبقى اتصال Cloudflare وقاعدة البحث وبيئة التشغيل كما هي. قم بتفعيل R2 في حساب Cloudflare، ثم امنح عميل SOCYVIA صلاحية Workers R2 Storage Write وأعد التفويض.",
                "Your Cloudflare connection, research database, and runtime remain intact. Enable R2 in Cloudflare, then grant the SOCYVIA OAuth client Workers R2 Storage Write and reauthorize.");
            CloudDiagnosticDetail.Text = exception.Message;
            try { SocyviaProductUrls.OpenCloudflareMediaStorageSetup(); }
            catch (Exception openException)
            {
                ApplicationDiagnosticsService.LogException(openException, "Open Cloudflare media storage setup");
            }
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Prepare Cloudflare media storage");
            SettingsCloudState.Text = Text("تعذر إعداد تخزين الوسائط", "Media setup could not be completed");
            CloudConnectionDetail.Text = Text(
                "لم يتم حذف بيانات البحث أو تغيير اتصال Cloudflare الحالي. أعد المحاولة أو افتح التشخيص المتقدم.",
                "No research data was deleted and the current Cloudflare connection was not changed. Retry or open Advanced diagnostics.");
            CloudDiagnosticDetail.Text = exception.Message;
        }
        finally
        {
            CloudMediaSetupButton.IsEnabled = true;
        }
    }

    private static bool RequiresR2ActivationOrAuthorization(CloudflareApiException exception) =>
        exception.Errors.Any(error => error.Code == "10042") ||
        exception.HttpStatusCode == 403 &&
        exception.Operation.Contains("media storage", StringComparison.OrdinalIgnoreCase);

    private async Task DisconnectCloudAsync()
    {
        var configuration = await new CloudflareProviderConfigurationStore().LoadAsync();
        await new CloudflareOAuthConnectionService().DisconnectAsync(
            configuration, CloudflareOAuthClientConfiguration.LoadReleaseConfiguration());
        CloudTokenBox.Text = string.Empty; SettingsCloudState.Text = Text("غير متصل", "Not Connected"); CloudConnectionDetail.Text = Text("لم تعد بيانات اعتماد Cloudflare محفوظة على هذا الجهاز. لم يتم حذف أي موارد أو بيانات سحابية.", "No Cloudflare credential remains saved on this device. No remote resources or data were deleted.");
        CloudDisconnectQuickButton.IsVisible = false;
        CloudConnectButton.Content = Text("ربط Cloudflare", "Connect Cloudflare");
        CloudAccountSummary.Text = Text("غير متصل", "Not connected");
        CloudDatabaseSummary.Text = Text("غير متاحة", "Unavailable");
        CloudRuntimeSummary.Text = Text("غير متاحة", "Unavailable");
        CloudMediaSummary.Text = Text("اختياري", "Optional");
        CloudMediaSetupButton.IsVisible = false;
        CloudRetryButton.IsVisible = false;
    }

    private void ShowUpdateReadiness()
    {
        // The public signed manifest is intentionally not published during RC
        // development. This remains non-fatal and does not affect local work.
        SettingsUpdateStatus.Text = Text(
            "لم يتم نشر بيان التحديث الرسمي الموقع بعد. سيستمر SOCYVIA في العمل دون اتصال.",
            "The official signed update manifest has not been published yet. SOCYVIA remains available offline.");
    }


    private async Task ShowFirstRunOnboardingIfNeededAsync()
    {
        if (_researcher is null ||
            _researcher.OnboardingCompleted ||
            _researcher.OnboardingSkipped)
        {
            return;
        }
        await ShowOnboardingAsync();
    }


    private async Task ShowOnboardingAsync()
    {
        var owner = ResolveOwnerWindow();
        if (_researcher is null || owner is null)
        {
            return;
        }

        var tour = new OnboardingTourWindow();
        var outcome = await tour.ShowDialog<OnboardingOutcome>(owner);
        if (outcome == OnboardingOutcome.None)
        {
            return;
        }
        _researcher.OnboardingCompleted =
            outcome is OnboardingOutcome.Completed or OnboardingOutcome.CreateStudy or
                OnboardingOutcome.OpenStudy or OnboardingOutcome.ExploreDemo;
        _researcher.OnboardingSkipped =
            outcome == OnboardingOutcome.Skipped;
        ResearcherService.SaveProfile(_researcher);
        switch (outcome)
        {
            case OnboardingOutcome.CreateStudy:
                ShowStudyBuilder();
                break;
            case OnboardingOutcome.OpenStudy:
                ShowStudiesManager();
                break;
            case OnboardingOutcome.ExploreDemo:
                await ShowGuidedDemoAsync();
                break;
        }
    }


    private static string GetInitials(
        string fullName)
    {
        if (string.IsNullOrWhiteSpace(
                fullName))
        {
            return "R";
        }

        var parts =
            fullName.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 1)
        {
            return parts[0]
                .Substring(
                    0,
                    1)
                .ToUpperInvariant();
        }

        return string.Concat(
                parts.First()[0],
                parts.Last()[0])
            .ToUpperInvariant();
    }


    // =========================================================
    // FOOTER
    // =========================================================

    private void ConfigureFooter()
    {
        VersionText.Text = $"Version {SocyviaProductIdentity.Version}";
    }

    private void SetupFooterLinks()
    {
        WebsiteFooterLink.Click += (_, _) => OpenExternal("https://socyvia.com");
        EmailFooterLink.Click += (_, _) => OpenExternal("mailto:contact@socyvia.com");
    }

    private static void OpenExternal(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { /* A footer link must never destabilize the research workspace. */ }
    }


    // =========================================================
    // CONNECTIVITY
    // =========================================================

    private void SetupConnectivity()
    {
        ConfigureConnectivityVisual();
    }

    private void OnConnectivityStateChanged(object? sender, ConnectivitySnapshot snapshot) =>
        Dispatcher.UIThread.Post(() => ApplyConnectivitySnapshot(snapshot));

    private void ApplyConnectivitySnapshot(ConnectivitySnapshot snapshot)
    {
        _connectivityState = snapshot.State;
        ConfigureConnectivityVisual();
    }

    private void ConfigureConnectivityVisual()
    {
        var (label, foreground, background, border) = _connectivityState switch
        {
            ConnectivityState.Online => (
                Text("متصل", "Connected"),
                "#177A5B", "#EAF7F2", "#55A5CCBC"),
            ConnectivityState.Offline => (
                Text("غير متصل", "Offline"),
                "#A33B3B", "#FFF1F1", "#66D77A7A"),
            _ => (
                Text("جار التحقق...", "Checking…"),
                "#2456A6", "#EDF4FF", "#482563EB")
        };

        ConnectivityText.Text = label;
        ConnectivityText.FontFamily = LocalizationService.IsArabic
            ? _arabicFont
            : _englishFont;
        ConnectivityText.FlowDirection = LocalizationService.IsArabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        ConnectivityIcon.Stroke = new SolidColorBrush(Color.Parse(foreground));
        ConnectivityText.Foreground = new SolidColorBrush(Color.Parse(foreground));
        ConnectivityIndicator.Background = new SolidColorBrush(Color.Parse(background));
        ConnectivityIndicator.BorderBrush = new SolidColorBrush(Color.Parse(border));
    }


    // =========================================================
    // DATA
    // =========================================================

    private async Task LoadDashboardDataAsync()
    {
        if (_researcher is null)
        {
            return;
        }

        try
        {
            var studies =
                await StudyRepository
                    .GetByResearcherAsync(
                        _researcher.Id);

            var realStudies = DemoAccessPolicy.RealStudies(studies).ToArray();

            StudiesCountText.Text =
                realStudies.Length.ToString();

            var participantCount =
                0;

            var sessionCount =
                0;

            foreach (var study in realStudies)
            {
                participantCount +=
                    await ParticipantRepository
                        .CountByStudyAsync(
                            study.Id);

                sessionCount +=
                    await ExperimentSessionRepository
                        .CountByStudyAsync(
                            study.Id);
            }

            ParticipantsCountText.Text =
                participantCount.ToString();

            SessionsCountText.Text =
                sessionCount.ToString();

            if (realStudies.Length > 0)
            {
                ShowRecentStudies(
                    realStudies
                        .Take(4)
                        .ToArray());
            }
            else
            {
                ShowEmptyStudies();
            }

            await ConfigureResearchCommandCenterAsync(realStudies);
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Dashboard data error: {exception}");
        }
    }


    private async Task ConfigureResearchCommandCenterAsync(
        Study[] studies)
    {
        AttentionContainer.Children.Clear();
        var study = studies
            .Where(item => !DemoAccessPolicy.IsDemoStudy(item))
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefault();
        _continueStudy = study;
        _continueDestination = null;

        if (study is null)
        {
            ContinueStudyTitle.Text = Text(
                "ابدأ برنامجك البحثي الأول",
                "Start your first research programme");
            ContinueStudyDetail.Text = Text(
                "أنشئ دراسة، صمم المجموعات والشروط، ثم أضف المحتوى والمشاركين.",
                "Create a study, define groups and conditions, then add content and participants.");
            ContinueStudyStatus.Text = Text("لا توجد دراسة", "No study");
            ContinueStudyReadiness.Text = Text(
                "الخطوة التالية: إنشاء دراسة",
                "Next step: create a study");
            ContinueStudyActionText.Text = Text("دراسة جديدة", "New study");
            ContinueStudyActionText.IsVisible = true;
            AttentionTitle.Text = Text("لا توجد تنبيهات", "Nothing needs attention");
            AttentionSubtitle.Text = Text(
                "ستظهر فحوص الجاهزية هنا بعد إنشاء الدراسة.",
                "Readiness checks will appear here after a study is created.");
            AttentionCountText.Text = "0";
            AddAttentionItem(Text(
                "ابدأ بتحديد سؤال البحث والتصميم التجريبي.",
                "Begin with the research question and experimental design."),
                false);
            return;
        }

        var readinessTask = ExperimentReadinessService.EvaluateAsync(study);
        var groupsTask = GroupRepository.GetByStudyAsync(study.Id);
        var conditionsTask = ExperimentalConditionRepository.GetByStudyAsync(study.Id);
        var feedItemsTask = ExperimentalFeedRepository.CountActiveItemsByStudyAsync(study.Id);
        var participantsTask = ParticipantRepository.CountByStudyAsync(study.Id);
        var sessionsTask = ExperimentSessionRepository.GetByStudyAsync(study.Id);
        await Task.WhenAll(
            readinessTask,
            groupsTask,
            conditionsTask,
            feedItemsTask,
            participantsTask,
            sessionsTask);

        var readiness = readinessTask.Result;
        var sessions = sessionsTask.Result;
        ContinueStudyTitle.Text = DisplayStudyTitle(study);
        ContinueStudyStatus.Text = GetLocalizedStudyStatus(study.Status);
        ContinueStudyDetail.Text = Text(
            $"{groupsTask.Result.Count} مجموعات • {conditionsTask.Result.Count} شروط • {feedItemsTask.Result} مواد تجريبية • {participantsTask.Result} مشاركين • {sessions.Count} جلسات",
            $"{groupsTask.Result.Count} groups • {conditionsTask.Result.Count} conditions • {feedItemsTask.Result} feed items • {participantsTask.Result} participants • {sessions.Count} sessions");
        ContinueStudyReadiness.Text = readiness.IsReady
            ? Text("جاهزة للتحضير", "Ready to prepare")
            : Text(
                $"{readiness.ErrorCount} عناصر تحتاج إلى معالجة",
                $"{readiness.ErrorCount} items require attention");

        // The primary decision surface always opens the actual study. Contextual next
        // steps remain visible in the readiness text instead of producing an unlabeled
        // or destination-dependent primary action.
        ContinueStudyActionText.Text = Text("فتح الدراسة", "Open Study");
        ContinueStudyActionText.IsVisible = true;

        var failedChecks = readiness.Checks
            .Where(check => !check.IsPassed)
            .Take(4)
            .ToList();
        var interruptedCount = sessions.Count(item =>
            item.Status == SessionLifecycleStates.Interrupted);
        AttentionCountText.Text =
            (failedChecks.Count + interruptedCount).ToString();
        AttentionTitle.Text = Text("الجاهزية والانتباه", "Readiness and attention");
        AttentionSubtitle.Text = Text(
            "العناصر التي تؤثر على الخطوة البحثية التالية فقط.",
            "Only items that affect the next research step.");

        foreach (var check in failedChecks)
        {
            AddAttentionItem(
                LocalizeReadinessMessage(check),
                check.Severity == ExperimentReadinessSeverity.Error);
        }
        if (interruptedCount > 0)
        {
            AddAttentionItem(Text(
                $"توجد {interruptedCount} جلسات متوقفة تحتاج إلى المراجعة.",
                $"{interruptedCount} interrupted sessions require review."),
                true);
        }
        if (failedChecks.Count == 0 && interruptedCount == 0)
        {
            AddAttentionItem(Text(
                "لا توجد مشكلات قابلة للتنفيذ في الدراسة الحالية.",
                "No actionable issues in the current study."),
                false);
        }
    }


    private void AddAttentionItem(string message, bool isImportant)
    {
        AttentionContainer.Children.Add(new StatusIndicatorView(
            message,
            isImportant ? "#B4233E" : "#177A5B"));
    }


    private static string LocalizeReadinessMessage(
        ExperimentReadinessCheck check)
    {
        if (!LocalizationService.IsArabic)
        {
            return check.CanonicalMessage;
        }
        return UiTextService.Arabic(check.Code switch
        {
            "study.title" => "عنوان الدراسة مطلوب",
            "groups.active" => "أضف مجموعة نشطة واحدة على الأقل",
            "conditions.active" => "أضف شرطا تجريبيا نشطا واحدا على الأقل",
            "stimuli.active" => "أضف محفزا نشطا واحدا على الأقل",
            "conditions.links" => "راجع روابط الشروط بالمجموعات",
            "sample.target" => "حدد حجم العينة المستهدف",
            "assignment.method" => "حدد طريقة التعيين",
            _ => check.CanonicalMessage
        });
    }


    private static string Text(string arabic, string english) =>
        UiTextService.Localized(arabic, english);


    private void ShowEmptyStudies()
    {
        RecentStudiesContainer
            .Children
            .Clear();

        EmptyStudiesPanel.IsVisible =
            true;

        RecentStudiesContainer
            .Children
            .Add(
                EmptyStudiesPanel);
    }


    // =========================================================
    // RECENT STUDIES
    // =========================================================

    private void ShowRecentStudies(
        Study[] studies)
    {
        RecentStudiesContainer
            .Children
            .Clear();

        foreach (var study in studies)
        {
            RecentStudiesContainer
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
            LocalizationService.IsArabic;


        // =====================================================
        // TITLE
        // Arabic = far right
        // English = far left
        // =====================================================

        var title =
            new TextBlock
            {
                Text =
                    DisplayStudyTitle(study),

                FontFamily =
                    isArabic
                        ? _arabicFont
                        : _englishFont,

                FontSize =
                    10.8,

                FontWeight =
                    FontWeight.SemiBold,

                Foreground =
                    new SolidColorBrush(
                        Color.Parse(
                            "#263855")),

                FlowDirection =
                    isArabic
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight,

                HorizontalAlignment =
                    HorizontalAlignment.Stretch,

                TextAlignment =
                    isArabic
                        ? TextAlignment.Right
                        : TextAlignment.Left,

                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };


        // =====================================================
        // STATUS
        // Small item = centered
        // =====================================================

        var statusText =
            new TextBlock
            {
                Text =
                    GetLocalizedStudyStatus(
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
                    new SolidColorBrush(
                        Color.Parse(
                            "#2563EB")),

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
                    62,

                Height =
                    25,

                Padding =
                    new Thickness(
                        9,
                        0),

                Background =
                    new SolidColorBrush(
                        Color.Parse(
                            "#F0F1FF")),

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

        var date =
            new TextBlock
            {
                Text =
                    study.UpdatedAtUtc
                        .ToLocalTime()
                        .ToString(
                            "dd MMM yyyy"),

                FontFamily =
                    _englishFont,

                FontSize =
                    7.8,

                Foreground =
                    new SolidColorBrush(
                        Color.Parse(
                            "#9BA6B8")),

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center,

                TextAlignment =
                    TextAlignment.Center
            };


        // =====================================================
        // METADATA
        // =====================================================

        var metadataPanel =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,

                Spacing =
                    10,

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


        metadataPanel.Children.Add(
            new TextBlock
            {
                Text =
                    "•",

                FontSize =
                    7,

                Foreground =
                    new SolidColorBrush(
                        Color.Parse(
                            "#C6CCD6")),

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center
            });


        metadataPanel.Children.Add(
            date);


        // =====================================================
        // STUDY INFO
        // =====================================================

        var studyInfoPanel =
            new StackPanel
            {
                Spacing =
                    7,

                HorizontalAlignment =
                    HorizontalAlignment.Stretch,

                VerticalAlignment =
                    VerticalAlignment.Center
            };


        studyInfoPanel.Children.Add(
            title);


        studyInfoPanel.Children.Add(
            metadataPanel);


        // =====================================================
        // MAIN CLICKABLE AREA
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
                        11),

                HorizontalAlignment =
                    HorizontalAlignment.Stretch,

                HorizontalContentAlignment =
                    HorizontalAlignment.Stretch,

                VerticalContentAlignment =
                    VerticalAlignment.Center,

                Cursor =
                    new Avalonia.Input.Cursor(
                        Avalonia.Input.StandardCursorType.Hand),

                Content =
                    studyInfoPanel
            };


        openButton.Click +=
            (_, _) =>
            {
                ShowStudyWorkspace(
                    study);
            };


        var menuButton =
            CreateStudyMenuButton(
                study);


        // =====================================================
        // CARD STRUCTURE
        //
        // ARABIC:
        // [ ⋯ ] ........................ [ STUDY INFO ]
        //
        // ENGLISH:
        // [ STUDY INFO ] ............... [ ⋯ ]
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
                68,

            Background =
                new SolidColorBrush(
                    Color.Parse(
                        "#F9FAFD")),

            BorderBrush =
                new SolidColorBrush(
                    Color.Parse(
                        "#EDF0F5")),

            BorderThickness =
                new Thickness(1),

            CornerRadius =
                new CornerRadius(10),

            HorizontalAlignment =
                HorizontalAlignment.Stretch,

            Child =
                cardGrid
        };
    }


    // =========================================================
    // STUDY CARD MENU
    // =========================================================

    private Button CreateStudyMenuButton(
    Study study)
{
    var isArabic =
        LocalizationService.IsArabic;


    var menuButton =
        new Button
        {
            Width =
                38,

            Height =
                38,

            Margin =
                new Thickness(
                    6,
                    0),

            Padding =
                new Thickness(0),

            Background =
                Brushes.Transparent,

            BorderThickness =
                new Thickness(0),

            CornerRadius =
                new CornerRadius(
                    8),

            HorizontalAlignment =
                HorizontalAlignment.Center,

            VerticalAlignment =
                VerticalAlignment.Center,

            HorizontalContentAlignment =
                HorizontalAlignment.Center,

            VerticalContentAlignment =
                VerticalAlignment.Center,

            Cursor =
                new Avalonia.Input.Cursor(
                    Avalonia.Input.StandardCursorType.Hand),

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
                        new SolidColorBrush(
                            Color.Parse(
                                "#7F8A9D")),

                    HorizontalAlignment =
                        HorizontalAlignment.Center,

                    VerticalAlignment =
                        VerticalAlignment.Center,

                    TextAlignment =
                        TextAlignment.Center
                }
        };


    // Adaptive width instead of Width = 178.
    var menuPanel =
        new StackPanel
        {
            MinWidth =
                142,

            MaxWidth =
                195,

            Spacing =
                1,

            HorizontalAlignment =
                HorizontalAlignment.Stretch
        };


    var openItem =
        CreateStudyMenuItem(
            isArabic
                ? "فتح الدراسة"
                : "Open study");


    openItem.Click +=
        (_, _) =>
        {
            menuButton.Flyout?
                .Hide();


            ShowStudyWorkspace(
                study);
        };


    var editItem =
        CreateStudyMenuItem(
            isArabic
                ? "تعديل الدراسة"
                : "Edit study");


    editItem.Click +=
        (_, _) =>
        {
            menuButton.Flyout?
                .Hide();


            ShowStudyBuilder(
                study);
        };


    var archiveItem =
        CreateStudyMenuItem(
            isArabic
                ? "أرشفة الدراسة"
                : "Archive study");


    archiveItem.Click +=
        async (_, _) =>
        {
            menuButton.Flyout?
                .Hide();


            await ArchiveStudyAsync(
                study);
        };


    var deleteItem =
        CreateStudyMenuItem(
            isArabic
                ? "حذف نهائي"
                : "Delete permanently",
            destructive: true);


    deleteItem.Click +=
        async (_, _) =>
        {
            menuButton.Flyout?
                .Hide();


            await DeleteStudyAsync(
                study);
        };


    menuPanel.Children.Add(
        openItem);


    menuPanel.Children.Add(
        editItem);


    menuPanel.Children.Add(
        new Border
        {
            Height =
                1,

            Margin =
                new Thickness(
                    6,
                    4),

            Background =
                new SolidColorBrush(
                    Color.Parse(
                        "#EDF0F5"))
        });


    menuPanel.Children.Add(
        archiveItem);


    menuPanel.Children.Add(
        deleteItem);


    menuButton.Flyout =
        new Flyout
        {
            Placement =
                PlacementMode.BottomEdgeAlignedRight,

            Content =
                new Border
                {
                    Padding =
                        new Thickness(
                            5),

                    Background =
                        Brushes.White,

                    BorderBrush =
                        new SolidColorBrush(
                            Color.Parse(
                                "#E6EAF1")),

                    BorderThickness =
                        new Thickness(
                            1),

                    CornerRadius =
                        new CornerRadius(
                            11),

                    Child =
                        menuPanel
                }
        };


    return menuButton;
}


    private Button CreateStudyMenuItem(
        string text,
        bool destructive = false)
    {
        var isArabic =
            LocalizationService.IsArabic;


        var label =
            new TextBlock
            {
                Text =
                    text,

                FontFamily =
                    isArabic
                        ? _arabicFont
                        : _englishFont,

                FontSize =
                    8.6,

                Foreground =
                    new SolidColorBrush(
                        Color.Parse(
                            destructive
                                ? "#D84A5B"
                                : "#455671")),

                FlowDirection =
                    isArabic
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight,

                TextAlignment =
                    TextAlignment.Center,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center
            };


        return new Button
        {
            Height =
                31,

            MinWidth =
                132,

            Padding =
                new Thickness(
                    12,
                    0),

            Background =
                Brushes.Transparent,

            BorderThickness =
                new Thickness(0),

            CornerRadius =
                new CornerRadius(
                    7),

            HorizontalAlignment =
                HorizontalAlignment.Stretch,

            HorizontalContentAlignment =
                HorizontalAlignment.Center,

            VerticalContentAlignment =
                VerticalAlignment.Center,

            Cursor =
                new Avalonia.Input.Cursor(
                    Avalonia.Input.StandardCursorType.Hand),

            Content =
                label
        };
    }


    // =========================================================
    // ARCHIVE
    // =========================================================

    private async Task ArchiveStudyAsync(
        Study study)
    {
        var confirmed =
            await ShowConfirmationDialogAsync(
                LocalizationService.IsArabic
                    ? "أرشفة الدراسة"
                    : "Archive study",

                LocalizationService.IsArabic
                    ? $"هل تريد أرشفة «{study.Title}»؟ ستختفي من الدراسات النشطة، لكن بياناتها لن تحذف."
                    : $"Archive “{study.Title}”? It will disappear from active studies, but its data will be preserved.",

                LocalizationService.IsArabic
                    ? "أرشفة"
                    : "Archive",

                destructive: false);


        if (!confirmed)
        {
            return;
        }


        try
        {
            await StudyService.ArchiveStudyAsync(
                study.Id);


            await LoadDashboardDataAsync();


            if (_studiesView is not null)
            {
                await _studiesView.ReloadAsync();
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Archive study error: {exception}");
        }
    }


    // =========================================================
    // DELETE
    // =========================================================

    private async Task DeleteStudyAsync(
        Study study)
    {
        var confirmed =
            await ShowConfirmationDialogAsync(
                LocalizationService.IsArabic
                    ? "حذف الدراسة نهائيا"
                    : "Delete study permanently",

                LocalizationService.IsArabic
                    ? $"سيتم حذف «{study.Title}» وجميع البيانات المرتبطة بها نهائيا، بما في ذلك المشاركون والجلسات والاستجابات والأحداث. لا يمكن التراجع عن هذه العملية."
                    : $"“{study.Title}” and all related data will be permanently deleted, including participants, sessions, responses and events. This cannot be undone.",

                LocalizationService.IsArabic
                    ? "حذف نهائي"
                    : "Delete permanently",

                destructive: true);


        if (!confirmed)
        {
            return;
        }


        try
        {
            await StudyService.DeleteStudyAsync(
                study.Id);


            await LoadDashboardDataAsync();


            if (_studiesView is not null)
            {
                await _studiesView.ReloadAsync();
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Delete study error: {exception}");
        }
    }


    // =========================================================
    // CONFIRMATION DIALOG
    // =========================================================

    private async Task<bool> ShowConfirmationDialogAsync(
        string title,
        string message,
        string confirmText,
        bool destructive)
    {
        var owner = ResolveOwnerWindow();
        if (owner is null)
        {
            return false;
        }


        var isArabic =
            LocalizationService.IsArabic;


        var dialog =
            new Window
            {
                Title =
                    title,

                Width =
                    430,

                Height =
                    245,

                MinWidth =
                    430,

                MinHeight =
                    245,

                MaxWidth =
                    430,

                MaxHeight =
                    245,

                CanResize =
                    false,

                ShowInTaskbar =
                    false,

                WindowStartupLocation =
                    WindowStartupLocation.CenterOwner,

                Background =
                    new SolidColorBrush(
                        Color.Parse(
                            "#EAF1F8"))
            };


        WindowAppearanceService.ApplyAppIcon(
            dialog);


        var titleText =
            new TextBlock
            {
                Text =
                    title,

                FontFamily =
                    isArabic
                        ? _arabicFont
                        : _englishFont,

                FontSize =
                    15,

                FontWeight =
                    FontWeight.SemiBold,

                Foreground =
                    new SolidColorBrush(
                        Color.Parse(
                            "#203451")),

                FlowDirection =
                    isArabic
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight,

                TextAlignment =
                    isArabic
                        ? TextAlignment.Right
                        : TextAlignment.Left,

                HorizontalAlignment =
                    HorizontalAlignment.Stretch
            };


        var messageText =
            new TextBlock
            {
                Text =
                    message,

                FontFamily =
                    isArabic
                        ? _arabicFont
                        : _englishFont,

                FontSize =
                    9.2,

                LineHeight =
                    17,

                Foreground =
                    new SolidColorBrush(
                        Color.Parse(
                            "#718097")),

                FlowDirection =
                    isArabic
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight,

                TextAlignment =
                    isArabic
                        ? TextAlignment.Right
                        : TextAlignment.Left,

                TextWrapping =
                    TextWrapping.Wrap,

                HorizontalAlignment =
                    HorizontalAlignment.Stretch
            };


        var cancelButton =
            new Button
            {
                Height =
                    38,

                MinWidth =
                    90,

                Content =
                    isArabic
                        ? "إلغاء"
                        : "Cancel",

                FontFamily =
                    isArabic
                        ? _arabicFont
                        : _englishFont,

                HorizontalContentAlignment =
                    HorizontalAlignment.Center,

                VerticalContentAlignment =
                    VerticalAlignment.Center,

                Cursor =
                    new Avalonia.Input.Cursor(
                        Avalonia.Input.StandardCursorType.Hand)
            };
        cancelButton.Classes.Add("secondary");


        var confirmButton =
            new Button
            {
                Height =
                    38,

                MinWidth =
                    110,

                Padding =
                    new Thickness(
                        16,
                        0),

                Content =
                    confirmText,

                FontFamily =
                    isArabic
                        ? _arabicFont
                        : _englishFont,

                HorizontalContentAlignment =
                    HorizontalAlignment.Center,

                VerticalContentAlignment =
                    VerticalAlignment.Center,

                Cursor =
                    new Avalonia.Input.Cursor(
                        Avalonia.Input.StandardCursorType.Hand)
            };
        confirmButton.Classes.Add(destructive ? "danger" : "primary");


        cancelButton.Click +=
            (_, _) =>
            {
                dialog.Close(
                    false);
            };


        confirmButton.Click +=
            (_, _) =>
            {
                dialog.Close(
                    true);
            };


        var buttonPanel =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,

                Spacing =
                    9,

                HorizontalAlignment =
                    isArabic
                        ? HorizontalAlignment.Left
                        : HorizontalAlignment.Right
            };


        if (isArabic)
        {
            buttonPanel.Children.Add(
                confirmButton);


            buttonPanel.Children.Add(
                cancelButton);
        }
        else
        {
            buttonPanel.Children.Add(
                cancelButton);


            buttonPanel.Children.Add(
                confirmButton);
        }


        var messageBorder =
            new Border
            {
                Padding =
                    new Thickness(
                        0,
                        14,
                        0,
                        10),

                Child =
                    messageText
            };


        Grid.SetRow(
            messageBorder,
            1);


        var buttonsBorder =
            new Border
            {
                Child =
                    buttonPanel
            };


        Grid.SetRow(
            buttonsBorder,
            2);


        var dialogGrid =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,*,Auto")
            };


        dialogGrid.Children.Add(
            titleText);


        dialogGrid.Children.Add(
            messageBorder);


        dialogGrid.Children.Add(
            buttonsBorder);


        var dialogSurface = new Border
        {
            Margin = new Thickness(16),
            Padding = new Thickness(22),
            Child = dialogGrid
        };
        dialogSurface.Classes.Add("dialogSurface");
        dialog.Content = dialogSurface;
        WindowAppearanceService.ApplySocyviaDialogChrome(dialog);


        return await dialog.ShowDialog<bool>(
            owner);
    }


    // =========================================================
    // NAVIGATION
    // =========================================================

    private void SetupNavigation()
    {
        HomeButton.Click +=
            (_, _) =>
            {
                ShowHome();
            };

        ContentLibraryButton.Click +=
            (_, _) => ShowContentLibrary();


        StudiesButton.Click +=
            (_, _) =>
            {
                ShowStudiesManager();
            };


        ParticipantsButton.Click +=
            (_, _) =>
            {
                ShowSection(
                    ParticipantsButton,

                    LocalizationService.IsArabic
                        ? "المشاركون"
                        : "Participants",

                    LocalizationService.IsArabic
                        ? "تتم إدارة سجلات المشاركين والموافقة والتعيين داخل مساحة كل دراسة. افتح دراسة للمتابعة."
                        : "Participant records, consent, and assignment are managed inside each study workspace. Open a study to continue.");
            };


        SessionsButton.Click +=
            (_, _) =>
            {
                ShowSection(
                    SessionsButton,
                    Text("الجلسات", "Sessions"),
                    Text(
                        "راجع الجلسات حسب حالتها وافتح الدراسة المناسبة لإدارتها.",
                        "Review sessions by lifecycle state and open their study to manage them."));
            };

        DemoButton.Click += async (_, _) => await ConfirmAndOpenOfficialDemoAsync();


        AnalysisButton.Click +=
            async (_, _) =>
            {
                if (_studyWorkspaceView is not null)
                {
                    await _studyWorkspaceView.OpenAnalysisAsync();
                    return;
                }
                if (_continueStudy is not null)
                {
                    ShowStudyWorkspace(_continueStudy, "analysis");
                    return;
                }
                ShowStudiesManager();
                CurrentSectionSubtitle.Text = Text(
                    "اختر دراسة لفتح مساحة التحليل الحتمي الخاصة بها.",
                    "Choose a study to open its deterministic analysis workspace.");
            };


        ReportsButton.Click +=
            async (_, _) =>
            {
                if (_studyWorkspaceView is not null)
                {
                    await _studyWorkspaceView.OpenResearchResultsAsync("report");
                    return;
                }
                if (_continueStudy is not null)
                {
                    ShowStudyWorkspace(_continueStudy, "report");
                    return;
                }
                ShowStudiesManager();
                CurrentSectionSubtitle.Text = Text(
                    "افتح دراسة لمعاينة تقريرها الحتمي.",
                    "Open a study to preview its deterministic report.");
            };


        SocyviaAiButton.Click +=
            async (_, _) =>
            {
                if (_studyWorkspaceView is not null)
                {
                    await _studyWorkspaceView.OpenSocyviaAiAsync();
                    return;
                }
                if (_continueStudy is not null)
                {
                    ShowStudyWorkspace(_continueStudy, "ai");
                    return;
                }
                ShowSocyviaAiProductHelp();
            };


        SettingsButton.Click +=
            (_, _) =>
            {
                ShowSettings();
            };


        ViewAllStudiesButton.Click +=
            (_, _) =>
            {
                ShowStudiesManager();
            };


        NewStudyButton.Click +=
            (_, _) =>
            {
                ShowStudyBuilder();
            };


        EmptyCreateStudyButton.Click +=
            (_, _) =>
            {
                ShowStudyBuilder();
            };

        ContinueStudyActionButton.Click +=
            (_, _) =>
            {
                if (_continueStudy is null)
                {
                    ShowStudyBuilder();
                }
                else
                {
                    ShowStudyWorkspace(_continueStudy, _continueDestination);
                }
            };

        AddParticipantsQuickButton.Click +=
            (_, _) => OpenContinueStudy("participants");

        PrepareSessionQuickButton.Click +=
            (_, _) => OpenContinueStudy("sessions");

        ImportDataButton.Click +=
            (_, _) => ShowContentLibrary();

        PlaceholderActionButton.Click +=
            (_, _) => OpenContinueStudy(_placeholderDestination);
    }


    private void OpenContinueStudy(string? destination = null)
    {
        if (_continueStudy is null)
        {
            ShowStudiesManager();
            return;
        }
        ShowStudyWorkspace(_continueStudy, destination);
    }

    private async Task ShowGuidedDemoAsync()
    {
        if (_researcher is null || GetContentGrid() is not { } contentGrid) return;
        RemoveStudyBuilder();
        RemoveStudyWorkspace();
        RemoveStudiesView();
        RemoveContentLibraryView();
        RemoveGuidedDemoView();
        HomeContent.IsVisible = false;
        SectionPlaceholder.IsVisible = false;
        SettingsPanel.IsVisible = false;
        SetSelectedNavigation(DemoButton);
        CurrentSectionTitle.Text = Text("عرض SOCYVIA التجريبي", "SOCYVIA Guided Demo");
        CurrentSectionSubtitle.Text = Text("رحلة بحث اصطناعية للقراءة والمعاينة فقط", "A read-only synthetic research journey");
        _guidedDemoView = new GuidedDemoView(_researcher);
        contentGrid.Children.Add(_guidedDemoView);
        await Task.CompletedTask;
    }

    private async Task ConfirmAndOpenOfficialDemoAsync()
    {
        var open = await ShowConfirmationDialogAsync(
            Text("العرض التجريبي التفاعلي", "Interactive Demo"),
            Text(
                "استكشف خلاصة تجريبية عامة واصطناعية للقراءة فقط. لن تؤثر تفاعلات العرض في دراساتك أو بيانات البحث.",
                "Explore a public, synthetic, read-only experimental feed. Demo interactions never affect your studies or research data."),
            Text("فتح العرض", "Open Demo"),
            false);
        if (!open) return;
        try
        {
            SocyviaProductUrls.OpenParticipantDemo();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to open the SOCYVIA public Demo: {exception}");
            CurrentSectionSubtitle.Text = Text(
                "تعذر فتح المتصفح الافتراضي.",
                "The default browser could not be opened.");
        }
    }

    private void RemoveGuidedDemoView()
    {
        if (_guidedDemoView?.Parent is Panel panel) panel.Children.Remove(_guidedDemoView);
        _guidedDemoView = null;
    }

    private void ShowSocyviaAiProductHelp()
    {
        if (_researcher is null || GetContentGrid() is not { } contentGrid) return;
        RemoveStudyBuilder();
        RemoveStudyWorkspace();
        RemoveStudiesView();
        RemoveContentLibraryView();
        RemoveGuidedDemoView();
        RemoveSocyviaAiProductHelpView();
        HomeContent.IsVisible = false;
        SectionPlaceholder.IsVisible = false;
        SettingsPanel.IsVisible = false;
        SetSelectedNavigation(SocyviaAiButton);
        CurrentSectionTitle.Text = "SOCYVIA AI";
        CurrentSectionSubtitle.Text = Text(
            "مساعد SOCYVIA للإرشاد داخل المنتج وتفسير أدلة الدراسة عند توفرها.",
            "SOCYVIA assistant for product guidance and study evidence when available.");
        _socyviaAiProductHelpView = new SocyviaAiProductHelpView();
        contentGrid.Children.Add(_socyviaAiProductHelpView);
    }

    private void RemoveSocyviaAiProductHelpView()
    {
        if (_socyviaAiProductHelpView?.Parent is Panel panel) panel.Children.Remove(_socyviaAiProductHelpView);
        _socyviaAiProductHelpView = null;
    }


    // =========================================================
    // STUDIES MANAGER
    // =========================================================

    private void ShowStudiesManager()
    {
        if (_researcher is null)
        {
            return;
        }


        var contentGrid =
            GetContentGrid();


        if (contentGrid is null)
        {
            return;
        }


        RemoveStudyBuilder();

        RemoveStudyWorkspace();

        RemoveStudiesView();


        _studyBeingEdited =
            null;


        HomeContent.IsVisible =
            false;


        SectionPlaceholder.IsVisible =
            false;

        SettingsPanel.IsVisible = false;


        SetSelectedNavigation(
            StudiesButton);


        CurrentSectionTitle.Text =
            LocalizationService.IsArabic
                ? "الدراسات"
                : "Studies";


        CurrentSectionSubtitle.Text =
            LocalizationService.IsArabic
                ? "إدارة المشاريع البحثية"
                : "Manage research projects";


        _studiesView =
            new StudiesView(
                _researcher);


        _studiesView.NewStudyRequested +=
            OnStudiesNewStudyRequested;


        _studiesView.OpenStudyRequested +=
            OnStudiesOpenRequested;


        _studiesView.EditStudyRequested +=
            OnStudiesEditRequested;


        _studiesView.ArchiveStudyRequested +=
            OnStudiesArchiveRequested;


        _studiesView.RestoreStudyRequested +=
            OnStudiesRestoreRequested;


        _studiesView.DeleteStudyRequested +=
            OnStudiesDeleteRequested;

        _studiesView.DuplicateStudyRequested +=
            OnStudiesDuplicateRequested;


        contentGrid.Children.Add(
            _studiesView);
    }


    private void OnStudiesNewStudyRequested(
        object? sender,
        EventArgs e)
    {
        ShowStudyBuilder();
    }


    private void OnStudiesOpenRequested(
        object? sender,
        Study study)
    {
        ShowStudyWorkspace(
            study);
    }


    private void OnStudiesEditRequested(
        object? sender,
        Study study)
    {
        ShowStudyBuilder(
            study);
    }


    private async void OnStudiesArchiveRequested(
        object? sender,
        Study study)
    {
        if (!DemoAccessPolicy.CanMutate(study))
        {
            await ShowGuidedDemoAsync();
            return;
        }

        await ArchiveStudyAsync(
            study);


        if (_studiesView is not null)
        {
            await _studiesView.ReloadAsync();
        }
    }


    // =========================================================
    // RESTORE ARCHIVED STUDY
    // =========================================================

    private async void OnStudiesRestoreRequested(
        object? sender,
        Study study)
    {
        if (!DemoAccessPolicy.CanMutate(study))
        {
            await ShowGuidedDemoAsync();
            return;
        }

        try
        {
            await ArchivedStudyRepository
                .RestoreAsync(
                    study.Id);


            await LoadDashboardDataAsync();


            if (_studiesView is not null)
            {
                await _studiesView.ReloadAsync();
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Restore study error: {exception}");
        }
    }


    private async void OnStudiesDeleteRequested(
        object? sender,
        Study study)
    {
        if (!DemoAccessPolicy.CanMutate(study))
        {
            await ShowGuidedDemoAsync();
            return;
        }

        await DeleteStudyAsync(
            study);


        if (_studiesView is not null)
        {
            await _studiesView.ReloadAsync();
        }
    }


    private async void OnStudiesDuplicateRequested(object? sender, Study study)
    {
        if (!DemoAccessPolicy.CanMutate(study))
        {
            await ShowGuidedDemoAsync();
            return;
        }

        var confirmed = await ShowConfirmationDialogAsync(
            Text("نسخ تصميم الدراسة", "Duplicate study design"),
            Text(
                $"سيتم إنشاء «نسخة من {study.Title}» من التصميم فقط. لن يتم نسخ المشاركين أو الجلسات أو النتائج أو حالة النشر.",
                $"A design-only “Copy of {study.Title}” will be created. Participants, sessions, results, and publication state will not be copied."),
            Text("نسخ الدراسة", "Duplicate Study"),
            destructive: false);
        if (!confirmed) return;

        try
        {
            var duplicate = await StudyDuplicationService.DuplicateAsync(study, LocalizationService.IsArabic);
            await LoadDashboardDataAsync();
            ShowStudyWorkspace(duplicate);
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Duplicate study design");
        }
    }

    private void RemoveStudiesView()
    {
        if (_studiesView is null)
        {
            return;
        }


        _studiesView.NewStudyRequested -=
            OnStudiesNewStudyRequested;


        _studiesView.OpenStudyRequested -=
            OnStudiesOpenRequested;


        _studiesView.EditStudyRequested -=
            OnStudiesEditRequested;


        _studiesView.ArchiveStudyRequested -=
            OnStudiesArchiveRequested;


        _studiesView.RestoreStudyRequested -=
            OnStudiesRestoreRequested;


        _studiesView.DeleteStudyRequested -=
            OnStudiesDeleteRequested;


        _studiesView.DuplicateStudyRequested -=
            OnStudiesDuplicateRequested;


        if (_studiesView.Parent
            is Panel panel)
        {
            panel.Children.Remove(
                _studiesView);
        }


        _studiesView =
            null;
    }


    // =========================================================
    // STUDY BUILDER
    // =========================================================

    private void ShowStudyBuilder()
    {
        ShowStudyBuilder(
            null);
    }


    private void ShowStudyBuilder(
        Study? study)
    {
        if (DemoAccessPolicy.IsReadOnlyStudy(study))
        {
            _ = ShowGuidedDemoAsync();
            return;
        }

        if (_researcher is null)
        {
            return;
        }


        var contentGrid =
            GetContentGrid();


        if (contentGrid is null)
        {
            return;
        }


        RemoveStudyWorkspace();

        RemoveStudiesView();

        RemoveStudyBuilder();


        _studyBeingEdited =
            study;


        HomeContent.IsVisible =
            false;


        SectionPlaceholder.IsVisible =
            false;

        SettingsPanel.IsVisible = false;


        SetSelectedNavigation(
            StudiesButton);


        CurrentSectionTitle.Text =
            study is null
                ? LocalizationService.IsArabic
                    ? "دراسة جديدة"
                    : "New Study"
                : LocalizationService.IsArabic
                    ? "تعديل الدراسة"
                    : "Edit Study";


        CurrentSectionSubtitle.Text =
            study is null
                ? LocalizationService.IsArabic
                    ? "إعداد دراسة بحثية جديدة"
                    : "Create a new research study"
                : DisplayStudyTitle(study);


        _studyBuilderView =
            study is null
                ? new StudyBuilderView(
                    _researcher)
                : new StudyBuilderView(
                    _researcher,
                    study);


        _studyBuilderView.CancelRequested +=
            OnStudyBuilderCancelled;


        _studyBuilderView.StudyCreated +=
            OnStudySaved;


        contentGrid.Children.Add(
            _studyBuilderView);
    }


    private void OnStudyBuilderCancelled(
        object? sender,
        EventArgs e)
    {
        var editedStudy =
            _studyBeingEdited;


        RemoveStudyBuilder();


        _studyBeingEdited =
            null;


        if (editedStudy is not null)
        {
            ShowStudyWorkspace(
                editedStudy);


            return;
        }


        ShowHome();
    }


    private async void OnStudySaved(
        object? sender,
        Study study)
    {
        var wasEditing =
            _studyBeingEdited is not null;


        RemoveStudyBuilder();


        _studyBeingEdited =
            null;


        await LoadDashboardDataAsync();


        if (wasEditing)
        {
            var refreshed =
                await StudyService
                    .GetStudyAsync(
                        study.Id);


            ShowStudyWorkspace(
                refreshed
                ?? study);


            return;
        }


        ShowStudyWorkspace(
            study);
    }


    private void RemoveStudyBuilder()
    {
        RemoveSocyviaAiProductHelpView();
        RemoveGuidedDemoView();
        RemoveContentLibraryView();

        if (_studyBuilderView is null)
        {
            return;
        }


        _studyBuilderView.CancelRequested -=
            OnStudyBuilderCancelled;


        _studyBuilderView.StudyCreated -=
            OnStudySaved;


        if (_studyBuilderView.Parent
            is Panel panel)
        {
            panel.Children.Remove(
                _studyBuilderView);
        }


        _studyBuilderView =
            null;
    }


    // =========================================================
    // STUDY WORKSPACE
    // =========================================================

    private void ShowStudyWorkspace(
        Study study,
        string? destination = null)
    {
        if (DemoAccessPolicy.IsReadOnlyStudy(study))
        {
            _ = ShowGuidedDemoAsync();
            return;
        }

        var contentGrid =
            GetContentGrid();


        if (contentGrid is null)
        {
            return;
        }


        RemoveStudyBuilder();

        RemoveStudyWorkspace();

        RemoveStudiesView();


        HomeContent.IsVisible =
            false;


        SectionPlaceholder.IsVisible =
            false;

        SettingsPanel.IsVisible = false;


        SetSelectedNavigation(
            StudiesButton);


        CurrentSectionTitle.Text =
            DisplayStudyTitle(study);


        CurrentSectionSubtitle.Text =
            LocalizationService.IsArabic
                ? "مساحة الدراسة"
                : "Study workspace";


        _studyWorkspaceView =
            new StudyWorkspaceView(
                study);


        _studyWorkspaceView.BackRequested +=
            OnStudyWorkspaceBackRequested;


        _studyWorkspaceView.EditRequested +=
            OnStudyWorkspaceEditRequested;

        _studyWorkspaceView.ContentLibraryRequested +=
            OnStudyWorkspaceContentLibraryRequested;

        _studyWorkspaceView.MediaUrlsSetupRequested +=
            OnStudyWorkspaceMediaUrlsSetupRequested;

        _studyWorkspaceView.CloudSettingsRequested +=
            OnStudyWorkspaceCloudSettingsRequested;


        contentGrid.Children.Add(
            _studyWorkspaceView);

        switch (destination)
        {
            case "stimuli":
                _studyWorkspaceView.OpenStimulusLibrary();
                break;
            case "participants":
                _ = _studyWorkspaceView.OpenParticipantsAsync();
                break;
            case "sessions":
                _ = _studyWorkspaceView.OpenSessionsAsync();
                break;
            case "builder":
                _ = _studyWorkspaceView.OpenExperimentBuilderAsync();
                break;
            case "ai":
                _ = _studyWorkspaceView.OpenSocyviaAiAsync();
                break;
            case "analysis":
                _ = _studyWorkspaceView.OpenAnalysisAsync();
                break;
            case "report":
                _ = _studyWorkspaceView.OpenResearchResultsAsync("report");
                break;
        }
    }


    private void OnStudyWorkspaceBackRequested(
        object? sender,
        EventArgs e)
    {
        RemoveStudyWorkspace();

        ShowHome();
    }


    private void OnStudyWorkspaceEditRequested(
        object? sender,
        Study study)
    {
        ShowStudyBuilder(
            study);
    }


    private void OnStudyWorkspaceContentLibraryRequested(
        object? sender,
        EventArgs e)
    {
        ShowContentLibrary();
    }

    private void OnStudyWorkspaceMediaUrlsSetupRequested(string? contentItemId)
    {
        ShowContentLibrary(contentItemId);
    }

    private void OnStudyWorkspaceCloudSettingsRequested(
        object? sender,
        EventArgs e)
    {
        _ = StartCloudflareConnectionAsync();
    }


    private void HideStudyWorkspace()
    {
        if (_studyWorkspaceView is not null)
        {
            _studyWorkspaceView.IsVisible =
                false;
        }
    }


    private void RemoveStudyWorkspace()
    {
        if (_studyWorkspaceView is null)
        {
            return;
        }


        _studyWorkspaceView.BackRequested -=
            OnStudyWorkspaceBackRequested;


        _studyWorkspaceView.EditRequested -=
            OnStudyWorkspaceEditRequested;

        _studyWorkspaceView.ContentLibraryRequested -=
            OnStudyWorkspaceContentLibraryRequested;

        _studyWorkspaceView.MediaUrlsSetupRequested -=
            OnStudyWorkspaceMediaUrlsSetupRequested;

        _studyWorkspaceView.CloudSettingsRequested -=
            OnStudyWorkspaceCloudSettingsRequested;


        if (_studyWorkspaceView.Parent
            is Panel panel)
        {
            panel.Children.Remove(
                _studyWorkspaceView);
        }


        _studyWorkspaceView =
            null;
    }


    // =========================================================
    // CONTENT LIBRARY
    // =========================================================

    private void ShowContentLibrary(string? initialContentItemId = null)
    {
        if (_researcher is null || GetContentGrid() is not { } contentGrid)
        {
            return;
        }

        RemoveStudyBuilder();
        RemoveStudyWorkspace();
        RemoveStudiesView();
        RemoveContentLibraryView();
        _studyBeingEdited = null;
        HomeContent.IsVisible = false;
        SectionPlaceholder.IsVisible = false;
        SettingsPanel.IsVisible = false;
        SetSelectedNavigation(ContentLibraryButton);
        CurrentSectionTitle.Text = Text("مكتبة المحتوى", "Content Library");
        CurrentSectionSubtitle.Text = Text(
            "حقيقة المصدر والمحتوى البحثي القابل لإعادة الاستخدام",
            "Source truth and reusable research content");
        _contentLibraryView = new ContentLibraryView(_researcher, initialContentItemId);
        contentGrid.Children.Add(_contentLibraryView);
    }


    private void RemoveContentLibraryView()
    {
        if (_contentLibraryView?.Parent is Panel panel)
        {
            panel.Children.Remove(_contentLibraryView);
        }
        _contentLibraryView = null;
    }


    // =========================================================
    // CONTENT GRID
    // =========================================================

    private Grid? GetContentGrid()
    {
        return HomeContent.Parent
            as Grid;
    }


    // =========================================================
    // HOME
    // =========================================================

    private void ShowHome()
    {
        RemoveStudyBuilder();

        RemoveStudyWorkspace();

        RemoveStudiesView();


        _studyBeingEdited =
            null;


        SetSelectedNavigation(
            HomeButton);


        HomeContent.IsVisible =
            true;

        SettingsPanel.IsVisible = false;


        SectionPlaceholder.IsVisible =
            false;


        CurrentSectionTitle.Text =
            LocalizationService.IsArabic
                ? "نظرة عامة"
                : "Overview";


        CurrentSectionSubtitle.Text =
            LocalizationService.IsArabic
                ? "مساحة العمل الرئيسية"
                : "Main research workspace";
    }


    // =========================================================
    // SECTION
    // =========================================================

    private void ShowSection(
        Button selectedButton,
        string title,
        string description)
    {
        RemoveStudyBuilder();

        RemoveStudyWorkspace();

        RemoveStudiesView();


        _studyBeingEdited =
            null;


        SetSelectedNavigation(
            selectedButton);


        HomeContent.IsVisible =
            false;

        SettingsPanel.IsVisible = false;


        SectionPlaceholder.IsVisible =
            true;


        CurrentSectionTitle.Text =
            title;


        CurrentSectionSubtitle.Text =
            description;


        PlaceholderTitle.Text =
            title;


        PlaceholderDescription.Text =
            description;

        _placeholderDestination = selectedButton == ParticipantsButton
            ? "participants"
            : selectedButton == SessionsButton
                ? "sessions"
                : null;
        PlaceholderActionButton.IsVisible = _placeholderDestination is not null;
        PlaceholderActionButton.Content = _continueStudy is null
            ? Text("عرض الدراسات", "View studies")
            : _placeholderDestination == "participants"
                ? Text("فتح المشاركين", "Open participants")
                : Text("فتح الجلسات", "Open sessions");
    }


    private void ShowSettings()
    {
        RemoveStudyBuilder();
        RemoveStudyWorkspace();
        RemoveStudiesView();
        _studyBeingEdited = null;
        SetSelectedNavigation(SettingsButton);
        HomeContent.IsVisible = false;
        SectionPlaceholder.IsVisible = false;
        SettingsPanel.IsVisible = true;
        CurrentSectionTitle.Text = Text("الإعدادات", "Settings");
        CurrentSectionSubtitle.Text = Text(
            "اللغة والمظهر وإرشادات المنتج",
            "Language, appearance, and product guidance");
        ConfigureSettingsLanguage();
        _ = LoadCloudConfigurationAsync();
        _ = LoadAiServiceStatusAsync();
    }

    private async Task LoadAiServiceStatusAsync()
    {
        SettingsAiState.Text = Text("جار الاتصال", "Connecting");
        AiRefreshButton.IsEnabled = false;
        try
        {
            var status = await SocyviaAiService.GetStatusAsync();
            SettingsAiState.Text = SocyviaAiStatusPresentationService.StateLabel(status, LocalizationService.IsArabic);
            AiConnectionDetail.Text = SocyviaAiStatusPresentationService.Detail(status, LocalizationService.IsArabic);
        }
        finally
        {
            AiRefreshButton.IsEnabled = true;
        }
    }

    private async Task LoadCloudConfigurationAsync()
    {
        var configuration = await new CloudflareProviderConfigurationStore().LoadAsync();
        if (configuration is null)
        {
            CloudDisconnectQuickButton.IsVisible = false;
            CloudConnectButton.Content = Text("ربط Cloudflare", "Connect Cloudflare");
            CloudAccountSummary.Text = Text("غير متصل", "Not connected");
            CloudDatabaseSummary.Text = Text("غير متاحة", "Unavailable");
            CloudRuntimeSummary.Text = Text("غير متاحة", "Unavailable");
            CloudMediaSummary.Text = Text("اختياري", "Optional");
            CloudMediaSetupButton.IsVisible = false;
            return;
        }
        CloudDisconnectQuickButton.IsVisible = true;
        CloudConnectButton.Content = Text("إعادة ربط Cloudflare", "Reconnect Cloudflare");
        ApplyCloudConfigurationToUi(configuration);
        var oauthExpired = configuration.ConnectionMode == CloudflareConnectionMode.OAuth &&
                           configuration.OAuthExpiresAtUtc <= DateTime.UtcNow.AddMinutes(2);
        SettingsCloudState.Text = oauthExpired
            ? Text("يحتاج إلى اهتمام", "Needs Attention")
            : configuration.ProviderStatus is CloudflareProviderConnectionState.Connected or CloudflareProviderConnectionState.Ready
            ? Text("جاهز", "Ready")
            : configuration.ConnectionMode == CloudflareConnectionMode.OAuth
                ? Text("متصل — يلزم الإعداد", "Connected — Setup Required")
                : Text("غير متصل", "Not Connected");
        CloudConnectionDetail.Text = configuration.ConnectionMode == CloudflareConnectionMode.OAuth
            ? $"Cloudflare · {configuration.AccountDisplayName}"
            : CloudConnectionDetail.Text;
    }


    private void ConfigureSettingsLanguage()
    {
        SettingsPanelTitle.Text = Text("إعدادات SOCYVIA", "SOCYVIA settings");
        SettingsPanelSubtitle.Text = Text(
            "خصص تجربة الباحث دون تغيير البيانات العلمية.",
            "Personalize the researcher experience without changing scientific data.");
        SettingsLanguageTitle.Text = Text("اللغة", "Language");
        SettingsLanguageDescription.Text = Text(
            "غير اتجاه واجهة الباحث والنصوص.",
            "Change researcher interface language and direction.");
        SettingsAppearanceTitle.Text = Text("المظهر", "Appearance");
        SettingsAppearanceDescription.Text = Text(
            "تم إعداد البنية لوضعي النظام والداكن لاحقا.",
            "The groundwork supports future System and Dark modes.");
        SettingsAppearanceValue.Text = Text(
            "الواجهة العلمية",
            "Scientific Glass");
        SettingsTourTitle.Text = Text("جولة المنتج", "Product tour");
        SettingsTourDescription.Text = Text(
            "أعد تشغيل الجولة التي تشرح سير العمل الحالي.",
            "Replay the tour of the current research workflow.");
        SettingsCloudTitle.Text = Text("السحابة والتجارب عن بعد", "Cloud & Remote Experiments");
        SettingsCloudDescription.Text = Text(
            "يتطلب النشر العام موفر سحابي يملكه الباحث. لا يتم حفظ رموز الوصول في SQLite.",
            "Public publishing requires a researcher-owned cloud provider. Access tokens are never stored in SQLite.");
        SettingsCloudState.Text = Text("غير متصل", "Not Connected");
        SettingsAiTitle.Text = Text("SOCYVIA AI", "SOCYVIA AI");
        SettingsAiDescription.Text = Text(
            "مساعد SOCYVIA للإرشاد داخل المنتج وتفسير أدلة الدراسة الحتمية دون أن يكون بديلا عنها.",
            "A SOCYVIA assistant for product guidance and interpretation of deterministic study evidence, never a replacement for it.");
        SettingsAiState.Text = Text("جار الاتصال", "Connecting");
        AiRefreshButton.Content = Text("إعادة التحقق", "Check availability");
        SettingsUpdateTitle.Text = Text("تحديثات SOCYVIA", "SOCYVIA updates");
        SettingsUpdateDescription.Text = Text(
            "تتحقق التحديثات من مصدر HTTPS رسمي وتتحقق من SHA-256 قبل أي تثبيت بموافقة الباحث.",
            "Updates use an official HTTPS source and verify SHA-256 before any researcher-approved installation.");
        SettingsUpdateStatus.Text = Text("قناة مستقرة · لم يتم التحقق بعد", "Stable channel · not checked yet");
        CheckUpdatesButton.Content = Text("التحقق من التحديثات", "Check for updates");
        SettingsCloudSetupTitle.Text = Text("إعداد Cloudflare للتجارب عن بعد", "Set up Cloudflare for remote experiments");
        SettingsCloudSetupHelp.Text = Text("اربط Cloudflare مرة واحدة وسيكتشف SOCYVIA حسابك ويجهز قاعدة البحث وبيئة التشغيل تلقائيا. تبقى البنية والبيانات تحت حسابك.", "Connect Cloudflare once. SOCYVIA discovers your account and prepares the research database and experiment runtime automatically; infrastructure and data remain in your account.");
        CloudGuideSummary.Text = Text("اتصال واحد · إعداد تلقائي · دون معرفات يدوية", "One connection · automatic setup · no manual IDs");
        CloudGuideSteps.Text = Text("التفويض ← التحقق من الحساب ← قاعدة البحث ← بيئة التشغيل ← اختبار الاتصال ← جاهز. تخزين الوسائط اختياري ولا يتم إنشاؤه تلقائيا.", "Authorization → account check → research database → experiment runtime → connection test → Ready. Media storage is optional and is not created automatically.");
        CloudAccountSummaryLabel.Text = Text("Cloudflare", "Cloudflare");
        CloudDatabaseSummaryLabel.Text = Text("قاعدة البحث", "Research database");
        CloudRuntimeSummaryLabel.Text = Text("بيئة التجربة", "Experiment runtime");
        CloudMediaSummaryLabel.Text = Text("تخزين الوسائط", "Media storage");
        CloudAccountIdBox.PlaceholderText = Text("معرف حساب Cloudflare", "Cloudflare account ID");
        CloudD1IdBox.PlaceholderText = Text("معرف قاعدة بيانات البحث", "Research database ID");
        CloudWorkerEndpointBox.PlaceholderText = Text("رابط بيئة تشغيل التجربة", "Experiment runtime endpoint");
        CloudR2BucketBox.PlaceholderText = Text("تخزين الوسائط (اختياري للنص فقط)", "Media storage (optional for text-only)");
        CloudTokenBox.PlaceholderText = Text("رمز API محدود الصلاحيات", "Scoped API token");
        CloudConnectButton.Content = CloudDisconnectQuickButton.IsVisible
            ? Text("إعادة ربط Cloudflare", "Reconnect Cloudflare")
            : Text("ربط Cloudflare", "Connect Cloudflare");
        CloudRetryButton.Content = Text("إعادة المحاولة", "Retry setup");
        CloudMediaSetupButton.Content = Text("تخزين سحابي اختياري", "Optional cloud media storage");
        CloudOptionalMediaDisclosure.Text = Text(
            "اختياري: قد يتطلب تفعيل R2 الموافقة على فوترة حسب الاستخدام بعد تجاوز حدود Cloudflare المجانية. لا يلزم R2 عند استخدام روابط HTTPS عامة للوسائط.",
            "Optional: activating R2 may require accepting usage-based billing beyond Cloudflare's free allowance. R2 is not required when media uses public HTTPS URLs.");
        CloudDisconnectQuickButton.Content = Text("قطع الاتصال", "Disconnect");
        CloudAdvancedSetupButton.Content = Text("إعداد متقدم / يدوي", "Advanced / Manual configuration");
        CloudSaveButton.Content = Text("اتصال وحفظ آمن", "Connect / Save securely");
        CloudTestButton.Content = Text("اختبار الاتصال", "Test connection");
        CloudDisconnectButton.Content = Text("قطع الاتصال", "Disconnect");
        ReplayTourButton.Content = Text("إعادة الجولة", "Replay product tour");
        SettingsPanel.FlowDirection = LocalizationService.IsArabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
    }


    private void SetSelectedNavigation(
        Button selectedButton)
    {
        // Each top-level workspace owns a fresh visual reading position. Keeping
        // the previous page offset made newly opened surfaces appear clipped or
        // empty even though their content was present above the viewport.
        MainWorkspaceScrollViewer.Offset = new Vector(0, 0);

        var buttons =
            new[]
            {
                HomeButton,
                ContentLibraryButton,
                StudiesButton,
                ParticipantsButton,
                SessionsButton,
                DemoButton,
                AnalysisButton,
                ReportsButton,
                SocyviaAiButton,
                SettingsButton
            };


        foreach (var button in buttons)
        {
            button.Classes.Remove(
                "selected");
        }


        selectedButton.Classes.Add(
            "selected");
    }


    // =========================================================
    // STUDY STATUS
    // =========================================================

    private string GetLocalizedStudyStatus(
        string status)
    {
        if (!LocalizationService.IsArabic)
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
    // LANGUAGE
    // =========================================================

    private void ConfigureLanguage()
    {
        if (_researcher is null)
        {
            return;
        }

        RootDashboard.FlowDirection = LocalizationService.IsArabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        // GlobalHeaderGrid deliberately owns physical geometry in LTR so the
        // application flow direction cannot flip its manually mirrored zones.
        // Its text-bearing children still receive the active UI direction.
        ProfileButton.FlowDirection = RootDashboard.FlowDirection;
        SectionContextPanel.FlowDirection = RootDashboard.FlowDirection;

        if (LocalizationService.IsArabic)
        {
            ApplyArabicDashboard();
        }
        else
        {
            ApplyEnglishDashboard();
        }

        ConfigureCommandCenterLanguage();
        ConfigureConnectivityVisual();
        if (_socyviaAiProductHelpView is not null) ShowSocyviaAiProductHelp();
    }

    private Window? ResolveOwnerWindow() =>
        TopLevel.GetTopLevel(this) as Window ??
        VisualRoot as Window ??
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;


    private void ConfigureCommandCenterLanguage()
    {
        var hour = DateTime.Now.Hour;
        var greeting = LocalizationService.IsArabic
            ? hour < 12
                ? "صباح الخير"
                : hour < 18
                    ? "مساء الخير"
                    : "مساء الخير"
            : hour < 12
                ? "Good morning"
                : hour < 18
                    ? "Good afternoon"
                    : "Good evening";

        CommandGreetingText.Text = _researcher is null
            ? greeting
            : $"{greeting}, {_researcher.FullName}";
        CommandSubtitleText.Text = Text(
            "تابع دراساتك وجمع البيانات ومراقبة تقدم العمل من مساحة واحدة.",
            "Design, run, and observe controlled digital experiments from one research environment.");
        ContinueResearchLabel.Text = Text("متابعة البحث", "CONTINUE RESEARCH");
        // Provide an immediate localized label while asynchronous study data is loading.
        ContinueStudyActionText.Text = Text("فتح الدراسة", "Open Study");
        ContinueStudyActionText.IsVisible = true;
        QuickActionsTitle.Text = Text("إجراءات سريعة", "Quick actions");
        AddParticipantsQuickText.Text = Text("إضافة مشاركين", "Add participants");
        PrepareSessionQuickText.Text = Text("تحضير جلسة", "Prepare session");

        if (LocalizationService.IsArabic)
        {
            Grid.SetColumn(CommandGreetingPanel, 0);
            CommandGreetingPanel.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(ContinueStudyInformationPanel, 1);
            Grid.SetColumn(ContinueStudyActionPanel, 0);
            ContinueStudyInformationPanel.HorizontalAlignment = HorizontalAlignment.Right;
            ContinueStudyActionPanel.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(AttentionCountBadge, 0);
            Grid.SetColumn(AttentionTextPanel, 1);
            AttentionCountBadge.HorizontalAlignment = HorizontalAlignment.Left;
            AttentionTextPanel.HorizontalAlignment = HorizontalAlignment.Right;
            AttentionTitle.TextAlignment = TextAlignment.Right;
            AttentionSubtitle.TextAlignment = TextAlignment.Right;
        }
        else
        {
            Grid.SetColumn(CommandGreetingPanel, 0);
            CommandGreetingPanel.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(ContinueStudyInformationPanel, 0);
            Grid.SetColumn(ContinueStudyActionPanel, 1);
            ContinueStudyInformationPanel.HorizontalAlignment = HorizontalAlignment.Left;
            ContinueStudyActionPanel.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(AttentionTextPanel, 0);
            Grid.SetColumn(AttentionCountBadge, 1);
            AttentionCountBadge.HorizontalAlignment = HorizontalAlignment.Right;
            AttentionTextPanel.HorizontalAlignment = HorizontalAlignment.Left;
            AttentionTitle.TextAlignment = TextAlignment.Left;
            AttentionSubtitle.TextAlignment = TextAlignment.Left;
        }

        var direction = LocalizationService.IsArabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        CommandGreetingText.FlowDirection = direction;
        CommandSubtitleText.FlowDirection = direction;
        ContinueResearchLabel.FlowDirection = direction;
        ContinueStudyReadiness.FlowDirection = direction;
        ContinueStudyTitle.FlowDirection = direction;
        ContinueStudyDetail.FlowDirection = direction;
        ContinueStudyMetadataPanel.FlowDirection = direction;
        CommandGreetingText.TextAlignment = LocalizationService.IsArabic
            ? TextAlignment.Right
            : TextAlignment.Left;
        CommandSubtitleText.TextAlignment = LocalizationService.IsArabic
            ? TextAlignment.Right
            : TextAlignment.Left;
        ContinueResearchLabel.TextAlignment = LocalizationService.IsArabic
            ? TextAlignment.Right
            : TextAlignment.Left;
        ContinueResearchLabel.HorizontalAlignment = HorizontalAlignment.Stretch;
        ContinueStudyReadiness.TextAlignment = LocalizationService.IsArabic
            ? TextAlignment.Right
            : TextAlignment.Left;
        ContinueStudyReadiness.HorizontalAlignment = LocalizationService.IsArabic
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
        ContinueStudyTitle.TextAlignment = LocalizationService.IsArabic
            ? TextAlignment.Right
            : TextAlignment.Left;
        ContinueStudyTitle.HorizontalAlignment = HorizontalAlignment.Stretch;
        ContinueStudyDetail.TextAlignment = LocalizationService.IsArabic
            ? TextAlignment.Right
            : TextAlignment.Left;
        ContinueStudyDetail.HorizontalAlignment = HorizontalAlignment.Stretch;
        ContinueStudyActionText.FlowDirection = direction;
        ContinueStudyActionText.TextAlignment = TextAlignment.Center;
        ContinueStudyActionText.HorizontalAlignment = HorizontalAlignment.Stretch;

        if (SettingsPanel.IsVisible)
        {
            ConfigureSettingsLanguage();
        }
    }


    // =========================================================
    // ARABIC
    // =========================================================

    private void ApplyArabicDashboard()
    {
        DashboardRootGrid.ColumnDefinitions =
            new ColumnDefinitions(
                "*,216");


        Grid.SetColumn(
            MainWorkspaceRoot,
            0);


        Grid.SetColumn(
            SidebarRoot,
            1);

        Grid.SetColumn(ProfileButton, 1);
        Grid.SetColumn(SectionContextPanel, 0);
        ProfileButton.HorizontalAlignment = HorizontalAlignment.Right;
        SectionContextPanel.HorizontalAlignment = HorizontalAlignment.Left;


        SidebarRoot.BorderThickness =
            new Thickness(
                1,
                0,
                0,
                0);


        ResearcherRoleText.FontFamily =
            _arabicFont;


        ResearcherRoleText.Text =
            "الباحث";


        ProfileMenuRoleText.FontFamily =
            _arabicFont;


        ProfileMenuRoleText.Text =
            "الباحث";


        ProfileSettingsText.FontFamily =
            _arabicFont;


        ProfileSettingsText.Text =
            "إعدادات الباحث";


        LogoutText.FontFamily =
            _arabicFont;


        LogoutText.Text =
            "تسجيل الخروج";


        WelcomeWordText.FontFamily =
            _arabicFont;


        WelcomeWordText.Text =
            "مرحبا";


        WelcomeNameText.Text =
            _researcher!.FullName;


        WelcomePanel.HorizontalAlignment =
            HorizontalAlignment.Right;


        WelcomeLinePanel.HorizontalAlignment =
            HorizontalAlignment.Right;


        WelcomeSubtitleText.FontFamily =
            _arabicFont;


        WelcomeSubtitleText.Text =
            "تابع مشاريعك البحثية أو ابدأ دراسة جديدة";


        WelcomeSubtitleText.HorizontalAlignment =
            HorizontalAlignment.Right;


        WelcomeSubtitleText.TextAlignment =
            TextAlignment.Right;


        WorkspaceLabel.FontFamily =
            _arabicFont;


        WorkspaceLabel.Text =
            "SOCYVIA";


        NavigationLabel.FontFamily =
            _arabicFont;


        NavigationLabel.Text =
            "مساحة العمل";


        NavigationLabel.HorizontalAlignment =
            HorizontalAlignment.Center;


        NavigationLabel.TextAlignment =
            TextAlignment.Center;


        SetArabicNavText(
            HomeButtonText,
            "نظرة عامة");

        SetArabicNavText(
            ContentLibraryButtonText,
            "مكتبة المحتوى");


        SetArabicNavText(
            StudiesButtonText,
            "الدراسات");


        SetArabicNavText(
            ParticipantsButtonText,
            "المشاركون");

        SetArabicNavText(
            SessionsButtonText,
            "الجلسات");

        SetArabicNavText(DemoButtonText, "العرض التجريبي العام");
        SetArabicNavText(DemoPermanentText, "اصطناعي");
        SetArabicCenterText(ResearchNavigationLabel, "البحث");
        SetArabicCenterText(IntelligenceNavigationLabel, "الذكاء");
        SetArabicCenterText(SystemNavigationLabel, "النظام");


        SetArabicNavText(
            AnalysisButtonText,
            "التحليل");


        SetArabicNavText(
            ReportsButtonText,
            "التقارير");

        SetArabicNavText(
            SocyviaAiButtonText,
            "SOCYVIA AI");

        SetArabicNavText(
            SocyviaAiPreviewText,
            "بحثي");


        SetArabicNavText(
            SettingsButtonText,
            "الإعدادات");


        SetArabicText(
            CurrentSectionTitle,
            "نظرة عامة");


        SetArabicText(
            CurrentSectionSubtitle,
            "مساحة العمل الرئيسية");


        SetArabicButtonText(
            NewStudyButtonText,
            "+ دراسة جديدة");


        SetArabicButtonText(
            ImportDataButtonText,
            "إضافة محتوى");


        // =====================================================
        // METRICS = CENTERED
        // =====================================================

        SetArabicCenterText(
            StudiesMetricLabel,
            "الدراسات النشطة");


        SetArabicCenterText(
            StudiesMetricHint,
            "المشاريع البحثية");


        SetArabicCenterText(
            ParticipantsMetricLabel,
            "المشاركون");


        SetArabicCenterText(
            ParticipantsMetricHint,
            "في الدراسات النشطة");


        SetArabicCenterText(
            SessionsMetricLabel,
            "الجلسات");


        SetArabicCenterText(
            SessionsMetricHint,
            "الجلسات البحثية");


        StudiesCountText.HorizontalAlignment =
            HorizontalAlignment.Center;


        StudiesCountText.TextAlignment =
            TextAlignment.Center;


        ParticipantsCountText.HorizontalAlignment =
            HorizontalAlignment.Center;


        ParticipantsCountText.TextAlignment =
            TextAlignment.Center;


        SessionsCountText.HorizontalAlignment =
            HorizontalAlignment.Center;


        SessionsCountText.TextAlignment =
            TextAlignment.Center;


        SetArabicRightText(
            RecentStudiesTitle,
            "أحدث الدراسات");


        SetArabicRightText(
            RecentStudiesSubtitle,
            "المشاريع البحثية التي عملت عليها مؤخرا");


        SetArabicButtonText(
            ViewAllStudiesText,
            "عرض الكل");


        SetArabicButtonText(
            EmptyStudiesTitle,
            "لا توجد دراسات بعد");


        SetArabicButtonText(
            EmptyStudiesText,
            "ابدأ بإنشاء أول دراسة بحثية");


        SetArabicButtonText(
            EmptyCreateStudyText,
            "+ إنشاء");


        var versionText =
            VersionText.Text
            ?? string.Empty;


        if (versionText.StartsWith(
                "Version ",
                StringComparison.Ordinal))
        {
            VersionText.Text =
                "الإصدار " +
                versionText[
                "Version ".Length..];
        }

        SetArabicNavText(ContentLibraryButtonText, "المحتوى والوسائط");
        SetArabicNavText(StudiesButtonText, "المشاريع البحثية");
        SetArabicNavText(ReportsButtonText, "التقارير");
    }


    // =========================================================
    // ENGLISH
    // =========================================================

    private void ApplyEnglishDashboard()
    {
        DashboardRootGrid.ColumnDefinitions =
            new ColumnDefinitions(
                "216,*");


        Grid.SetColumn(
            SidebarRoot,
            0);


        Grid.SetColumn(
            MainWorkspaceRoot,
            1);

        Grid.SetColumn(SectionContextPanel, 1);
        Grid.SetColumn(ProfileButton, 0);
        SectionContextPanel.HorizontalAlignment = HorizontalAlignment.Right;
        ProfileButton.HorizontalAlignment = HorizontalAlignment.Left;


        SidebarRoot.BorderThickness =
            new Thickness(
                0,
                0,
                1,
                0);


        ResearcherRoleText.FontFamily =
            _englishFont;


        ResearcherRoleText.Text =
            "Researcher";


        ProfileMenuRoleText.FontFamily =
            _englishFont;


        ProfileMenuRoleText.Text =
            "Researcher";


        ProfileSettingsText.FontFamily =
            _englishFont;


        ProfileSettingsText.Text =
            "Researcher settings";


        LogoutText.FontFamily =
            _englishFont;


        LogoutText.Text =
            "Sign out";


        WelcomeWordText.FontFamily =
            _englishFont;


        WelcomeWordText.Text =
            "Welcome";


        WelcomeNameText.Text =
            _researcher!.FullName;


        WelcomePanel.HorizontalAlignment =
            HorizontalAlignment.Left;


        WelcomeLinePanel.HorizontalAlignment =
            HorizontalAlignment.Left;


        WelcomeSubtitleText.FontFamily =
            _englishFont;


        WelcomeSubtitleText.Text =
            "Continue your research or start a new study";


        WelcomeSubtitleText.HorizontalAlignment =
            HorizontalAlignment.Left;


        WelcomeSubtitleText.TextAlignment =
            TextAlignment.Left;


        WorkspaceLabel.FontFamily =
            _englishFont;


        WorkspaceLabel.Text =
            "SOCYVIA";


        NavigationLabel.FontFamily =
            _englishFont;


        NavigationLabel.Text =
            "WORKSPACE";


        NavigationLabel.HorizontalAlignment =
            HorizontalAlignment.Center;


        NavigationLabel.TextAlignment =
            TextAlignment.Center;


        SetEnglishNavText(
            HomeButtonText,
            "Overview");

        SetEnglishNavText(
            ContentLibraryButtonText,
            "Content & Media");


        SetEnglishNavText(
            StudiesButtonText,
            "Research Projects");


        SetEnglishNavText(
            ParticipantsButtonText,
            "Participants");

        SetEnglishNavText(
            SessionsButtonText,
            "Sessions");

        SetEnglishNavText(DemoButtonText, "Public Demo");
        SetEnglishNavText(DemoPermanentText, "SYNTHETIC");
        SetEnglishCenterText(ResearchNavigationLabel, "RESEARCH");
        SetEnglishCenterText(IntelligenceNavigationLabel, "INTELLIGENCE");
        SetEnglishCenterText(SystemNavigationLabel, "SYSTEM");


        SetEnglishNavText(
            AnalysisButtonText,
            "Analysis");


        SetEnglishNavText(
            ReportsButtonText,
            "Reports");

        SetEnglishNavText(
            SocyviaAiButtonText,
            "SOCYVIA AI");

        SetEnglishNavText(
            SocyviaAiPreviewText,
            "SCIENTIFIC");


        SetEnglishNavText(
            SettingsButtonText,
            "Settings");


        SetEnglishText(
            CurrentSectionTitle,
            "Overview");


        SetEnglishText(
            CurrentSectionSubtitle,
            "Main research workspace");


        SetEnglishButtonText(
            NewStudyButtonText,
            "+ New Study");


        SetEnglishButtonText(
            ImportDataButtonText,
            "Add content");


        // =====================================================
        // METRICS = CENTERED
        // =====================================================

        SetEnglishCenterText(
            StudiesMetricLabel,
            "Active studies");


        SetEnglishCenterText(
            StudiesMetricHint,
            "Research projects");


        SetEnglishCenterText(
            ParticipantsMetricLabel,
            "Participants");


        SetEnglishCenterText(
            ParticipantsMetricHint,
            "Across active studies");


        SetEnglishCenterText(
            SessionsMetricLabel,
            "Sessions");


        SetEnglishCenterText(
            SessionsMetricHint,
            "Research sessions");


        StudiesCountText.HorizontalAlignment =
            HorizontalAlignment.Center;


        StudiesCountText.TextAlignment =
            TextAlignment.Center;


        ParticipantsCountText.HorizontalAlignment =
            HorizontalAlignment.Center;


        ParticipantsCountText.TextAlignment =
            TextAlignment.Center;


        SessionsCountText.HorizontalAlignment =
            HorizontalAlignment.Center;


        SessionsCountText.TextAlignment =
            TextAlignment.Center;


        // English long text = LEFT

        SetEnglishText(
            RecentStudiesTitle,
            "Recent studies");


        SetEnglishText(
            RecentStudiesSubtitle,
            "Research projects you worked on recently");


        SetEnglishButtonText(
            ViewAllStudiesText,
            "View all");


        SetEnglishButtonText(
            EmptyStudiesTitle,
            "No studies yet");


        SetEnglishButtonText(
            EmptyStudiesText,
            "Create your first research study");


        SetEnglishButtonText(
            EmptyCreateStudyText,
            "+ Create");


        var versionText =
            VersionText.Text
            ?? string.Empty;


        if (versionText.StartsWith(
                "الإصدار ",
                StringComparison.Ordinal))
        {
            VersionText.Text =
                "Version " +
                versionText[
                    "الإصدار ".Length..];
        }
    }


    // =========================================================
    // HELPERS
    // =========================================================

    private void SetArabicNavText(
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


    private void SetEnglishNavText(
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


    private void SetArabicText(
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


    private void SetEnglishText(
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


    private void SetArabicButtonText(
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
            TextAlignment.Center;
    }


    private void SetEnglishButtonText(
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
            TextAlignment.Center;
    }


    // =========================================================
    // CENTERED SMALL ELEMENTS
    // =========================================================

    private string DisplayStudyTitle(Study study)
    {
        var title = study.Title?.Trim() ?? string.Empty;
        return title.Any(char.IsLetterOrDigit)
            ? title
            : Text("دراسة بلا عنوان", "Untitled study");
    }

    private static bool IsDemoStudy(Study study) => DemoAccessPolicy.IsDemoStudy(study);


    private void SetArabicCenterText(
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
            TextAlignment.Center;


        textBlock.HorizontalAlignment =
            HorizontalAlignment.Center;
    }


    private void SetEnglishCenterText(
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
            TextAlignment.Center;


        textBlock.HorizontalAlignment =
            HorizontalAlignment.Center;
    }


    private void SetArabicRightText(
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


    private void SetEnglishRightText(
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
            TextAlignment.Right;


        textBlock.HorizontalAlignment =
            HorizontalAlignment.Right;
    }
}
