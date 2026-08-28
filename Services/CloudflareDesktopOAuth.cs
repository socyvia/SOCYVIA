using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SOCYVIA.Services;

/// <summary>Release-owned, non-secret registration data for Cloudflare's public desktop OAuth client.</summary>
public sealed record CloudflareOAuthClientConfiguration(
    string ClientId,
    Uri RedirectUri,
    IReadOnlyList<string> Scopes)
{
    public const string OfficialClientId = "f94ac305e7e32b9606732e5115660c69";
    public static readonly Uri OfficialRedirectUri = new("https://socyvia.com/oauth/cloudflare/callback");

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        RedirectUri.IsAbsoluteUri &&
        RedirectUri.Scheme == Uri.UriSchemeHttps;

    public static CloudflareOAuthClientConfiguration LoadReleaseConfiguration() => new(
        OfficialClientId,
        OfficialRedirectUri,
        CloudflareOAuthScopes.MinimumResearchPublishing);
}

/// <summary>Public scope IDs verified against Cloudflare's live GET /oauth/scopes catalogue on 2026-08-23.</summary>
public static class CloudflareOAuthScopes
{
    public static readonly IReadOnlyList<string> MinimumResearchPublishing =
    [
        "d1.read",
        "d1.write",
        "workers-scripts.read",
        "workers-scripts.write"
    ];
}

public sealed record CloudflareOAuthAuthorizationRequest(
    Uri AuthorizationUri,
    string State,
    string CodeVerifier,
    DateTime CreatedAtUtc);

public sealed record CloudflareOAuthCallbackResult(
    bool IsValid,
    string? AuthorizationCode,
    string? Error,
    string? ErrorDescription)
{
    public static CloudflareOAuthCallbackResult Invalid(string error, string? description = null) =>
        new(false, null, error, description);
}

public sealed record CloudflareOAuthTokenSet(
    string AccessToken,
    string TokenType,
    DateTime ExpiresAtUtc,
    string? RefreshToken,
    string? Scope,
    string? IdToken)
{
    public bool IsExpired(DateTime utcNow, TimeSpan? safetyWindow = null) =>
        ExpiresAtUtc <= utcNow.Add(safetyWindow ?? TimeSpan.FromMinutes(2));
}

public sealed record CloudflareOAuthAccount(string Id, string Name);

public static class CloudflareDesktopOAuth
{
    public static readonly Uri AuthorizationEndpoint = new("https://dash.cloudflare.com/oauth2/auth");
    public static readonly Uri TokenEndpoint = new("https://dash.cloudflare.com/oauth2/token");
    public static readonly Uri RevocationEndpoint = new("https://dash.cloudflare.com/oauth2/revoke");
    public static readonly Uri UserInfoEndpoint = new("https://dash.cloudflare.com/oauth2/userinfo");
    public static readonly Uri ApplicationHandoffOrigin = new("socyvia://oauth/");

    public static bool TryCreateAuthorizationRequest(
        CloudflareOAuthClientConfiguration configuration,
        out CloudflareOAuthAuthorizationRequest? request,
        DateTime? utcNow = null)
    {
        request = null;
        if (!configuration.IsConfigured || configuration.Scopes.Count == 0) return false;

        var state = CreateSecureValue();
        var verifier = CreateSecureValue();
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var values = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = configuration.ClientId,
            ["redirect_uri"] = configuration.RedirectUri.AbsoluteUri,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["scope"] = string.Join(' ', configuration.Scopes)
        };
        var query = string.Join("&", values.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        request = new CloudflareOAuthAuthorizationRequest(
            new Uri($"{AuthorizationEndpoint}?{query}"), state, verifier, utcNow ?? DateTime.UtcNow);
        return true;
    }

