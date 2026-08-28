using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

public enum SocyviaAiServiceState { Ready, Unavailable, Connecting, RateLimited, ServiceError }
public enum SocyviaAiServiceAvailabilityReason { None, GatewayNotConfigured, AuthorizationRequired, TemporarilyUnavailable, RateLimited, ServiceNotReady, InvalidContract }

public sealed record SocyviaAiServiceStatus(
    SocyviaAiServiceState State,
    string Message,
    SocyviaAiServiceAvailabilityReason Reason = SocyviaAiServiceAvailabilityReason.None);

public static class SocyviaAiGatewayContract
{
    public const string Version = "SOCYVIA.AI/1";
    public const string ServiceIdentity = "SOCYVIA AI";
    public const string StatusPath = "api/ai/status";
    public const string InterpretationsPath = "api/ai/research-assistant";
}

public static class SocyviaAiStatusPresentationService
{
    public static string StateLabel(SocyviaAiServiceStatus status, bool arabic) => status.State switch
    {
        SocyviaAiServiceState.Ready => arabic ? "جاهز" : "Ready",
        SocyviaAiServiceState.Connecting => arabic ? "جار الاتصال" : "Connecting",
        SocyviaAiServiceState.RateLimited => arabic ? "تم بلوغ حد السعة مؤقتا" : "Temporarily at capacity",
        SocyviaAiServiceState.ServiceError => arabic ? "خطأ في الخدمة" : "Service error",
        _ => arabic ? "غير متاح" : "Unavailable"
    };

    public static string Detail(SocyviaAiServiceStatus status, bool arabic) => status.Reason switch
    {
        SocyviaAiServiceAvailabilityReason.GatewayNotConfigured => arabic
            ? "خدمة SOCYVIA AI غير مفعلة على خادم SOCYVIA حاليا. لا تتأثر التحليلات الحتمية أو الجداول أو الأشكال أو التقارير."
            : "SOCYVIA AI is not activated on the SOCYVIA server. Deterministic analysis, tables, figures, and reports are unaffected.",
        SocyviaAiServiceAvailabilityReason.TemporarilyUnavailable => arabic
            ? "خدمة SOCYVIA AI غير متاحة مؤقتا. حاول مرة أخرى لاحقا."
            : "SOCYVIA AI is temporarily unavailable. Try again later.",
        SocyviaAiServiceAvailabilityReason.AuthorizationRequired => arabic
            ? "يلزم اتصال Cloudflare المصرح به لاستخدام خدمة SOCYVIA AI الآمنة."
            : "An authorized Cloudflare connection is required to use the secure SOCYVIA AI service.",
        SocyviaAiServiceAvailabilityReason.RateLimited => arabic
            ? "بلغت خدمة SOCYVIA AI سعتها المؤقتة. حاول مرة أخرى لاحقا."
            : "SOCYVIA AI has reached temporary capacity. Try again later.",
        SocyviaAiServiceAvailabilityReason.ServiceNotReady => arabic
            ? "خدمة SOCYVIA AI متاحة لكنها ليست جاهزة للمحادثة حاليا."
            : "SOCYVIA AI is available but is not ready for a conversation yet.",
        SocyviaAiServiceAvailabilityReason.InvalidContract => arabic
            ? "تعذر التحقق من استجابة خدمة SOCYVIA AI الآمنة."
            : "SOCYVIA could not validate the AI service response.",
        _ when status.State == SocyviaAiServiceState.Ready => arabic
            ? "خدمة SOCYVIA AI جاهزة للإرشاد داخل المنتج وللمحادثات العلمية المستندة إلى أدلة الدراسة والمقيدة بها."
            : "SOCYVIA AI is ready for product guidance and scientific conversations constrained by study evidence.",
        _ => status.Message
    };
}

/// <summary>
/// Managed SOCYVIA AI gateway configuration. There is deliberately no shipped
/// endpoint or researcher-editable provider setting until the real service is
/// available. Maintainers may inject the official HTTPS endpoint at runtime.
/// </summary>
public sealed record SocyviaAiGatewayConfiguration(Uri Endpoint)
{
    public static readonly Uri ProductionEndpoint = new("https://socyvia.com/experimentfeed/", UriKind.Absolute);

