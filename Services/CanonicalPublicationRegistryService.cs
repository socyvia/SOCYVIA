using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

/// <summary>
/// Registers only safe public-route metadata with the SOCYVIA domain resolver.
/// The researcher-owned runtime and D1 remain authoritative for participant data.
/// OAuth credentials are used in memory for verification and are never persisted here.
/// </summary>
public sealed class CanonicalPublicationRegistryService
{
    public static readonly Uri RegistrationEndpoint = new("https://socyvia.com/experimentfeed/api/publications/register");
    private readonly HttpClient _http;

    public CanonicalPublicationRegistryService(HttpClient? http = null) =>
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<Uri> EnsureRegisteredAsync(
        CloudflareProviderConfiguration configuration,
        string accessToken,
        ExperimentDeployment deployment,
        CancellationToken cancellationToken = default)
    {
        var link = PublicExperimentLinkService.ForPublishedDeployment(deployment)
                   ?? throw new InvalidOperationException("A confirmed publication is required for canonical registration.");

        // Deployments hosted directly by the canonical runtime are already resolvable.
        try
        {
            using var existing = await _http.GetAsync(link.CanonicalUri, cancellationToken);
            if (existing.IsSuccessStatusCode && existing.Headers.TryGetValues("x-socyvia-participant-route", out var values) &&
                values.Contains("canonical", StringComparer.Ordinal) &&
                existing.Headers.TryGetValues("x-socyvia-configuration-hash", out var hashes) &&
                hashes.Contains(deployment.ConfigurationHash, StringComparer.OrdinalIgnoreCase))
                return link.CanonicalUri;
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }

        using var request = new HttpRequestMessage(HttpMethod.Post, RegistrationEndpoint)
        {
            Content = JsonContent.Create(new
            {
                accountId = configuration.AccountId,
                publicId = CloudflareRemoteProvider.DeploymentPublicId(deployment),
                runtimeEndpoint = configuration.WorkerEndpoint,
                configurationHash = deployment.ConfigurationHash
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("SOCYVIA could not activate the canonical participant route.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        if (!root.TryGetProperty("registered", out var registered) || !registered.GetBoolean() ||
            !root.TryGetProperty("canonicalUrl", out var canonicalValue) ||
            !Uri.TryCreate(canonicalValue.GetString(), UriKind.Absolute, out var canonical) ||
            canonical != link.CanonicalUri)
            throw new InvalidOperationException("SOCYVIA returned an invalid canonical publication result.");
        return canonical;
    }
}
