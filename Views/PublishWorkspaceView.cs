using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SOCYVIA.Models;
using SOCYVIA.Repositories;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

/// <summary>
/// The study-scoped deployment surface. It reports only persisted publication
/// outcomes; a participant link is never manufactured for a draft study.
/// </summary>
public sealed class PublishWorkspaceView : UserControl
{
    private readonly Study _study;
    private readonly StackPanel _body = new() { Spacing = 14 };
    private readonly TextBlock _status = new() { Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap };
    private bool _isPublishing;
    private string? _publicationFailure;

    public PublishWorkspaceView(Study study)
    {
        _study = study;
        FlowDirection = LocalizationService.IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var title = new TextBlock { Text = T("النشر", "Publish"), Classes = { "pageTitle" }, HorizontalAlignment = HorizontalAlignment.Stretch, TextAlignment = Alignment };
        var subtitle = new TextBlock
        {
            Text = T("تحقق من جاهزية الدراسة والنشر السحابي قبل توزيع رابط المشاركين المباشر.", "Review study and cloud readiness before distributing a live participant link."),
            Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch, TextAlignment = Alignment
        };
        Content = new ScrollViewer { Content = new StackPanel { Spacing = 16, Children = { title, subtitle, _status, _body } } };
        AttachedToVisualTree += async (_, _) => await ReloadAsync();
    }

    public event EventHandler? CloudSettingsRequested;
    public event Action<string?>? MediaUrlsSetupRequested;
    public event EventHandler? PilotDataRequested;

    public async Task ReloadAsync()
    {
        _body.Children.Clear();
        _status.Text = string.Empty;
        try
        {
            var preparedTask = RemotePublicationPreparationService.PrepareAsync(_study);
            var cloudTask = new CloudflareProviderConfigurationStore().LoadAsync();
            var publishedTask = PublishedExperimentStatusStore.GetAsync(_study.Id);
            var sessionsTask = RemoteResearchRepository.GetSessionsAsync(studyId: _study.Id);
            await Task.WhenAll(preparedTask, cloudTask, publishedTask, sessionsTask);

            var cloud = cloudTask.Result;
            var readiness = await PublicationReadinessService.EvaluateAsync(_study, preparedTask.Result, cloud);
            var published = publishedTask.Result;
            var currentPublication = published is not null &&
                                     published.PublishedAtUtc.HasValue &&
                                     string.Equals(published.ConfigurationHash, preparedTask.Result.Package.ConfigurationHash, StringComparison.Ordinal) &&
                                     Uri.TryCreate(published.CanonicalParticipantUrl, UriKind.Absolute, out var participantUri) &&
                                     participantUri.Scheme == Uri.UriSchemeHttps;

            var state = PublicationWorkspaceStateService.Resolve(
                readiness, published, currentPublication, _isPublishing, _publicationFailure);
            var actionPanel = state switch
            {
                PublicationWorkspaceState.Publishing => PublishingCard(),
                PublicationWorkspaceState.Failed => PublicationFailureCard(_publicationFailure!),
                PublicationWorkspaceState.Published or PublicationWorkspaceState.PublishedAwaitingCanonicalRoute => PublishedCard(published!),
                _ => PublishReadyCard(readiness)
            };
            _body.Children.Add(actionPanel);

            if (state == PublicationWorkspaceState.NotReady &&
                (!readiness.AccountReady || !readiness.DatabaseReady || !readiness.RuntimeReady || !readiness.MediaReady))
                _body.Children.Add(CloudSetupCard(readiness));

            _body.Children.Add(ReadinessCard(readiness, cloud));
            if (currentPublication)
            {
                _body.Children.Add(PilotCard(published!));
                _body.Children.Add(RecruitmentSummaryCard(published!, sessionsTask.Result));
            }
            _body.Children.Add(ResearchDataCard());
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Load publish workspace");
            _body.Children.Add(Card(new TextBlock { Text = T("تعذر تحميل حالة النشر. راجع إعدادات الدراسة ثم أعد المحاولة.", "The publication state could not be loaded. Review the study settings and try again."), Foreground = Brush("#B8384F"), TextWrapping = TextWrapping.Wrap }, "errorCard"));
        }
    }