    /// <summary>Validates the app handoff URI produced by the registered HTTPS callback.</summary>
    public static CloudflareOAuthCallbackResult ValidateCallback(
        Uri callbackUri,
        CloudflareOAuthAuthorizationRequest pending,
        DateTime? utcNow = null,
        TimeSpan? maximumAge = null)
    {
        if (!IsExpectedApplicationHandoff(callbackUri))
            return CloudflareOAuthCallbackResult.Invalid("invalid_callback", "The callback did not originate from the registered SOCYVIA application handoff.");
        if ((utcNow ?? DateTime.UtcNow) - pending.CreatedAtUtc > (maximumAge ?? TimeSpan.FromMinutes(10)))
            return CloudflareOAuthCallbackResult.Invalid("expired_request", "The authorization request expired. Reconnect to start a fresh request.");

        if (!TryParseCallbackQuery(callbackUri.Query, out var values))
            return CloudflareOAuthCallbackResult.Invalid("malformed_callback", "Cloudflare returned a malformed authorization response.");
        if (!values.TryGetValue("state", out var state) || !FixedTimeEquals(state, pending.State))
            return CloudflareOAuthCallbackResult.Invalid("invalid_state", "The authorization response could not be matched to this device.");
        if (values.TryGetValue("error", out var error))
            return CloudflareOAuthCallbackResult.Invalid(error,
                values.GetValueOrDefault("error_description") ??
                (error.Equals("access_denied", StringComparison.Ordinal)
                    ? "Cloudflare authorization was cancelled. SOCYVIA remains disconnected."
                    : "Cloudflare did not authorize the connection."));
        if (!values.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            return CloudflareOAuthCallbackResult.Invalid("missing_code", "Cloudflare did not return an authorization code.");
        return new CloudflareOAuthCallbackResult(true, code, null, null);
    }

    public static bool IsExpectedApplicationHandoff(Uri uri) =>
        uri.IsAbsoluteUri &&
        uri.Scheme.Equals(ApplicationHandoffOrigin.Scheme, StringComparison.OrdinalIgnoreCase) &&
        uri.Host.Equals("oauth", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.Equals("/cloudflare/callback", StringComparison.Ordinal);

    private static bool TryParseCallbackQuery(string query, out Dictionary<string, string> result)
    {
        result = new Dictionary<string, string>(StringComparer.Ordinal);
        var allowed = new HashSet<string>(["code", "state", "error", "error_description", "error_uri"], StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            string key;
            string value;
            try
            {
                key = Uri.UnescapeDataString(separator < 0 ? part : part[..separator]);
                value = Uri.UnescapeDataString((separator < 0 ? string.Empty : part[(separator + 1)..]).Replace('+', ' '));
            }
            catch (UriFormatException)
            {
                return false;
            }
            if (!allowed.Contains(key) || !result.TryAdd(key, value)) return false;
        }
        if (!result.TryGetValue("state", out var state) || state.Length is < 16 or > 512) return false;
        var hasCode = result.TryGetValue("code", out var code) && !string.IsNullOrWhiteSpace(code);
        var hasError = result.TryGetValue("error", out var error) && !string.IsNullOrWhiteSpace(error);
        if (hasCode == hasError || code?.Length > 4096 || error?.Length > 128) return false;
        if (hasCode && (result.ContainsKey("error_description") || result.ContainsKey("error_uri"))) return false;
        if (result.GetValueOrDefault("error_description")?.Length > 512) return false;
        if (result.TryGetValue("error_uri", out var errorUri) &&
            (!Uri.TryCreate(errorUri, UriKind.Absolute, out var parsedErrorUri) || parsedErrorUri.Scheme != Uri.UriSchemeHttps))
            return false;
        return true;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string CreateSecureValue()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64Url(bytes);
    }

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>One-time PKCE state persisted only inside the OS credential boundary.</summary>
public sealed class CloudflareOAuthPendingStore
{
    public const string CredentialKey = "SOCYVIA.Cloudflare.OAuth.Pending";
    private readonly ISecureCredentialStore _credentials;

    public CloudflareOAuthPendingStore(ISecureCredentialStore? credentials = null) =>
        _credentials = credentials ?? SecureCredentialStoreFactory.Create();

    public Task SaveAsync(CloudflareOAuthAuthorizationRequest request, CancellationToken cancellationToken = default) =>
        _credentials.StoreAsync(CredentialKey, JsonSerializer.Serialize(request), cancellationToken);

    public async Task<CloudflareOAuthAuthorizationRequest?> TakeAsync(CancellationToken cancellationToken = default)
    {
        var serialized = await _credentials.RetrieveAsync(CredentialKey, cancellationToken);
        await _credentials.RemoveAsync(CredentialKey, cancellationToken);
        return string.IsNullOrWhiteSpace(serialized)
            ? null
            : JsonSerializer.Deserialize<CloudflareOAuthAuthorizationRequest>(serialized);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        _credentials.RemoveAsync(CredentialKey, cancellationToken);
}

public sealed class CloudflareOAuthProtocolClient
{
    private readonly HttpClient _http;

    public CloudflareOAuthProtocolClient(HttpClient? http = null) => _http = http ?? new HttpClient();

    public Task<CloudflareOAuthTokenSet> ExchangeCodeAsync(
        CloudflareOAuthClientConfiguration configuration,
        string authorizationCode,
        string codeVerifier,
        CancellationToken cancellationToken = default) =>
        RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = configuration.ClientId,
            ["code"] = authorizationCode,
            ["redirect_uri"] = configuration.RedirectUri.AbsoluteUri,
            ["code_verifier"] = codeVerifier
        }, cancellationToken);

    public Task<CloudflareOAuthTokenSet> RefreshAsync(
        CloudflareOAuthClientConfiguration configuration,
        string refreshToken,
        CancellationToken cancellationToken = default) =>
        RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = configuration.ClientId,
            ["refresh_token"] = refreshToken
        }, cancellationToken);

