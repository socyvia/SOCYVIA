using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SOCYVIA.Services;

/// <summary>Secret persistence boundary. SOCYVIA deliberately has no plaintext SQLite, JSON, or settings implementation.</summary>
public interface ISecureCredentialStore
{
    Task StoreAsync(string key, string secret, CancellationToken cancellationToken = default);
    Task<string?> RetrieveAsync(string key, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

public enum CloudflareProviderConnectionState
{
    NotConnected,
    Checking,
    Connected,
    ConfigurationRequired,
    ConnectionFailed,
    Ready,
    NeedsAttention
}

public enum CloudflareConnectionMode { None, OAuth, Manual }

/// <summary>Persistable researcher-owned resource metadata. The API token is intentionally absent.</summary>
public sealed record CloudflareProviderConfiguration
{
    public string AccountId { get; init; } = string.Empty;
    public string AccountDisplayName { get; init; } = string.Empty;
    public string D1DatabaseId { get; init; } = string.Empty;
    public string D1DatabaseName { get; init; } = "socyvia-research";
    public string R2BucketName { get; init; } = string.Empty;
    public string WorkerName { get; init; } = "socyvia-runtime";
    public string WorkerEndpoint { get; init; } = string.Empty;
    public CloudflareProviderConnectionState ProviderStatus { get; init; } = CloudflareProviderConnectionState.NotConnected;
    public CloudflareConnectionMode ConnectionMode { get; init; } = CloudflareConnectionMode.None;
    public DateTime? LastVerifiedAtUtc { get; init; }
    public DateTime? OAuthExpiresAtUtc { get; init; }
    public string CredentialKey => ConnectionMode == CloudflareConnectionMode.OAuth
        ? CloudflareOAuthConnectionService.TokenCredentialKey
        : "SOCYVIA.Cloudflare.Manual." + AccountId.Trim();
    public bool HasRequiredResourceIdentity => !string.IsNullOrWhiteSpace(AccountId) && !string.IsNullOrWhiteSpace(D1DatabaseId) && !string.IsNullOrWhiteSpace(R2BucketName) && Uri.TryCreate(WorkerEndpoint, UriKind.Absolute, out _);
    public bool HasRequiredTextRuntimeIdentity => !string.IsNullOrWhiteSpace(AccountId) && !string.IsNullOrWhiteSpace(D1DatabaseId) && Uri.TryCreate(WorkerEndpoint, UriKind.Absolute, out _);
}

public sealed record CloudflareTokenValidationResult(bool IsValid, string? TokenId = null, string? Status = null, string? Error = null);
public sealed record CloudflareDiscoveredResources(string D1DatabaseId, string D1DatabaseName, string WorkerName, string WorkerEndpoint, string R2BucketName);
public sealed record CloudflareDatabaseProvisionResult(string DatabaseId, string DatabaseName, bool Created);
public sealed record CloudflareBucketProvisionResult(string BucketName, bool Created);
public sealed record CloudflareResourcePlan(string D1DatabaseName = "socyvia-research", string R2BucketName = "socyvia-experiments", string WorkerName = "socyvia-runtime");
public enum CloudflareRuntimeCompatibilityState
{
    Ready,
    DnsPropagationPending,
    TemporarilyUnreachable,
    WrongRuntimeIdentity,
    MissingD1Binding,
    MissingAssetsBinding,
    ProviderFailure
}

public sealed record CloudflareRuntimeCompatibilityResult(
    CloudflareRuntimeCompatibilityState State,
    bool R2Available,
    string Message)
{
    public bool IsReady => State == CloudflareRuntimeCompatibilityState.Ready;
}

public sealed record CloudflareResourceInspectionResult(
    CloudflareProviderConnectionState State,
    bool AccountAvailable,
    bool D1Available,
    bool R2Available,
    bool WorkerHealthy,
    string Message,
    CloudflareRuntimeCompatibilityState RuntimeCompatibility = CloudflareRuntimeCompatibilityState.ProviderFailure);
public sealed record CloudflareApiError(string Code, string Message);

/// <summary>
/// Canonical interpretation of the SOCYVIA runtime health contract. R2 is
/// optional while D1 and ASSETS are required for every participant runtime.
/// </summary>
public static class SocyviaRuntimeHealthContract
{
    public const string RuntimeIdentity = "SOCYVIA Cloudflare Runtime";

    public static CloudflareRuntimeCompatibilityResult Evaluate(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!root.TryGetProperty("runtime", out var runtime) ||
                !string.Equals(runtime.GetString(), RuntimeIdentity, StringComparison.Ordinal))
                return new(CloudflareRuntimeCompatibilityState.WrongRuntimeIdentity, false,
                    "The endpoint did not identify itself as the SOCYVIA Cloudflare Runtime.");
            if (!root.TryGetProperty("d1", out var d1) || d1.ValueKind is not JsonValueKind.True)
                return new(CloudflareRuntimeCompatibilityState.MissingD1Binding, false,
                    "The SOCYVIA runtime is missing its required D1 binding.");
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind is not JsonValueKind.True)
                return new(CloudflareRuntimeCompatibilityState.MissingAssetsBinding, false,
                    "The SOCYVIA runtime is missing its required ASSETS binding.");
            var r2Available = root.TryGetProperty("r2", out var r2) && r2.ValueKind is JsonValueKind.True;
            return new(CloudflareRuntimeCompatibilityState.Ready, r2Available,
                r2Available
                    ? "The SOCYVIA runtime is healthy and media storage is available."
                    : "The SOCYVIA runtime is healthy. Media storage is optional and is not configured.");
        }
        catch (JsonException)
        {
            return new(CloudflareRuntimeCompatibilityState.WrongRuntimeIdentity, false,
                "The endpoint returned an invalid SOCYVIA runtime health response.");
        }
    }

    public static bool IsDnsResolutionFailure(HttpRequestException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is not SocketException socket) continue;
            return socket.SocketErrorCode is SocketError.HostNotFound or SocketError.TryAgain or
                SocketError.NoRecovery or SocketError.NoData;
        }
        return false;
    }
}