    private Control ReadinessCard(PublicationReadinessResult readiness, CloudflareProviderConfiguration? cloud)
    {
        var panel = new StackPanel { Spacing = 9 };
        panel.Children.Add(Heading(T("جاهزية النشر", "Publishing readiness")));
        panel.Children.Add(Line(T("جاهزية الدراسة", "Study readiness"), readiness.StudyReady ? T("جاهزة", "Ready") : T("تحتاج إلى اهتمام", "Needs attention"), readiness.StudyReady));
        panel.Children.Add(Line("Cloudflare", readiness.AccountReady ? T("متصل", "Connected") : T("غير متصل", "Not Connected"), readiness.AccountReady));
        if (readiness.AccountReady && !string.IsNullOrWhiteSpace(cloud?.AccountDisplayName))
            panel.Children.Add(Line(T("الحساب", "Account"), cloud!.AccountDisplayName, true));
        panel.Children.Add(Line(T("قاعدة بيانات البحث", "Research Database"),
            readiness.DatabaseReady ? T("جاهزة", "Ready") : !string.IsNullOrWhiteSpace(cloud?.D1DatabaseId) ? T("يلزم التحقق", "Needs Verification") : T("يلزم الإعداد", "Needs Setup"),
            readiness.DatabaseReady));
        panel.Children.Add(Line(T("التجارب البعيدة", "Remote Experiments"),
            readiness.RuntimeReady ? T("جاهزة", "Ready") : !string.IsNullOrWhiteSpace(cloud?.WorkerEndpoint) ? T("يلزم التحقق", "Needs Verification") : T("يلزم الإعداد", "Needs Setup"),
            readiness.RuntimeReady));
        panel.Children.Add(Line(T("وسائط النشر", "Deployment media"), LocalizeMedia(readiness.Media), readiness.MediaReady));
        return Card(panel, readiness.CanPublish ? "successCard" : "warningCard");
    }

