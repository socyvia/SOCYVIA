using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

/// <summary>
/// Local researcher-facing mirror of confirmed publication outcomes. This is
/// not research data and never changes the immutable deployment in D1.
/// </summary>
public static class PublishedExperimentStatusStore
{
    private static string FilePath => Path.Combine(StorageService.SettingsFolder, "published-experiments.json");

    public static async Task SaveAsync(ExperimentDeployment deployment, Uri runtimeUri)
    {
        var link = PublicExperimentLinkService.ForPublishedDeployment(deployment)
                   ?? throw new InvalidOperationException("Only published deployments can be shown as participant links.");
        var values = (await LoadAllAsync()).Where(item => item.StudyId != deployment.StudyId).ToList();
        values.Add(new PublishedExperimentStatus(
            deployment.StudyId,
            deployment.DeploymentId,
            link.CanonicalUri.AbsoluteUri,
            runtimeUri.AbsoluteUri,
            deployment.DeploymentVersion,
            deployment.PublishedAtUtc,
            link.RoutingStatus,
            IsRecruitmentPaused: false,
            ConfigurationHash: deployment.ConfigurationHash));
        Directory.CreateDirectory(StorageService.SettingsFolder);
        await File.WriteAllTextAsync(FilePath, JsonSerializer.Serialize(values));
    }

    public static async Task<PublishedExperimentStatus?> GetAsync(string studyId) =>
        (await LoadAllAsync()).OrderByDescending(item => item.PublishedAtUtc).FirstOrDefault(item => item.StudyId == studyId);

    public static async Task<string?> GetStudyIdByDeploymentAsync(string deploymentId) =>
        (await LoadAllAsync()).OrderByDescending(item => item.PublishedAtUtc)
            .FirstOrDefault(item => string.Equals(item.DeploymentId, deploymentId, StringComparison.Ordinal))?.StudyId;

    public static async Task SetRecruitmentPausedAsync(string studyId, bool paused)
    {
        var values = (await LoadAllAsync()).Select(item => item.StudyId == studyId
            ? item with { IsRecruitmentPaused = paused }
            : item).ToList();
        Directory.CreateDirectory(StorageService.SettingsFolder);
        await File.WriteAllTextAsync(FilePath, JsonSerializer.Serialize(values));
    }

    public static async Task SetPilotStateAsync(string studyId, PilotLifecycleState state)
    {
        var values = (await LoadAllAsync()).Select(item => item.StudyId == studyId
            ? item with
            {
                PilotState = state,
                IsMainRecruitmentStarted = state == PilotLifecycleState.Running ? false : item.IsMainRecruitmentStarted,
                PilotCompletedAtUtc = state == PilotLifecycleState.Completed ? DateTime.UtcNow : item.PilotCompletedAtUtc,
                PilotDeploymentVersion = state is PilotLifecycleState.Running or PilotLifecycleState.Completed ? item.DeploymentVersion : item.PilotDeploymentVersion,
                PilotConfigurationHash = state is PilotLifecycleState.Running or PilotLifecycleState.Completed ? item.ConfigurationHash : item.PilotConfigurationHash
            }
            : item).ToList();
        Directory.CreateDirectory(StorageService.SettingsFolder);
        await File.WriteAllTextAsync(FilePath, JsonSerializer.Serialize(values));
    }

    public static async Task SetMainRecruitmentStartedAsync(string studyId, bool started)
    {
        var values = (await LoadAllAsync()).Select(item => item.StudyId == studyId
            ? item with { IsMainRecruitmentStarted = started, IsRecruitmentPaused = false }
            : item).ToList();
        Directory.CreateDirectory(StorageService.SettingsFolder);
        await File.WriteAllTextAsync(FilePath, JsonSerializer.Serialize(values));
    }

    public static async Task RemoveAsync(string studyId)
    {
        var values = (await LoadAllAsync()).Where(item => item.StudyId != studyId).ToList();
        Directory.CreateDirectory(StorageService.SettingsFolder);
        await File.WriteAllTextAsync(FilePath, JsonSerializer.Serialize(values));
    }

    private static async Task<IReadOnlyList<PublishedExperimentStatus>> LoadAllAsync()
    {
        if (!File.Exists(FilePath)) return Array.Empty<PublishedExperimentStatus>();
        try
        {
            var values = JsonSerializer.Deserialize<List<PublishedExperimentStatus>>(await File.ReadAllTextAsync(FilePath)) ?? [];
            return values.Select(item => string.Equals(item.RoutingStatus, PublicExperimentLinkService.LegacyPreparedRoutingStatus, StringComparison.Ordinal)
                ? item with { RoutingStatus = PublicExperimentLinkService.LiveRoutingStatus }
                : item).ToArray();
        }
        catch (JsonException) { return Array.Empty<PublishedExperimentStatus>(); }
    }
}

public sealed record PublishedExperimentStatus(
    string StudyId,
    string DeploymentId,
    string CanonicalParticipantUrl,
    string RuntimeParticipantUrl,
    int DeploymentVersion,
    DateTime? PublishedAtUtc,
    string RoutingStatus,
    bool IsRecruitmentPaused = false,
    string? ConfigurationHash = null,
    PilotLifecycleState PilotState = PilotLifecycleState.NotStarted,
    int? PilotDeploymentVersion = null,
    string? PilotConfigurationHash = null,
    DateTime? PilotCompletedAtUtc = null,
    bool IsMainRecruitmentStarted = true);