    public async Task RevokeAsync(
        CloudflareOAuthClientConfiguration configuration,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        using var request = new HttpRequestMessage(HttpMethod.Post, CloudflareDesktopOAuth.RevocationEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = configuration.ClientId,
                ["token"] = token
            })
        };
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Cloudflare did not confirm token revocation.");
    }

    private async Task<CloudflareOAuthTokenSet> RequestTokenAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, CloudflareDesktopOAuth.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(values)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Cloudflare authorization could not be completed. Reconnect and try again.");

        TokenResponse? token;
        try { token = JsonSerializer.Deserialize<TokenResponse>(body); }
        catch (JsonException) { throw new InvalidOperationException("Cloudflare returned an invalid token response."); }
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken) ||
            !string.Equals(token.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase) || token.ExpiresIn <= 0)
            throw new InvalidOperationException("Cloudflare returned an incomplete token response.");

        return new CloudflareOAuthTokenSet(
            token.AccessToken,
            "Bearer",
            DateTime.UtcNow.AddSeconds(token.ExpiresIn),
            token.RefreshToken,
            token.Scope,
            token.IdToken);
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("scope")] string? Scope,
        [property: JsonPropertyName("id_token")] string? IdToken);
}

/// <summary>Captures a packaging-registered protocol activation without logging its authorization code.</summary>
public static class CloudflareOAuthCallbackInbox
{
    private static readonly object Gate = new();
    private static Uri? _callback;
    public static event EventHandler? CallbackCaptured;

    public static void Capture(IEnumerable<string> arguments)
    {
        var candidate = arguments.Select(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null)
            .FirstOrDefault(uri => uri is not null && CloudflareDesktopOAuth.IsExpectedApplicationHandoff(uri));
        if (candidate is null) return;
        Capture(candidate);
    }

    public static void Capture(Uri callback)
    {
        if (!CloudflareDesktopOAuth.IsExpectedApplicationHandoff(callback)) return;
        lock (Gate) _callback = callback;
        CallbackCaptured?.Invoke(null, EventArgs.Empty);
    }

    public static Uri? Take()
    {
        lock (Gate)
        {
            var result = _callback;
            _callback = null;
            return result;
        }
    }
}

public sealed record CloudflareOAuthCompletionResult(
    bool Success,
    CloudflareProviderConfiguration? Configuration,
    string Message);

/// <summary>End-to-end source-side coordinator. OAuth secrets remain in the OS secure store.</summary>
public sealed class CloudflareOAuthConnectionService
{
    public const string TokenCredentialKey = "SOCYVIA.Cloudflare.OAuth.Tokens";
    private readonly ISecureCredentialStore _credentials;
    private readonly CloudflareOAuthPendingStore _pending;
    private readonly CloudflareOAuthProtocolClient _protocol;
    private readonly CloudflareApiClient _api;

