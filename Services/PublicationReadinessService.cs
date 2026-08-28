using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SOCYVIA.Services;

public sealed record PublicationBlockingReason(string Code, string Message);

/// <summary>
/// One authoritative view of local study, Cloudflare, runtime, and conditional
/// media readiness. The UI and publish action consume the same result.
/// </summary>
public sealed record PublicationReadinessResult(
    bool AccountReady,
    bool DatabaseReady,
    bool RuntimeReady,
    bool StudyReady,
    bool MediaRequired,
    bool MediaReady,
    CloudMediaReadiness Media,
    ResearcherPublishReadiness Study,
    IReadOnlyList<PublicationBlockingReason> BlockingReasons)
{
    public bool CanPublish => AccountReady && DatabaseReady && RuntimeReady && StudyReady && MediaReady;
}

public static class PublicationReadinessService
{
    public static async Task<PublicationReadinessResult> EvaluateAsync(
        Models.Study study,
        PreparedRemotePublication prepared,
        CloudflareProviderConfiguration? cloud)
    {
        var studyReadiness = await ResearcherPublishValidationService.EvaluateStudyAsync(
            study, prepared.Entry, prepared.Content, prepared.Questionnaires);
        var media = await CloudMediaReadinessService.EvaluateAsync(prepared.Package.MediaManifest, cloud);
        return Combine(studyReadiness, cloud, media);
    }

    public static PublicationReadinessResult Combine(
        ResearcherPublishReadiness study,
        CloudflareProviderConfiguration? cloud,
        CloudMediaReadiness media)
    {
        var connected = cloud?.ConnectionMode is CloudflareConnectionMode.OAuth or CloudflareConnectionMode.Manual;
        var accountReady = connected && !string.IsNullOrWhiteSpace(cloud?.AccountId);
        var providerReady = cloud?.ProviderStatus == CloudflareProviderConnectionState.Ready;
        var databaseReady = accountReady && providerReady && !string.IsNullOrWhiteSpace(cloud?.D1DatabaseId);
        var runtimeReady = accountReady && providerReady &&
                           Uri.TryCreate(cloud?.WorkerEndpoint, UriKind.Absolute, out var runtime) &&
                           runtime.Scheme == Uri.UriSchemeHttps;
        var mediaRequired = media.State != CloudMediaReadinessState.TextOnlyReady;
        var mediaReady = media.CanPublish;
        var reasons = new List<PublicationBlockingReason>();

        if (!accountReady)
            reasons.Add(new("cloud.account", "Connect an authorized Cloudflare account."));
        if (!databaseReady)
            reasons.Add(new("cloud.database", "Verify the SOCYVIA research database."));
        if (!runtimeReady)
            reasons.Add(new("cloud.runtime", "Verify the SOCYVIA experiment runtime."));
        reasons.AddRange(study.Checks.Where(item => !item.IsReady)
            .Select(item => new PublicationBlockingReason("study." + item.Area, item.Message)));
        if (!mediaReady)
            reasons.Add(new("media.remote-url", media.Message));

        return new PublicationReadinessResult(
            accountReady, databaseReady, runtimeReady, study.IsReady,
            mediaRequired, mediaReady, media, study, reasons);
    }
}