public sealed class CloudflareApiException : InvalidOperationException
{
    public CloudflareApiException(string operation, int? httpStatusCode, IReadOnlyList<CloudflareApiError> errors)
        : base(BuildMessage(operation, httpStatusCode, errors))
    {
        Operation = operation;
        HttpStatusCode = httpStatusCode;
        Errors = errors;
    }

    public string Operation { get; }
    public int? HttpStatusCode { get; }
    public IReadOnlyList<CloudflareApiError> Errors { get; }

    private static string BuildMessage(string operation, int? status, IReadOnlyList<CloudflareApiError> errors)
    {
        var statusText = status.HasValue ? $" HTTP {status.Value}." : string.Empty;
        var detail = errors.Count == 0
            ? string.Empty
            : " " + string.Join(" | ", errors.Take(3).Select(error => $"[{error.Code}] {Sanitize(error.Message)}"));
        return operation + statusText + detail;
    }

    private static string Sanitize(string value)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= 300 ? singleLine : singleLine[..300] + "…";
    }
}

public static class SecureCredentialStoreFactory
{
    public static ISecureCredentialStore Create() => OperatingSystem.IsWindows() ? new WindowsCredentialStore() : new UnsupportedSecureCredentialStore();
}

public sealed class UnsupportedSecureCredentialStore : ISecureCredentialStore
{
    private static Exception Unsupported() => new PlatformNotSupportedException("Secure credential storage is not available on this operating system. SOCYVIA will not fall back to plaintext storage.");
    public Task StoreAsync(string key, string secret, CancellationToken cancellationToken = default) => Task.FromException(Unsupported());
    public Task<string?> RetrieveAsync(string key, CancellationToken cancellationToken = default) => Task.FromException<string?>(Unsupported());
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.FromException(Unsupported());
}

/// <summary>Windows Credential Manager implementation; secrets never enter SOCYVIA settings or SQLite.</summary>
public sealed class WindowsCredentialStore : ISecureCredentialStore
{
    private const int CredTypeGeneric = 1, CredPersistLocalMachine = 2, ErrorNotFound = 1168;
    public Task StoreAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = Encoding.Unicode.GetBytes(secret); var pointer = Marshal.AllocCoTaskMem(bytes.Length);
        try { Marshal.Copy(bytes, 0, pointer, bytes.Length); var credential = new NativeCredential { Type = CredTypeGeneric, TargetName = key, CredentialBlobSize = (uint)bytes.Length, CredentialBlob = pointer, Persist = CredPersistLocalMachine, UserName = "SOCYVIA" }; if (!CredWrite(ref credential, 0)) throw new InvalidOperationException("Windows Credential Manager could not securely save the credential."); return Task.CompletedTask; }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }
    public Task<string?> RetrieveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CredRead(key, CredTypeGeneric, 0, out var pointer)) { if (Marshal.GetLastWin32Error() == ErrorNotFound) return Task.FromResult<string?>(null); throw new InvalidOperationException("Windows Credential Manager could not read the credential."); }
        try { var credential = Marshal.PtrToStructure<NativeCredential>(pointer); var bytes = new byte[credential.CredentialBlobSize]; Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length); return Task.FromResult<string?>(Encoding.Unicode.GetString(bytes)); }
        finally { CredFree(pointer); }
    }
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); if (!CredDelete(key, CredTypeGeneric, 0) && Marshal.GetLastWin32Error() != ErrorNotFound) throw new InvalidOperationException("Windows Credential Manager could not remove the credential."); return Task.CompletedTask;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NativeCredential { public uint Flags; public uint Type; public string TargetName; public string Comment; public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten; public uint CredentialBlobSize; public IntPtr CredentialBlob; public uint Persist; public uint AttributeCount; public IntPtr Attributes; public string TargetAlias; public string UserName; }
    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredWrite([In] ref NativeCredential credential, uint flags);
    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("Advapi32.dll", SetLastError = true)] private static extern void CredFree(IntPtr buffer);
}