    public static SocyviaAiGatewayConfiguration? LoadManagedConfiguration()
    {
        var value = Environment.GetEnvironmentVariable("SOCYVIA_AI_GATEWAY_URL");
        if (string.IsNullOrWhiteSpace(value)) return new(ProductionEndpoint);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps) return null;
        if (!endpoint.Host.Equals("socyvia.com", StringComparison.OrdinalIgnoreCase) &&
            !endpoint.Host.EndsWith(".socyvia.com", StringComparison.OrdinalIgnoreCase))
            return null;
        var normalized = endpoint.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? endpoint
            : new Uri(endpoint.AbsoluteUri + '/', UriKind.Absolute);
        return new(normalized);
    }
}

/// <summary>Resolves only the SOCYVIA-managed AI service boundary.</summary>
public static class ResearchInterpretationProviderFactory
{
    public static async Task<IResearchInterpretationProvider?> CreateConfiguredAsync(CancellationToken cancellationToken = default)
    {
        var gateway = SocyviaAiGatewayConfiguration.LoadManagedConfiguration();
        var cloud = await new CloudflareProviderConfigurationStore().LoadAsync(cancellationToken);
        if (gateway is null || cloud is null || cloud.ConnectionMode != CloudflareConnectionMode.OAuth) return null;
        var token = await new CloudflareOAuthConnectionService().GetAccessTokenAsync(
            cloud, CloudflareOAuthClientConfiguration.LoadReleaseConfiguration(), cancellationToken);
        return string.IsNullOrWhiteSpace(token) ? null : new SocyviaAiGatewayClient(gateway, bearerToken: token);
    }
}

public static class SocyviaAiService
{
    public static async Task<SocyviaAiServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var configuration = SocyviaAiGatewayConfiguration.LoadManagedConfiguration();
        if (configuration is null)
            return new(SocyviaAiServiceState.Unavailable,
                "The SOCYVIA AI service is not configured on the SOCYVIA server.",
                SocyviaAiServiceAvailabilityReason.GatewayNotConfigured);
        var status = await new SocyviaAiGatewayClient(configuration).GetStatusAsync(cancellationToken);
        if (status.State != SocyviaAiServiceState.Ready) return status;
        return await ResearchInterpretationProviderFactory.CreateConfiguredAsync(cancellationToken) is null
            ? new(SocyviaAiServiceState.Unavailable, "Authorized Cloudflare connection required.", SocyviaAiServiceAvailabilityReason.AuthorizationRequired)
            : status;
    }
}

/// <summary>
/// Provider-neutral client for the future SOCYVIA-controlled service. No model,
/// provider credential, or provider endpoint crosses the Desktop boundary.
/// </summary>
public sealed class SocyviaAiGatewayClient : IResearchInterpretationProvider
{
    private readonly HttpClient _http;
    public string ProviderName => "SOCYVIA AI";

    public SocyviaAiGatewayClient(SocyviaAiGatewayConfiguration configuration, HttpClient? http = null, string? bearerToken = null)
    {
        _http = http ?? new HttpClient { BaseAddress = configuration.Endpoint };
        _http.BaseAddress ??= configuration.Endpoint;
        if (!string.IsNullOrWhiteSpace(bearerToken))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
    }