    public CloudflareOAuthConnectionService(
        ISecureCredentialStore? credentials = null,
        CloudflareOAuthProtocolClient? protocol = null,
        CloudflareApiClient? api = null)
    {
        _credentials = credentials ?? SecureCredentialStoreFactory.Create();
        _pending = new CloudflareOAuthPendingStore(_credentials);
        _protocol = protocol ?? new CloudflareOAuthProtocolClient();
        _api = api ?? new CloudflareApiClient();
    }

    public async Task<CloudflareOAuthAuthorizationRequest?> BeginAsync(
        CloudflareOAuthClientConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (!CloudflareDesktopOAuth.TryCreateAuthorizationRequest(configuration, out var request)) return null;
        await _pending.SaveAsync(request!, cancellationToken);
        return request;
    }

    public async Task<CloudflareOAuthCompletionResult> CompleteAsync(
        CloudflareOAuthClientConfiguration registration,
        Uri callback,
        Func<IReadOnlyList<CloudflareOAuthAccount>, CancellationToken, Task<CloudflareOAuthAccount?>>? selectAccount = null,
        CancellationToken cancellationToken = default)
    {
        var pending = await _pending.TakeAsync(cancellationToken);
        if (pending is null) return new(false, null, "No active Cloudflare authorization request was found. Reconnect to start again.");
        var validated = CloudflareDesktopOAuth.ValidateCallback(callback, pending);
        if (!validated.IsValid) return new(false, null, validated.ErrorDescription ?? "Cloudflare authorization was not accepted.");

        CloudflareOAuthTokenSet tokenSet;
        try
        {
            tokenSet = await _protocol.ExchangeCodeAsync(registration, validated.AuthorizationCode!, pending.CodeVerifier, cancellationToken);
        }
        catch
        {
            await _credentials.RemoveAsync(TokenCredentialKey, cancellationToken);
            throw;
        }

        var accounts = await _api.DiscoverAccountsAsync(tokenSet.AccessToken, cancellationToken);
        if (accounts.Count == 0)
        {
            try { await _protocol.RevokeAsync(registration, tokenSet.RefreshToken ?? tokenSet.AccessToken, cancellationToken); }
            catch { /* the credential is still discarded locally */ }
            return new(false, null, "Connected, but Cloudflare did not authorize access to an account.");
        }

        CloudflareOAuthAccount? selected;
        try
        {
            selected = accounts.Count == 1
                ? accounts[0]
                : selectAccount is null
                    ? null
                    : await selectAccount(accounts, cancellationToken);
        }
        catch
        {
            await RevokeDiscardedAuthorizationAsync(registration, tokenSet, cancellationToken);
            throw;
        }
        if (selected is null || !accounts.Any(account =>
                account.Id.Equals(selected.Id, StringComparison.Ordinal) &&
                account.Name.Equals(selected.Name, StringComparison.Ordinal)))
        {
            await RevokeDiscardedAuthorizationAsync(registration, tokenSet, cancellationToken);
            return new(false, null, "Cloudflare account selection was cancelled. SOCYVIA remains disconnected.");
        }
        var discovered = await _api.DiscoverResearchResourcesAsync(selected.Id, tokenSet.AccessToken, cancellationToken: cancellationToken);
        var configurationStore = new CloudflareProviderConfigurationStore();
        var previousConfiguration = await configurationStore.LoadAsync(cancellationToken);
        if (previousConfiguration?.ConnectionMode == CloudflareConnectionMode.Manual)
            await _credentials.RemoveAsync(previousConfiguration.CredentialKey, cancellationToken);
        await _credentials.StoreAsync(TokenCredentialKey, JsonSerializer.Serialize(tokenSet), cancellationToken);
        var configuration = new CloudflareProviderConfiguration
        {
            AccountId = selected.Id,
            AccountDisplayName = selected.Name,
            D1DatabaseId = discovered.D1DatabaseId,
            D1DatabaseName = discovered.D1DatabaseName,
            R2BucketName = discovered.R2BucketName,
            WorkerName = discovered.WorkerName,
            WorkerEndpoint = discovered.WorkerEndpoint,
            ConnectionMode = CloudflareConnectionMode.OAuth,
            ProviderStatus = CloudflareProviderConnectionState.ConfigurationRequired,
            LastVerifiedAtUtc = DateTime.UtcNow,
            OAuthExpiresAtUtc = tokenSet.ExpiresAtUtc
        };
        await configurationStore.SaveAsync(configuration, cancellationToken);
        return new(true, configuration, configuration.HasRequiredTextRuntimeIdentity
            ? "Cloudflare is connected. Existing SOCYVIA research resources were discovered and are ready for verification."
            : "Cloudflare is connected. Required research resources were not discovered; finish setup before publishing.");
    }