public sealed class CloudflareProviderConfigurationStore
{
    private static readonly string ConfigurationFile = Path.Combine(StorageService.SettingsFolder, "cloudflare-provider.json");
    public async Task<CloudflareProviderConfiguration?> LoadAsync(CancellationToken cancellationToken = default) { if (!File.Exists(ConfigurationFile)) return null; await using var stream = File.OpenRead(ConfigurationFile); return await JsonSerializer.DeserializeAsync<CloudflareProviderConfiguration>(stream, cancellationToken: cancellationToken); }
    public async Task SaveAsync(CloudflareProviderConfiguration configuration, CancellationToken cancellationToken = default) { Directory.CreateDirectory(StorageService.SettingsFolder); await using var stream = File.Create(ConfigurationFile); await JsonSerializer.SerializeAsync(stream, configuration, cancellationToken: cancellationToken); }
    public Task RemoveAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); if (File.Exists(ConfigurationFile)) File.Delete(ConfigurationFile); return Task.CompletedTask; }
}

/// <summary>Scoped-token Cloudflare REST client. It accepts the token only in memory and never logs it or API response bodies.</summary>
public sealed class CloudflareApiClient
{
    private readonly HttpClient _httpClient;
    public CloudflareApiClient(HttpClient? httpClient = null) => _httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") };
    public async Task<CloudflareTokenValidationResult> ValidateScopedTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return new(false, Error: "A scoped Cloudflare API token is required.");
        using var request = Authorized(HttpMethod.Get, "user/tokens/verify", token); using var response = await _httpClient.SendAsync(request, cancellationToken); var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) return new(false, Error: "Cloudflare token verification failed.");
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        if (!root.TryGetProperty("success", out var successValue) || !successValue.GetBoolean()) return new(false, Error: "Cloudflare rejected the supplied token.");
        var result = root.GetProperty("result"); return new(true, result.TryGetProperty("id", out var id) ? id.GetString() : null, result.TryGetProperty("status", out var status) ? status.GetString() : null);
    }
    public async Task<IReadOnlyList<CloudflareOAuthAccount>> DiscoverAccountsAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return Array.Empty<CloudflareOAuthAccount>();
        using var request = Authorized(HttpMethod.Get, "accounts?per_page=50", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return Array.Empty<CloudflareOAuthAccount>();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        if (!root.TryGetProperty("success", out var success) || !success.GetBoolean() ||
            !root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            return Array.Empty<CloudflareOAuthAccount>();
        var accounts = new System.Collections.Generic.List<CloudflareOAuthAccount>();
        foreach (var item in result.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idValue) || !item.TryGetProperty("name", out var nameValue)) continue;
            var id = idValue.GetString(); var name = nameValue.GetString();
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) accounts.Add(new(id, name));
        }
        return accounts;
    }

    public async Task<CloudflareDatabaseProvisionResult> EnsureD1DatabaseAsync(
        string accountId,
        string token,
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        var escapedAccount = Uri.EscapeDataString(accountId);
        var existing = SelectPreferred(
            await ReadNamedResourcesAsync($"accounts/{escapedAccount}/d1/database?per_page=100", token, "name", ["uuid", "id"], cancellationToken),
            databaseName);
        if (existing is not null && existing.Value.Name.Equals(databaseName, StringComparison.OrdinalIgnoreCase))
            return new(existing.Value.Id, existing.Value.Name, false);

        using var request = Authorized(HttpMethod.Post, $"accounts/{escapedAccount}/d1/database", token);
        request.Content = JsonContent.Create(new { name = databaseName });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        using var document = await ReadSuccessfulResponseAsync(response, "Cloudflare could not create the SOCYVIA research database.", cancellationToken);
        var result = document.RootElement.GetProperty("result");
        var id = result.TryGetProperty("uuid", out var uuid) ? uuid.GetString() : null;
        var name = result.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : databaseName;
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("Cloudflare created a database without returning its resource identity.");
        return new(id, string.IsNullOrWhiteSpace(name) ? databaseName : name, true);
    }

    /// <summary>
    /// Reuses the named researcher-owned R2 bucket or creates it once. The
    /// operation is intentionally non-destructive and propagates Cloudflare
    /// activation/permission failures instead of treating them as "not found".
    /// </summary>
    public async Task<CloudflareBucketProvisionResult> EnsureR2BucketAsync(
        string accountId,
        string token,
        string bucketName,
        CancellationToken cancellationToken = default)
    {
        var escapedAccount = Uri.EscapeDataString(accountId);
        using (var listRequest = Authorized(HttpMethod.Get,
                   $"accounts/{escapedAccount}/r2/buckets?per_page=100", token))
        using (var listResponse = await _httpClient.SendAsync(listRequest, cancellationToken))
        using (var listDocument = await ReadSuccessfulResponseAsync(
                   listResponse, "Cloudflare could not inspect SOCYVIA media storage.", cancellationToken))
        {
            if (listDocument.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("buckets", out var buckets) &&
                buckets.ValueKind == JsonValueKind.Array &&
                buckets.EnumerateArray().Any(item =>
                    item.TryGetProperty("name", out var name) &&
                    string.Equals(name.GetString(), bucketName, StringComparison.OrdinalIgnoreCase)))
                return new(bucketName, false);
        }

        using var createRequest = Authorized(HttpMethod.Post, $"accounts/{escapedAccount}/r2/buckets", token);
        createRequest.Content = JsonContent.Create(new { name = bucketName });
        using var createResponse = await _httpClient.SendAsync(createRequest, cancellationToken);
        using var createDocument = await ReadSuccessfulResponseAsync(
            createResponse, "Cloudflare could not create SOCYVIA media storage.", cancellationToken);
        var createdName = createDocument.RootElement.TryGetProperty("result", out var createResult) &&
                          createResult.TryGetProperty("name", out var nameValue)
            ? nameValue.GetString()
            : null;
        if (!string.Equals(createdName, bucketName, StringComparison.OrdinalIgnoreCase) ||
            !await VerifyR2Async(accountId, bucketName, token, cancellationToken))
            throw new CloudflareApiException(
                "Cloudflare did not confirm the SOCYVIA media-storage resource.",
                (int)createResponse.StatusCode,
                [new CloudflareApiError("R2_NOT_CONFIRMED", "The created bucket could not be verified.")]);
        return new(bucketName, true);
    }

    public async Task ExecuteD1StatementAsync(
        string accountId,
        string databaseId,
        string token,
        string statement,
        CancellationToken cancellationToken = default)
    {
        using var ignored = await ExecuteD1Async(accountId, databaseId, token, statement, cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> QueryD1RowsAsync(
        string accountId,
        string databaseId,
        string token,
        string statement,
        CancellationToken cancellationToken = default)
    {
        using var document = await ExecuteD1Async(accountId, databaseId, token, statement, cancellationToken);
        var rows = new List<IReadOnlyDictionary<string, string?>>();
        foreach (var block in document.RootElement.GetProperty("result").EnumerateArray())
        {
            if (!block.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) continue;
            foreach (var row in results.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                rows.Add(row.EnumerateObject().ToDictionary(
                    property => property.Name,
                    property => property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.ToString(),
                    StringComparer.OrdinalIgnoreCase));
            }
        }
        return rows;
    }

    private async Task<JsonDocument> ExecuteD1Async(
        string accountId,
        string databaseId,
        string token,
        string statement,
        CancellationToken cancellationToken)
    {
        CloudflareApiException? lastFailure = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var request = Authorized(HttpMethod.Post,
                    $"accounts/{Uri.EscapeDataString(accountId)}/d1/database/{Uri.EscapeDataString(databaseId)}/query", token);
                request.Content = JsonContent.Create(new { sql = statement, @params = Array.Empty<string>() });
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var document = await ReadSuccessfulResponseAsync(response, "Cloudflare could not prepare the SOCYVIA research database.", cancellationToken);
                if (!document.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
                {
                    document.Dispose();
                    throw new CloudflareApiException("Cloudflare returned an invalid D1 query result.", (int)response.StatusCode, []);
                }
                var failures = result.EnumerateArray()
                    .Where(item => item.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
                    .Select(item => new CloudflareApiError(
                        "D1_QUERY_FAILED",
                        item.TryGetProperty("error", out var error) ? error.ToString() : "The D1 statement was rejected."))
                    .ToArray();
                if (failures.Length == 0) return document;
                document.Dispose();
                throw new CloudflareApiException("Cloudflare did not apply the SOCYVIA database statement.", (int)response.StatusCode, failures);
            }
            catch (CloudflareApiException exception) when (attempt < 2)
            {
                lastFailure = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(attempt == 0 ? 250 : 750), cancellationToken);
            }
        }
        throw lastFailure ?? new CloudflareApiException("Cloudflare could not prepare the SOCYVIA research database.", null, []);
    }

    public async Task<bool> WorkerExistsAsync(string accountId, string token, string workerName, CancellationToken cancellationToken = default)
    {
        var workers = await ReadNamedResourcesAsync(
            $"accounts/{Uri.EscapeDataString(accountId)}/workers/scripts", token, "id", ["id"], cancellationToken);
        return workers.Any(item => item.Name.Equals(workerName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> WorkerUsesD1BindingAsync(
        string accountId,
        string token,
        string workerName,
        string databaseId,
        CancellationToken cancellationToken = default)
    {
        using var request = Authorized(HttpMethod.Get,
            $"accounts/{Uri.EscapeDataString(accountId)}/workers/scripts/{Uri.EscapeDataString(workerName)}/settings", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        using var document = await ReadSuccessfulResponseAsync(response,
            "Cloudflare could not inspect the SOCYVIA runtime bindings.", cancellationToken);
        if (!document.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("bindings", out var bindings) || bindings.ValueKind != JsonValueKind.Array)
            return false;
        return bindings.EnumerateArray().Any(binding =>
            binding.TryGetProperty("type", out var type) && type.GetString() == "d1" &&
            binding.TryGetProperty("name", out var name) && name.GetString() == "DB" &&
            ((binding.TryGetProperty("database_id", out var id) && id.GetString() == databaseId) ||
             (binding.TryGetProperty("id", out var legacyId) && legacyId.GetString() == databaseId)));
    }

    public async Task<string> EnsureWorkerSubdomainAsync(
        string accountId,
        string token,
        string workerName,
        CancellationToken cancellationToken = default)
    {
        var escapedAccount = Uri.EscapeDataString(accountId);
        var escapedWorker = Uri.EscapeDataString(workerName);
        var subdomain = await ReadScalarResultAsync($"accounts/{escapedAccount}/workers/subdomain", token, "subdomain", cancellationToken);
        if (string.IsNullOrWhiteSpace(subdomain))
        {
            var requestedSubdomain = StableWorkerSubdomain(accountId);
            using var accountRequest = Authorized(HttpMethod.Put, $"accounts/{escapedAccount}/workers/subdomain", token);
            accountRequest.Content = JsonContent.Create(new { subdomain = requestedSubdomain });
            using var accountResponse = await _httpClient.SendAsync(accountRequest, cancellationToken);
            using var accountDocument = await ReadSuccessfulResponseAsync(accountResponse,
                "Cloudflare could not create the SOCYVIA workers.dev account endpoint.", cancellationToken);
            subdomain = accountDocument.RootElement.TryGetProperty("result", out var accountResult) &&
                        accountResult.TryGetProperty("subdomain", out var subdomainValue)
                ? subdomainValue.GetString()
                : null;
        }
        using (var request = Authorized(HttpMethod.Post, $"accounts/{escapedAccount}/workers/scripts/{escapedWorker}/subdomain", token))
        {
            request.Content = JsonContent.Create(new { enabled = true, previews_enabled = false });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            using var ignored = await ReadSuccessfulResponseAsync(response, "Cloudflare could not enable the SOCYVIA runtime endpoint.", cancellationToken);
        }
        if (string.IsNullOrWhiteSpace(subdomain))
            throw new InvalidOperationException("Cloudflare did not provide a workers.dev account subdomain.");
        return $"https://{workerName}.{subdomain}.workers.dev";
    }

    internal static string StableWorkerSubdomain(string accountId)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountId))).ToLowerInvariant();
        return "socyvia-" + digest[..12];
    }

    public async Task<string> BeginWorkerAssetsUploadAsync(
        string accountId,
        string token,
        string workerName,
        IReadOnlyDictionary<string, object> manifest,
        CancellationToken cancellationToken = default)
    {
        using var request = Authorized(HttpMethod.Post,
            $"accounts/{Uri.EscapeDataString(accountId)}/workers/scripts/{Uri.EscapeDataString(workerName)}/assets-upload-session", token);
        request.Content = JsonContent.Create(new { manifest });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        using var document = await ReadSuccessfulResponseAsync(response, "Cloudflare could not begin the participant-runtime asset upload.", cancellationToken);
        var result = document.RootElement.GetProperty("result");
        var jwt = result.TryGetProperty("jwt", out var jwtValue) ? jwtValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(jwt)) throw new InvalidOperationException("Cloudflare did not issue an asset upload token.");
        return JsonSerializer.Serialize(new
        {
            jwt,
            buckets = result.TryGetProperty("buckets", out var buckets) ? buckets.Clone() : default(JsonElement)
        });
    }

    public async Task<string?> UploadWorkerAssetBatchAsync(
        string accountId,
        string uploadToken,
        IReadOnlyDictionary<string, (string Base64, string MimeType)> assets,
        CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        foreach (var (hash, asset) in assets)
        {
            var part = new StringContent(asset.Base64, Encoding.UTF8, asset.MimeType);
            content.Add(part, hash);
        }
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"accounts/{Uri.EscapeDataString(accountId)}/workers/assets/upload?base64=true") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", uploadToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        using var document = await ReadSuccessfulResponseAsync(response, "Cloudflare could not upload the participant-runtime assets.", cancellationToken);
        return document.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("jwt", out var jwt)
            ? jwt.GetString()
            : null;
    }

    public async Task UploadWorkerModuleAsync(
        string accountId,
        string token,
        string workerName,
        string databaseId,
        byte[] module,
        string completionToken,
        string? r2BucketName = null,
        CancellationToken cancellationToken = default)
    {
        var bindings = new List<object>
        {
            new { type = "d1", name = "DB", database_id = databaseId },
            new { type = "assets", name = "ASSETS" }
        };
        if (!string.IsNullOrWhiteSpace(r2BucketName))
            bindings.Add(new { type = "r2_bucket", name = "MEDIA", bucket_name = r2BucketName });
        var metadata = JsonSerializer.Serialize(new
        {
            main_module = "socyvia-runtime.js",
            compatibility_date = "2026-08-21",
            bindings,
            // A media-only runtime update must not remove the owner-managed AI
            // secret or the server-side abuse-protection bindings.
            keep_bindings = new[] { "secret_text", "ratelimit" },
            assets = new
            {
                jwt = completionToken,
                config = new
                {
                    not_found_handling = "single-page-application",
                    run_worker_first = new[] { "/api/*", "/media/*", "/experimentfeed/*", "/oauth/cloudflare/callback*" }
                }
            },
            annotations = new Dictionary<string, string> { ["workers/message"] = "SOCYVIA desktop automatic research environment setup" }
        });
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(metadata, Encoding.UTF8, "application/json"), "metadata");
        var script = new ByteArrayContent(module);
        script.Headers.ContentType = new MediaTypeHeaderValue("application/javascript+module");
        content.Add(script, "socyvia-runtime.js", "socyvia-runtime.js");
        using var request = Authorized(HttpMethod.Put,
            $"accounts/{Uri.EscapeDataString(accountId)}/workers/scripts/{Uri.EscapeDataString(workerName)}", token);
        request.Content = content;
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        using var ignored = await ReadSuccessfulResponseAsync(response, "Cloudflare could not deploy the SOCYVIA participant runtime.", cancellationToken);
    }
    /// <summary>
    /// Discovers an already-provisioned SOCYVIA resource set through Cloudflare's read-only list APIs.
    /// It selects the preferred SOCYVIA name, or the sole accessible resource when unambiguous.
    /// </summary>
    public async Task<CloudflareDiscoveredResources> DiscoverResearchResourcesAsync(
        string accountId,
        string token,
        CloudflareResourcePlan? plan = null,
        CancellationToken cancellationToken = default)
    {
        plan ??= new CloudflareResourcePlan();
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(token))
            return new(string.Empty, plan.D1DatabaseName, plan.WorkerName, string.Empty, string.Empty);

        var escapedAccount = Uri.EscapeDataString(accountId);
        var databasesTask = ReadNamedResourcesAsync($"accounts/{escapedAccount}/d1/database?per_page=100", token, "name", ["uuid", "id"], cancellationToken);
        var workersTask = ReadNamedResourcesAsync($"accounts/{escapedAccount}/workers/scripts", token, "id", ["id"], cancellationToken);
        var bucketsTask = ReadBucketsAsync($"accounts/{escapedAccount}/r2/buckets?per_page=100", token, cancellationToken);
        await Task.WhenAll(databasesTask, workersTask, bucketsTask);

        var database = SelectPreferred(databasesTask.Result, plan.D1DatabaseName);
        var worker = SelectPreferred(workersTask.Result, plan.WorkerName);
        var bucket = SelectPreferred(bucketsTask.Result, plan.R2BucketName);
        var endpoint = string.Empty;
        if (worker is not null)
        {
            var enabled = await ReadBooleanResultAsync(
                $"accounts/{escapedAccount}/workers/scripts/{Uri.EscapeDataString(worker.Value.Name)}/subdomain",
                token, "enabled", cancellationToken);
            var subdomain = enabled
                ? await ReadScalarResultAsync($"accounts/{escapedAccount}/workers/subdomain", token, "subdomain", cancellationToken)
                : null;
            if (!string.IsNullOrWhiteSpace(subdomain)) endpoint = $"https://{worker.Value.Name}.{subdomain}.workers.dev";
        }

        return new(database?.Id ?? string.Empty, database?.Name ?? plan.D1DatabaseName,
            worker?.Name ?? plan.WorkerName, endpoint, bucket?.Name ?? string.Empty);
    }

    private async Task<IReadOnlyList<(string Id, string Name)>> ReadNamedResourcesAsync(
        string path, string token, string nameProperty, IReadOnlyList<string> idProperties, CancellationToken cancellationToken)
    {
        try
        {
            using var request = Authorized(HttpMethod.Get, path, token);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return Array.Empty<(string, string)>();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!IsSuccessful(document.RootElement) || !document.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
                return Array.Empty<(string, string)>();
            var resources = new List<(string Id, string Name)>();
            foreach (var item in result.EnumerateArray())
            {
                if (!item.TryGetProperty(nameProperty, out var nameValue)) continue;
                var name = nameValue.GetString();
                string? id = null;
                foreach (var property in idProperties)
                    if (item.TryGetProperty(property, out var idValue) && !string.IsNullOrWhiteSpace(id = idValue.GetString())) break;
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) resources.Add((id, name));
            }
            return resources;
        }
        catch (HttpRequestException) { return Array.Empty<(string, string)>(); }
        catch (JsonException) { return Array.Empty<(string, string)>(); }
    }

    private async Task<IReadOnlyList<(string Id, string Name)>> ReadBucketsAsync(string path, string token, CancellationToken cancellationToken)
    {
        try
        {
            using var request = Authorized(HttpMethod.Get, path, token);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return Array.Empty<(string, string)>();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!IsSuccessful(document.RootElement) || !document.RootElement.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("buckets", out var buckets) || buckets.ValueKind != JsonValueKind.Array)
                return Array.Empty<(string, string)>();
            return buckets.EnumerateArray()
                .Select(item => item.TryGetProperty("name", out var value) ? value.GetString() : null)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => (name!, name!))
                .ToArray();
        }
        catch (HttpRequestException) { return Array.Empty<(string, string)>(); }
        catch (JsonException) { return Array.Empty<(string, string)>(); }
    }

    private async Task<bool> ReadBooleanResultAsync(string path, string token, string property, CancellationToken cancellationToken)
    {
        var value = await ReadScalarResultAsync(path, token, property, cancellationToken);
        return bool.TryParse(value, out var parsed) && parsed;
    }

    private async Task<string?> ReadScalarResultAsync(string path, string token, string property, CancellationToken cancellationToken)
    {
        try
        {
            using var request = Authorized(HttpMethod.Get, path, token);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!IsSuccessful(document.RootElement) || !document.RootElement.TryGetProperty("result", out var result) ||
                !result.TryGetProperty(property, out var value)) return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }
        catch (HttpRequestException) { return null; }
        catch (JsonException) { return null; }
    }

    private static (string Id, string Name)? SelectPreferred(IReadOnlyList<(string Id, string Name)> resources, string preferredName)
    {
        var preferred = resources.FirstOrDefault(item => string.Equals(item.Name, preferredName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(preferred.Id)) return preferred;
        return resources.Count == 1 ? resources[0] : null;
    }

    private static bool IsSuccessful(JsonElement root) =>
        root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True;

    private static async Task<JsonDocument> ReadSuccessfulResponseAsync(
        HttpResponseMessage response,
        string message,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument document;
        try { document = JsonDocument.Parse(body); }
        catch (JsonException)
        {
            throw new CloudflareApiException(message, (int)response.StatusCode,
                [new CloudflareApiError("INVALID_RESPONSE", "Cloudflare returned a non-JSON response.")]);
        }
        if (!response.IsSuccessStatusCode || !IsSuccessful(document.RootElement))
        {
            var errors = ReadApiErrors(document.RootElement);
            document.Dispose();
            throw new CloudflareApiException(message, (int)response.StatusCode, errors);
        }
        return document;
    }

    private static IReadOnlyList<CloudflareApiError> ReadApiErrors(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
            return Array.Empty<CloudflareApiError>();
        return errors.EnumerateArray().Select(error => new CloudflareApiError(
            error.TryGetProperty("code", out var code) ? code.ToString() : "CLOUDFLARE_ERROR",
            error.TryGetProperty("message", out var message) ? message.GetString() ?? "Cloudflare rejected the operation." : "Cloudflare rejected the operation."))
            .ToArray();
    }

    public Task<bool> VerifyAccountAsync(string accountId, string token, CancellationToken cancellationToken = default) => SendSuccessAsync(HttpMethod.Get, $"accounts/{Uri.EscapeDataString(accountId)}", token, cancellationToken);
    public Task<bool> VerifyD1Async(string accountId, string databaseId, string token, CancellationToken cancellationToken = default) => SendSuccessAsync(HttpMethod.Get, $"accounts/{Uri.EscapeDataString(accountId)}/d1/database/{Uri.EscapeDataString(databaseId)}", token, cancellationToken);
    public Task<bool> VerifyR2Async(string accountId, string bucketName, string token, CancellationToken cancellationToken = default) => SendSuccessAsync(HttpMethod.Get, $"accounts/{Uri.EscapeDataString(accountId)}/r2/buckets/{Uri.EscapeDataString(bucketName)}", token, cancellationToken);
    private async Task<bool> SendSuccessAsync(HttpMethod method, string path, string token, CancellationToken cancellationToken) { using var request = Authorized(method, path, token); using var response = await _httpClient.SendAsync(request, cancellationToken); if (!response.IsSuccessStatusCode) return false; using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken)); return document.RootElement.TryGetProperty("success", out var success) && success.GetBoolean(); }
    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token) { var request = new HttpRequestMessage(method, path); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return request; }
}