    public async Task<SocyviaAiServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await _http.GetAsync(SocyviaAiGatewayContract.StatusPath, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return new(SocyviaAiServiceState.ServiceError, "The SOCYVIA AI service could not be reached successfully.", SocyviaAiServiceAvailabilityReason.TemporarilyUnavailable);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var contractValid = document.RootElement.TryGetProperty("service", out var service) &&
                                service.GetString()?.Equals(SocyviaAiGatewayContract.ServiceIdentity, StringComparison.Ordinal) == true &&
                                document.RootElement.TryGetProperty("contractVersion", out var contractVersion) &&
                                contractVersion.GetString()?.Equals(SocyviaAiGatewayContract.Version, StringComparison.Ordinal) == true;
            if (!contractValid)
                return new(SocyviaAiServiceState.ServiceError,
                    "The SOCYVIA AI service returned an invalid contract response.",
                    SocyviaAiServiceAvailabilityReason.InvalidContract);
            var statusValue = document.RootElement.TryGetProperty("status", out var status) ? status.GetString() : null;
            var ready = statusValue?.Equals("ready", StringComparison.OrdinalIgnoreCase) == true;
            if (!ready && statusValue?.Equals("unavailable", StringComparison.OrdinalIgnoreCase) == true)
            {
                var reason = document.RootElement.TryGetProperty("reason", out var reasonElement) ? reasonElement.GetString() : null;
                return reason?.Equals("INFERENCE_NOT_PROVISIONED", StringComparison.OrdinalIgnoreCase) == true
                    ? new(SocyviaAiServiceState.Unavailable, "SOCYVIA AI is not activated on the SOCYVIA server.", SocyviaAiServiceAvailabilityReason.GatewayNotConfigured)
                    : new(SocyviaAiServiceState.Unavailable, "SOCYVIA AI is temporarily unavailable.", SocyviaAiServiceAvailabilityReason.TemporarilyUnavailable);
            }
            return ready
                ? new(SocyviaAiServiceState.Ready, "SOCYVIA AI is ready.")
                : new(SocyviaAiServiceState.ServiceError, "The SOCYVIA AI service is responding but is not ready.", SocyviaAiServiceAvailabilityReason.ServiceNotReady);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(SocyviaAiServiceState.ServiceError, "The SOCYVIA AI service did not respond in time.", SocyviaAiServiceAvailabilityReason.TemporarilyUnavailable);
        }
        catch (HttpRequestException)
        {
            return new(SocyviaAiServiceState.ServiceError, "The SOCYVIA AI service could not be reached.", SocyviaAiServiceAvailabilityReason.TemporarilyUnavailable);
        }
        catch (JsonException)
        {
            return new(SocyviaAiServiceState.ServiceError, "The SOCYVIA AI service returned an invalid status response.", SocyviaAiServiceAvailabilityReason.InvalidContract);
        }
    }

    public async Task<ResearchInterpretationResponse> InterpretAsync(
        ResearchInterpretationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!AiConversationService.IsAggregateSafe(request))
            throw new InvalidOperationException("SOCYVIA AI blocked a non-aggregate context.");
        if (SocyviaAiScientificGuardrails.Evaluate(request) is { } blocked) return blocked;
        var inputHash = SocyviaAiScientificGuardrails.InputHash(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var response = await _http.PostAsJsonAsync(SocyviaAiGatewayContract.InterpretationsPath,
            new SocyviaAiGatewayRequest(SocyviaAiGatewayContract.Version, inputHash, request), timeout.Token);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new SocyviaAiRateLimitException();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("The SOCYVIA AI service could not generate an interpretation.");
        var payload = await response.Content.ReadFromJsonAsync<SocyviaAiGatewayResponse>(cancellationToken: timeout.Token);
        if (payload is null || payload.ContractVersion != SocyviaAiGatewayContract.Version ||
            string.IsNullOrWhiteSpace(payload.Interpretation) ||
            !string.Equals(payload.InputHash, inputHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The SOCYVIA AI service returned an invalid evidence response.");
        return new ResearchInterpretationResponse(
            payload.Status?.Equals("limited_evidence", StringComparison.OrdinalIgnoreCase) == true
                ? ResearchInterpretationResponse.EvidenceUnavailable
                : ResearchInterpretationResponse.Generated,
            ProviderName,
            payload.Model,
            payload.Interpretation,
            inputHash,
            payload.GeneratedAtUtc ?? DateTime.UtcNow,
            payload.SafetyNotes ?? ["AI-assisted interpretation requires researcher review.", "Deterministic statistics remain the source of truth."]);
    }

    private sealed record SocyviaAiGatewayRequest(string ContractVersion, string InputHash, ResearchInterpretationRequest Request);

    private sealed record SocyviaAiGatewayResponse(
        string? ContractVersion,
        string? Status,
        string? Model,
        string? Interpretation,
        string? InputHash,
        DateTime? GeneratedAtUtc,
        IReadOnlyList<string>? SafetyNotes);
}

public sealed class SocyviaAiRateLimitException : Exception
{
    public SocyviaAiRateLimitException() : base("SOCYVIA AI has reached temporary capacity.") { }
}