    private async Task RevokeDiscardedAuthorizationAsync(
        CloudflareOAuthClientConfiguration registration,
        CloudflareOAuthTokenSet tokens,
        CancellationToken cancellationToken)
    {
        try { await _protocol.RevokeAsync(registration, tokens.RefreshToken ?? tokens.AccessToken, cancellationToken); }
        catch { /* no credential is persisted when account selection is cancelled */ }
        await _credentials.RemoveAsync(TokenCredentialKey, cancellationToken);
    }

    public async Task<string?> GetAccessTokenAsync(
        CloudflareProviderConfiguration configuration,
        CloudflareOAuthClientConfiguration registration,
        CancellationToken cancellationToken = default)
    {
        var serialized = await _credentials.RetrieveAsync(configuration.CredentialKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(serialized)) return null;
        if (configuration.ConnectionMode == CloudflareConnectionMode.Manual) return serialized;

        var tokens = JsonSerializer.Deserialize<CloudflareOAuthTokenSet>(serialized);
        if (tokens is null) return null;
        if (!tokens.IsExpired(DateTime.UtcNow)) return tokens.AccessToken;
        if (string.IsNullOrWhiteSpace(tokens.RefreshToken))
        {
            await MarkNeedsAttentionAsync(configuration, cancellationToken);
            return null;
        }

        try
        {
            var refreshed = await _protocol.RefreshAsync(registration, tokens.RefreshToken, cancellationToken);
            if (string.IsNullOrWhiteSpace(refreshed.RefreshToken)) refreshed = refreshed with { RefreshToken = tokens.RefreshToken };
            await _credentials.StoreAsync(TokenCredentialKey, JsonSerializer.Serialize(refreshed), cancellationToken);
            await new CloudflareProviderConfigurationStore().SaveAsync(configuration with
            {
                OAuthExpiresAtUtc = refreshed.ExpiresAtUtc,
                ProviderStatus = configuration.HasRequiredTextRuntimeIdentity
                    ? CloudflareProviderConnectionState.Checking
                    : CloudflareProviderConnectionState.ConfigurationRequired
            }, cancellationToken);
            return refreshed.AccessToken;
        }
        catch
        {
            await _credentials.RemoveAsync(TokenCredentialKey, cancellationToken);
            await MarkNeedsAttentionAsync(configuration, cancellationToken);
            return null;
        }
    }

    private static Task MarkNeedsAttentionAsync(
        CloudflareProviderConfiguration configuration,
        CancellationToken cancellationToken) =>
        new CloudflareProviderConfigurationStore().SaveAsync(configuration with
        {
            ProviderStatus = CloudflareProviderConnectionState.NeedsAttention,
            OAuthExpiresAtUtc = null
        }, cancellationToken);

    public async Task DisconnectAsync(
        CloudflareProviderConfiguration? configuration,
        CloudflareOAuthClientConfiguration registration,
        CancellationToken cancellationToken = default)
    {
        if (configuration is not null)
        {
            var serialized = await _credentials.RetrieveAsync(configuration.CredentialKey, cancellationToken);
            if (configuration.ConnectionMode == CloudflareConnectionMode.OAuth && !string.IsNullOrWhiteSpace(serialized))
            {
                try
                {
                    var tokens = JsonSerializer.Deserialize<CloudflareOAuthTokenSet>(serialized);
                    if (tokens is not null)
                        await _protocol.RevokeAsync(registration, tokens.RefreshToken ?? tokens.AccessToken, cancellationToken);
                }
                catch { /* local disconnect still removes the credential */ }
            }
            await _credentials.RemoveAsync(configuration.CredentialKey, cancellationToken);
        }
        await _pending.ClearAsync(cancellationToken);
        await new CloudflareProviderConfigurationStore().RemoveAsync(cancellationToken);
    }
}
