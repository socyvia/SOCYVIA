using System;
using System.Linq;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class SocyviaAiApplicationContextService
{
    public static async Task<SocyviaAiApplicationState> ForStudyAsync(
        Study study, string currentSection, bool analysisAvailable)
    {
        var cloud = await new CloudflareProviderConfigurationStore().LoadAsync();
        var connected = cloud?.ConnectionMode is CloudflareConnectionMode.OAuth or CloudflareConnectionMode.Manual;
        var databaseReady = cloud?.ProviderStatus == CloudflareProviderConnectionState.Ready &&
                            !string.IsNullOrWhiteSpace(cloud.D1DatabaseId);
        var runtimeReady = cloud?.ProviderStatus == CloudflareProviderConnectionState.Ready &&
                           Uri.TryCreate(cloud.WorkerEndpoint, UriKind.Absolute, out var endpoint) &&
                           endpoint.Scheme == Uri.UriSchemeHttps;
        var sessions = await RemoteResearchRepository.GetSessionsAsync(studyId: study.Id);
        try
        {
            var prepared = await RemotePublicationPreparationService.PrepareAsync(study);
            var readiness = await PublicationReadinessService.EvaluateAsync(study, prepared, cloud);
            return new(currentSection, true, StudyContextLabelService.ForDisplay(study.Title, LocalizationService.IsArabic),
                study.Status, connected, databaseReady, runtimeReady, readiness.CanPublish,
                readiness.BlockingReasons.FirstOrDefault()?.Message, sessions.Select(item => item.ParticipantId).Distinct().Count(),
                sessions.Count, analysisAvailable);
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Build aggregate-safe SOCYVIA AI application context");
            return new(currentSection, true, StudyContextLabelService.ForDisplay(study.Title, LocalizationService.IsArabic),
                study.Status, connected, databaseReady, runtimeReady, false,
                "Study readiness could not be evaluated.", sessions.Select(item => item.ParticipantId).Distinct().Count(),
                sessions.Count, analysisAvailable);
        }
    }

    public static async Task<SocyviaAiApplicationState> WithoutStudyAsync(string currentSection)
    {
        var cloud = await new CloudflareProviderConfigurationStore().LoadAsync();
        var connected = cloud?.ConnectionMode is CloudflareConnectionMode.OAuth or CloudflareConnectionMode.Manual;
        var ready = cloud?.ProviderStatus == CloudflareProviderConnectionState.Ready;
        return new(currentSection, false, null, null, connected,
            ready && !string.IsNullOrWhiteSpace(cloud?.D1DatabaseId),
            ready && Uri.TryCreate(cloud?.WorkerEndpoint, UriKind.Absolute, out var endpoint) && endpoint.Scheme == Uri.UriSchemeHttps,
            false, null, 0, 0, false);
    }
}