    private Control CloudSetupCard(PublicationReadinessResult readiness)
    {
        var mediaUrlMissing = readiness.Media.State == CloudMediaReadinessState.RemoteMediaUrlMissing;
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Heading(mediaUrlMissing
            ? T("يجب إعداد مصادر الوسائط للنشر قبل نشر هذه التجربة", "Media sources must be configured before publishing this experiment")
            : T("إعداد النشر مطلوب", "Publishing setup required")));
        panel.Children.Add(new TextBlock { Text = mediaUrlMissing ? T("الملف موجود على جهازك فقط. أضف رابط HTTPS للصورة أو الفيديو أو الصوت حتى يتمكن المشاركون من الوصول إليه بعد نشر التجربة.", "This file currently exists only on your device. Add an HTTPS URL so participants can access it after publication.") : T("لإنشاء رابط مباشر للمشاركين، يجب أولا ربط بيئة Cloudflare الخاصة بك.", "To generate a live participant link, first connect your Cloudflare environment."), TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch, TextAlignment = Alignment });
        var open = Primary(mediaUrlMissing ? T("إعداد روابط الوسائط", "Set media URLs") : T("ربط Cloudflare", "Connect Cloudflare"));
        open.Click += (_, _) =>
        {
            if (mediaUrlMissing)
                MediaUrlsSetupRequested?.Invoke(readiness.Media.RequiredAssetCount > 0
                    ? readiness.Media.MediaAssetContentId
                    : null);
            else CloudSettingsRequested?.Invoke(this, EventArgs.Empty);
        };
        panel.Children.Add(open);
        return Card(panel, "warningCard");
    }

    private Control PublishReadyCard(PublicationReadinessResult readiness)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Heading(readiness.CanPublish ? T("الدراسة جاهزة للنشر", "Study ready to publish") : T("يلزم استكمال الجاهزية قبل النشر", "Publishing readiness needs attention")));
        panel.Children.Add(new TextBlock { Text = readiness.CanPublish ? T("اكتملت فحوصات الجاهزية المحلية. سيظهر رابط المشاركين المباشر فقط بعد نشر سحابي ناجح.", "Local readiness checks are complete. A live participant link appears only after a successful cloud publication.") : BlockingReason(readiness), TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch, TextAlignment = Alignment });
        var publish = Primary(T("نشر التجربة", "Publish Experiment"));
        publish.IsEnabled = readiness.CanPublish;
        publish.Click += async (_, _) => await PublishAsync(publish);
        panel.Children.Add(publish);
        return Card(panel, readiness.CanPublish ? "successCard" : "warningCard");
    }

    private async Task PublishAsync(Button button)
    {
        if (_isPublishing) return;
        _isPublishing = true;
        _publicationFailure = null;
        button.IsEnabled = false;
        _status.Text = string.Empty;
        _body.Children.Clear();
        _body.Children.Add(PublishingCard());
        try
        {
            if (!await StudySaveCoordinatorRegistry.FlushAsync(_study.Id))
            {
                await ShowPublicationFailureAsync(T("تعذر حفظ تغييرات الدراسة. تم إيقاف النشر لحماية سلامة الإصدار.", "Study changes could not be saved. Publishing was blocked to protect version integrity."));
                return;
            }

            var configuration = await new CloudflareProviderConfigurationStore().LoadAsync();
            var token = configuration is null ? null : await new CloudflareOAuthConnectionService().GetAccessTokenAsync(
                configuration, CloudflareOAuthClientConfiguration.LoadReleaseConfiguration());
            if (configuration is null || string.IsNullOrWhiteSpace(token))
            {
                await ShowPublicationFailureAsync(T("اربط Cloudflare أو أعد الاتصال قبل النشر.", "Connect or reconnect Cloudflare before publishing."));
                CloudSettingsRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            var prepared = await RemotePublicationPreparationService.PrepareAsync(_study);
            var requiresMedia = prepared.Package.MediaManifest.Any(item => item.RequiredForDeployment);
            var inspection = await new CloudflareConnectionService().InspectAsync(configuration, token, requiresMedia);
            configuration = configuration with
            {
                ProviderStatus = inspection.State,
                LastVerifiedAtUtc = DateTime.UtcNow
            };
            await new CloudflareProviderConfigurationStore().SaveAsync(configuration);
            if (inspection.State != CloudflareProviderConnectionState.Ready)
            {
                await ShowPublicationFailureAsync(inspection.Message);
                return;
            }
            var readiness = await ResearcherPublishValidationService.EvaluateAsync(
                _study, prepared.Entry, prepared.Content, prepared.Questionnaires,
                configuration, prepared.Package.MediaManifest);
            if (!readiness.IsReady)
            {
                await ShowPublicationFailureAsync(string.Join(Environment.NewLine,
                    readiness.Checks.Where(item => !item.IsReady).Select(item => $"{item.Area}: {item.Message}")));
                return;
            }

            var researcher = ResearcherService.GetProfile(_study.ResearcherId);
            var deployment = RemoteExperimentFoundationService.CreateDraftDeployment(
                prepared.Package, researcher?.FullName ?? "researcher");
            _status.Text = T("جار نشر التجربة...", "Publishing experiment...");
            var provider = new CloudflareRemoteProvider();
            var result = prepared.Package.MediaManifest.Any(item => item.RequiredForDeployment)
                ? await provider.PublishAsync(configuration, token, prepared.Package, deployment,
                    prepared.Entry, prepared.Content, prepared.Questionnaires)
                : await provider.PublishTextOnlyAsync(configuration, token, prepared.Package, deployment,
                    prepared.Entry, prepared.Content, prepared.Questionnaires);
            if (!result.Succeeded)
            {
                await ShowPublicationFailureAsync(result.Error ?? T("تعذر إكمال النشر. يمكن إعادة المحاولة بأمان.", "Publishing could not be completed and can be retried safely."));
                return;
            }

            var canonical = result.CanonicalParticipantLink;
            if (result.Deployment is null || canonical is null ||
                !Uri.TryCreate(result.ParticipantUrl, UriKind.Absolute, out var runtimeUri) ||
                runtimeUri.Scheme != Uri.UriSchemeHttps)
            {
                await ShowPublicationFailureAsync(T("اكتمل النشر البعيد، لكن تعذر تأكيد رابط المشاركين. أعد المحاولة بأمان.", "Remote publication completed, but the participant link could not be confirmed. Retry safely."));
                return;
            }

            // The provider persists this outcome as part of the confirmed remote
            // publication. Save it again idempotently so the success panel cannot be
            // lost if the first local metadata write was interrupted.
            await PublishedExperimentStatusStore.SaveAsync(result.Deployment, runtimeUri);
            _publicationFailure = null;
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Publish study");
            await ShowPublicationFailureAsync(T("تعذر إكمال النشر. لم يتم إنشاء رابط عام غير مؤكد.", "Publishing could not be completed. No unverified public link was created."));
        }
        finally
        {
            _isPublishing = false;
            button.IsEnabled = true;
        }
    }

    private Control PublishingCard()
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Heading(T("جار نشر التجربة...", "Publishing experiment...")));
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 4 });
        panel.Children.Add(new TextBlock
        {
            Text = T("يتم إنشاء النسخة السحابية والتحقق منها قبل إظهار رابط المشاركين.", "SOCYVIA is creating and verifying the cloud publication before showing a participant link."),
            Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap
        });
        return Card(panel, "statusCard");
    }

    private Control PublicationFailureCard(string message)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Heading(T("تعذر نشر التجربة", "Experiment publication failed")));
        panel.Children.Add(new TextBlock { Text = message, Foreground = Brush("#B8384F"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = T("لم يتم حذف بيانات الدراسة أو تغيير اتصال Cloudflare.", "Study data and the Cloudflare connection were not deleted or reset."),
            Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap
        });
        var retry = Primary(T("إعادة المحاولة", "Retry"));
        retry.Click += async (_, _) => await PublishAsync(retry);
        panel.Children.Add(retry);
        return Card(panel, "errorCard");
    }

    private async Task ShowPublicationFailureAsync(string message)
    {
        _publicationFailure = string.IsNullOrWhiteSpace(message)
            ? T("تعذر إكمال النشر. يمكن إعادة المحاولة بأمان.", "Publishing could not be completed and can be retried safely.")
            : message;
        _isPublishing = false;
        await ReloadAsync();
    }

    private Control PublishedCard(PublishedExperimentStatus published)
    {
        var distributableUri = PublicExperimentLinkService.DistributableCanonicalUri(published);
        var canonicalRouteLive = distributableUri is not null;
        var distributableUrl = distributableUri?.AbsoluteUri ?? published.CanonicalParticipantUrl;
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Heading(canonicalRouteLive
            ? T("تم نشر التجربة بنجاح", "Experiment published successfully")
            : T("اكتمل النشر السحابي", "Cloud publication complete")));
        panel.Children.Add(new TextBlock
        {
            Text = canonicalRouteLive
                ? T("رابط المشاركين", "Participant link")
                : T("الرابط العام النهائي غير مفعل بعد. لا توزع الرابط التالي على المشاركين.", "CANONICAL ROUTE NOT LIVE — do not distribute the following reserved link to participants."),
            Classes = { "metadata" }, HorizontalAlignment = HorizontalAlignment.Stretch, TextAlignment = Alignment,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock { Text = published.CanonicalParticipantUrl, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.LeftToRight, TextAlignment = TextAlignment.Left });
        panel.Children.Add(new TextBlock { Text = $"{T("إصدار النشر", "Deployment version")}: {published.DeploymentVersion}  ·  {T("آخر نشر", "Published")}: {published.PublishedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—"}", Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, HorizontalAlignment = LocalizationService.IsArabic ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        var copy = new Button { Content = T("نسخ الرابط", "Copy Link") };
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await ParticipantLinkActionService.CopyAsync(published, value => clipboard.SetTextAsync(value));
        };
        var open = Primary(T("فتح التجربة", "Open experiment"));
        open.Click += (_, _) => ParticipantLinkActionService.Open(published);
        var verify = new Button { Content = T("التحقق من النشر", "Verify Deployment") };
        verify.Click += (_, _) => Open(published.RuntimeParticipantUrl);
        var qr = new Button { Content = T("إظهار رمز QR", "Show QR") };
        qr.Click += (_, _) => ShowQrCode(distributableUrl);
        var recruitment = new Button { Content = published.IsRecruitmentPaused ? T("استئناف التجنيد", "Resume Recruitment") : T("إيقاف التجنيد مؤقتا", "Pause Recruitment") };
        recruitment.Click += async (_, _) => await SetRecruitmentPausedAsync(published, !published.IsRecruitmentPaused);
        copy.IsEnabled = canonicalRouteLive;
        open.IsEnabled = canonicalRouteLive;
        qr.IsEnabled = canonicalRouteLive;
        if (canonicalRouteLive) { actions.Children.Add(copy); actions.Children.Add(open); }
        actions.Children.Add(verify);
        if (canonicalRouteLive) actions.Children.Add(qr);
        actions.Children.Add(recruitment);
        panel.Children.Add(actions);
        return Card(panel, "successCard");
    }

    private Control PilotCard(PublishedExperimentStatus published)
    {
        var panel = new StackPanel { Spacing = 9 };
        panel.Children.Add(Heading(T("التجربة الاستطلاعية", "Pilot")));
        var pilotPredatesCurrentStudy = published.PilotState == PilotLifecycleState.Completed &&
                                        ((published.PilotCompletedAtUtc.HasValue && _study.UpdatedAtUtc > published.PilotCompletedAtUtc.Value) ||
                                         (!string.IsNullOrWhiteSpace(published.PilotConfigurationHash) && !string.Equals(published.PilotConfigurationHash, published.ConfigurationHash, StringComparison.Ordinal)));
        var state = published.PilotState switch
        {
            PilotLifecycleState.Running => T("التجربة الاستطلاعية قيد التشغيل", "Pilot Running"),
            PilotLifecycleState.Completed when pilotPredatesCurrentStudy => T("تم تنفيذ التجربة الاستطلاعية على إصدار سابق من الدراسة", "Pilot was completed on an earlier study version"),
            PilotLifecycleState.Completed => T("اكتملت التجربة الاستطلاعية", "Pilot Completed"),
            _ => T("لم تبدأ التجربة الاستطلاعية", "Pilot Not Started")
        };
        panel.Children.Add(new TextBlock { Text = state, Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch, TextAlignment = Alignment });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, HorizontalAlignment = LocalizationService.IsArabic ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        var view = new Button { Content = T("عرض بيانات التجربة الاستطلاعية", "View Pilot Data"), IsEnabled = published.PilotState != PilotLifecycleState.NotStarted };
        view.Click += (_, _) => PilotDataRequested?.Invoke(this, EventArgs.Empty);
        actions.Children.Add(view);
        if (published.PilotState == PilotLifecycleState.Running)
        {
            var end = Primary(T("إنهاء التجربة الاستطلاعية", "End Pilot"));
            end.Click += async (_, _) => await ChangePilotStateAsync(published, PilotLifecycleState.Completed);
            actions.Children.Add(end);
        }
        else if (published.PilotState == PilotLifecycleState.NotStarted)
        {
            var run = Primary(T("تشغيل تجربة استطلاعية", "Run Pilot"));
            run.Click += async (_, _) => await ChangePilotStateAsync(published, PilotLifecycleState.Running);
            actions.Children.Add(run);
        }
        else if (!published.IsMainRecruitmentStarted)
        {
            var main = Primary(T("بدء تجنيد الدراسة الرئيسية", "Start Main Recruitment"));
            main.Click += async (_, _) => await StartMainRecruitmentAsync(published);
            actions.Children.Add(main);
        }
        panel.Children.Add(actions);
        return Card(panel);
    }

    private Control RecruitmentSummaryCard(PublishedExperimentStatus published, IReadOnlyList<RemoteParticipantSessionContract> sessions)
    {
        var main = sessions.Where(item => item.RunType == ExperimentRunType.Main).ToArray();
        var completed = main.Count(item => item.CompletionState == RemoteParticipantCompletionState.CompletedEligible);
        var incomplete = main.Count(item => item.CompletionState != RemoteParticipantCompletionState.CompletedEligible && item.CompletionState != RemoteParticipantCompletionState.Excluded);
        var excluded = main.Count(item => item.CompletionState == RemoteParticipantCompletionState.Excluded);
        var pilot = sessions.Count(item => item.RunType == ExperimentRunType.Pilot);
        var panel = new StackPanel { Spacing = 7 };
        panel.Children.Add(Heading(T("ملخص التجنيد", "Recruitment summary")));
        panel.Children.Add(Line(T("المكتمل", "Completed"), _study.TargetSampleSize is > 0 ? $"{completed} / {_study.TargetSampleSize}" : completed.ToString(), true));
        panel.Children.Add(Line(T("غير المكتمل", "Incomplete"), incomplete.ToString(), incomplete == 0));
        panel.Children.Add(Line(T("المستبعد", "Excluded"), excluded.ToString(), true));
        panel.Children.Add(Line(T("الاستطلاعي", "Pilot"), pilot.ToString(), true));
        var status = published.PilotState == PilotLifecycleState.Running ? T("التجربة الاستطلاعية قيد التشغيل", "Pilot Running") : published.IsMainRecruitmentStarted ? (published.IsRecruitmentPaused ? T("التجنيد متوقف مؤقتا", "Main Paused") : T("يستقبل مشاركين", "Accepting Participants")) : T("التجنيد الرئيسي لم يبدأ", "Main Recruitment Not Started");
        panel.Children.Add(Line(T("الحالة", "Status"), status, !published.IsRecruitmentPaused));
        return Card(panel);
    }

    private Control ResearchDataCard()
    {
        var panel=new StackPanel { Spacing=8 };
        panel.Children.Add(Heading(T("قابلية إعادة إنتاج البيانات", "Research reproducibility")));
        var actions=new StackPanel { Orientation=Orientation.Horizontal, Spacing=9, HorizontalAlignment=LocalizationService.IsArabic?HorizontalAlignment.Right:HorizontalAlignment.Left };
        var dictionary=new Button { Content=T("تصدير قاموس البيانات", "Export Data Dictionary") };
        dictionary.Click += async (_,_) => { var entries=await ResearchDataDictionaryService.ForStudyAsync(_study,LocalizationService.IsArabic); var path=System.IO.Path.Combine(StorageService.GetResearcherExportsFolder(_study.ResearcherId),$"data_dictionary_{_study.Id}.csv"); System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); await System.IO.File.WriteAllTextAsync(path,ResearchDataDictionaryService.Csv(entries),new System.Text.UTF8Encoding(false)); _status.Text=T("تم تصدير قاموس البيانات.","Data Dictionary exported."); };
        var package=Primary(T("تصدير حزمة البحث", "Export Research Package")); package.Click += async (_,_) => { await ResearchPackageExportService.ExportAsync(_study,LocalizationService.IsArabic); _status.Text=T("تم حفظ نسخة محلية من حزمة البحث.","A local research package was saved."); };
        actions.Children.Add(dictionary); actions.Children.Add(package); panel.Children.Add(actions); return Card(panel);
    }

    private static Border Card(Control content, string visualRole = "researchCard") => new() { Classes = { visualRole }, Padding = new Thickness(20), Child = content };
    private static TextBlock Heading(string text) => new() { Text = text, Classes = { "sectionTitle" }, HorizontalAlignment = HorizontalAlignment.Stretch, TextAlignment = Alignment };
    private static TextBlock Line(string label, string value, bool ready) => new() { Text = $"{label}: {value}", Foreground = Brush(ready ? "#24765F" : "#9A650E"), TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch, TextAlignment = Alignment };
    private static Button Primary(string text) => new() { Content = text, Classes = { "primary" }, MinWidth = 190, Height = 42, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));
    private static string LocalizeMedia(CloudMediaReadiness readiness) => LocalizationService.IsArabic ? readiness.State switch { CloudMediaReadinessState.TextOnlyReady => "غير مطلوب للتجربة النصية", CloudMediaReadinessState.Ready => "جاهز", CloudMediaReadinessState.RemoteMediaUrlMissing => "يلزم رابط ويب", _ => "يلزم التحقق" } : readiness.State switch { CloudMediaReadinessState.TextOnlyReady => "Not required for text-only", CloudMediaReadinessState.Ready => "Ready", CloudMediaReadinessState.RemoteMediaUrlMissing => "Web URL required", _ => "Needs verification" };
    private static string BlockingReason(PublicationReadinessResult readiness)
    {
        var reason = readiness.BlockingReasons.FirstOrDefault();
        if (reason is null) return T("أكمل عناصر الجاهزية المشار إليها قبل النشر.", "Complete the readiness items above before publishing.");
        return reason.Code switch
        {
            "cloud.account" => T("اربط حساب Cloudflare مصرحا به قبل النشر.", reason.Message),
            "cloud.database" => T("تحقق من جاهزية قاعدة بيانات البحث قبل النشر.", reason.Message),
            "cloud.runtime" => T("تحقق من جاهزية بيئة تشغيل تجربة SOCYVIA قبل النشر.", reason.Message),
            "media.remote-url" => T("يجب توفير رابط ويب للوسائط قبل النشر.", "A web media URL is required before publishing."),
            _ => T("أكمل متطلب الدراسة المحدد في فحص الجاهزية قبل النشر.", reason.Message)
        };
    }
    private static string T(string ar, string en) => LocalizationService.IsArabic ? ar : en;
    private static TextAlignment Alignment => LocalizationService.IsArabic ? TextAlignment.Right : TextAlignment.Left;
    private static void Open(string uri)
    {
        try { SocyviaProductUrls.OpenInDefaultBrowser(new Uri(uri)); }
        catch (Exception exception) { ApplicationDiagnosticsService.LogException(exception, "Open published experiment"); }
    }

    private void ShowQrCode(string canonicalUrl)
    {
        try
        {
            var image = new Image { Width = 280, Height = 280, Stretch = Stretch.Uniform };
            using var stream = new MemoryStream(ParticipantLinkQrCodeService.CreatePng(canonicalUrl));
            image.Source = new Bitmap(stream);
            var window = new Window
            {
                Title = T("رمز QR للمشاركين", "Participant QR Code"),
                Width = 380,
                Height = 410,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(24), Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = T("رابط المشاركين المباشر", "Live Participant Link"), FontWeight = FontWeight.SemiBold, TextAlignment = Alignment },
                        image,
                        new TextBlock { Text = canonicalUrl, TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.LeftToRight, TextAlignment = TextAlignment.Left }
                    }
                }
            };
            if (TopLevel.GetTopLevel(this) is Window owner)
                _ = window.ShowDialog(owner);
            else
                window.Show();
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Generate participant QR code");
            _status.Text = T("تعذر إنشاء رمز QR للرابط المنشور.", "The QR code could not be created for the published link.");
        }
    }

    private async Task ChangePilotStateAsync(PublishedExperimentStatus published, PilotLifecycleState state)
    {
        try
        {
            var configuration = await new CloudflareProviderConfigurationStore().LoadAsync();
            var token = configuration is null ? null : await new CloudflareOAuthConnectionService().GetAccessTokenAsync(
                configuration, CloudflareOAuthClientConfiguration.LoadReleaseConfiguration());
            if (configuration is null || string.IsNullOrWhiteSpace(token))
            {
                _status.Text = T("يلزم ربط Cloudflare لإدارة التجربة الاستطلاعية.", "Connect Cloudflare to manage the pilot.");
                return;
            }
            var provider = new CloudflareRemoteProvider();
            if (state == PilotLifecycleState.Running)
                await provider.StartPilotAsync(configuration, token, published.DeploymentId);
            else
                await provider.EndPilotAsync(configuration, token, published.DeploymentId);
            await PublishedExperimentStatusStore.SetPilotStateAsync(_study.Id, state);
            _status.Text = state == PilotLifecycleState.Running ? T("التجربة الاستطلاعية قيد التشغيل.", "Pilot Running.") : T("اكتملت التجربة الاستطلاعية. لم يبدأ التجنيد الرئيسي تلقائيا.", "Pilot Completed. Main recruitment has not started automatically.");
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Change pilot recruitment state");
            _status.Text = T("تعذر تحديث حالة التجربة الاستطلاعية.", "The pilot state could not be updated.");
        }
    }

    private async Task StartMainRecruitmentAsync(PublishedExperimentStatus published)
    {
        try
        {
            var configuration = await new CloudflareProviderConfigurationStore().LoadAsync();
            var token = configuration is null ? null : await new CloudflareOAuthConnectionService().GetAccessTokenAsync(
                configuration, CloudflareOAuthClientConfiguration.LoadReleaseConfiguration());
            if (configuration is null || string.IsNullOrWhiteSpace(token))
            {
                _status.Text = T("يلزم ربط Cloudflare لبدء التجنيد الرئيسي.", "Connect Cloudflare to start main recruitment.");
                return;
            }
            await new CloudflareRemoteProvider().StartMainRecruitmentAsync(configuration, token, published.DeploymentId);
            await PublishedExperimentStatusStore.SetMainRecruitmentStartedAsync(_study.Id, true);
            _status.Text = T("بدأ التجنيد الرئيسي. تحتفظ بيانات التجربة الاستطلاعية بأصلها المنفصل.", "Main recruitment started. Pilot data retains separate provenance.");
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Start main recruitment");
            _status.Text = T("تعذر بدء التجنيد الرئيسي.", "Main recruitment could not be started.");
        }
    }

    private async Task SetRecruitmentPausedAsync(PublishedExperimentStatus published, bool paused)
    {
        try
        {
            if (published.PilotState == PilotLifecycleState.Running || !published.IsMainRecruitmentStarted)
            {
                _status.Text = T("يتم التحكم في قبول المشاركين أثناء التجربة الاستطلاعية من خلال إجراءات التجربة الاستطلاعية.", "Pilot admission is controlled by the Pilot actions, not Main recruitment controls.");
                return;
            }
            var configuration = await new CloudflareProviderConfigurationStore().LoadAsync();
            var token = configuration is null ? null : await new CloudflareOAuthConnectionService().GetAccessTokenAsync(
                configuration, CloudflareOAuthClientConfiguration.LoadReleaseConfiguration());
            if (configuration is null || string.IsNullOrWhiteSpace(token))
            {
                _status.Text = T("يلزم ربط Cloudflare لإدارة حالة التجنيد.", "Connect Cloudflare to manage recruitment.");
                return;
            }
            await new CloudflareRemoteProvider().SetRecruitmentPausedAsync(configuration, token, published.DeploymentId, paused);
            await PublishedExperimentStatusStore.SetRecruitmentPausedAsync(_study.Id, paused);
            _status.Text = paused ? T("تم إيقاف التجنيد مؤقتا. لا يتم حذف أي بيانات.", "Recruitment is paused. No research data was deleted.") : T("تم استئناف التجنيد.", "Recruitment is accepting participants again.");
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Change recruitment state");
            _status.Text = T("تعذر تحديث حالة التجنيد. لم يتم تغيير بيانات الدراسة المحلية.", "Recruitment status could not be updated. Local study data was not changed.");
        }
    }
}