public sealed class CloudflareConnectionService
{
    private const int MaximumHealthPayloadBytes = 16 * 1024;
    private readonly CloudflareApiClient _api; private readonly HttpClient _http;
    public CloudflareConnectionService(CloudflareApiClient? api = null, HttpClient? http = null) { _api = api ?? new CloudflareApiClient(); _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) }; }
    public async Task<CloudflareResourceInspectionResult> InspectAsync(CloudflareProviderConfiguration configuration, string token, bool requireMediaStorage = false, CancellationToken cancellationToken = default)
    {
        if (requireMediaStorage ? !configuration.HasRequiredResourceIdentity : !configuration.HasRequiredTextRuntimeIdentity)
            return new(CloudflareProviderConnectionState.ConfigurationRequired, false, false, false, false,
                requireMediaStorage
                    ? "Account, D1, media storage, and Worker endpoint are required."
                    : "Account, D1, and Worker endpoint are required for a text-only remote experiment.");
        if (configuration.ConnectionMode == CloudflareConnectionMode.OAuth)
        {
            var accounts = await _api.DiscoverAccountsAsync(token, cancellationToken);
            if (!accounts.Any(item => item.Id == configuration.AccountId))
                return new(CloudflareProviderConnectionState.NeedsAttention, false, false, false, false, "The OAuth connection no longer authorizes the selected Cloudflare account.");
        }
        else
        {
            var verified = await _api.ValidateScopedTokenAsync(token, cancellationToken);
            if (!verified.IsValid) return new(CloudflareProviderConnectionState.NeedsAttention, false, false, false, false, verified.Error ?? "Token validation failed.");
        }
        var account = await _api.VerifyAccountAsync(configuration.AccountId, token, cancellationToken); var d1 = account && await _api.VerifyD1Async(configuration.AccountId, configuration.D1DatabaseId, token, cancellationToken); var r2 = requireMediaStorage && account && await _api.VerifyR2Async(configuration.AccountId, configuration.R2BucketName, token, cancellationToken);
        var runtime = await InspectRuntimeAsync(configuration.WorkerEndpoint, cancellationToken);
        var worker = runtime.IsReady;
        var ready = account && d1 && worker && (!requireMediaStorage || r2);
        var state = ready
            ? CloudflareProviderConnectionState.Ready
            : runtime.State == CloudflareRuntimeCompatibilityState.DnsPropagationPending
                ? CloudflareProviderConnectionState.Checking
                : CloudflareProviderConnectionState.NeedsAttention;
        var message = state == CloudflareProviderConnectionState.Ready
            ? (requireMediaStorage ? "Cloudflare research and media resources are available." : "Cloudflare research database and runtime are available. Media storage is optional for text-only experiments.")
            : !account ? "Token is valid but cannot access the selected Cloudflare account."
            : !d1 ? "Token is valid but D1 is unavailable."
            : requireMediaStorage && !r2 ? "Media storage was not found or cannot be accessed."
            : runtime.Message;
        return new(state, account, d1, r2, worker, message, runtime.State);
    }

    private async Task<CloudflareRuntimeCompatibilityResult> InspectRuntimeAsync(
        string workerEndpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri(new Uri(workerEndpoint.TrimEnd('/') + "/"), "api/health"));
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(CloudflareRuntimeCompatibilityState.ProviderFailure, false,
                    $"The SOCYVIA runtime health endpoint returned HTTP {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength is > MaximumHealthPayloadBytes)
                return new(CloudflareRuntimeCompatibilityState.WrongRuntimeIdentity, false,
                    "The endpoint returned an invalid SOCYVIA runtime health response.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[2048];
            while (buffer.Length <= MaximumHealthPayloadBytes)
            {
                var read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
                if (read == 0) break;
                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }
            if (buffer.Length > MaximumHealthPayloadBytes)
                return new(CloudflareRuntimeCompatibilityState.WrongRuntimeIdentity, false,
                    "The endpoint returned an invalid SOCYVIA runtime health response.");
            return SocyviaRuntimeHealthContract.Evaluate(Encoding.UTF8.GetString(buffer.ToArray()));
        }
        catch (HttpRequestException exception) when (SocyviaRuntimeHealthContract.IsDnsResolutionFailure(exception))
        {
            return new(CloudflareRuntimeCompatibilityState.DnsPropagationPending, false,
                "The SOCYVIA runtime is ready in Cloudflare while its secure endpoint finishes DNS propagation.");
        }
        catch (HttpRequestException)
        {
            return new(CloudflareRuntimeCompatibilityState.TemporarilyUnreachable, false,
                "The SOCYVIA runtime is temporarily unreachable. Retry the connection check.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(CloudflareRuntimeCompatibilityState.TemporarilyUnreachable, false,
                "The SOCYVIA runtime health check timed out. Retry the connection check.");
        }
    }
}
