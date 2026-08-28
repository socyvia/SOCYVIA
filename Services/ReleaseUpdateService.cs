using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

/// <summary>
/// Boundary for a future explicit update flow. Verification is mandatory and
/// platform installation is intentionally delegated to a trusted installer.
/// </summary>
public interface IReleaseManifestVerifier
{
    bool Verify(string canonicalManifestJson, string keyId, string signature);
}

public sealed class ReleaseUpdateService
{
    private readonly IReleaseManifestVerifier _verifier;
    public ReleaseUpdateService(IReleaseManifestVerifier verifier) => _verifier = verifier;

    public ReleaseUpdateCheckResult Evaluate(string currentVersion, string manifestJson, string platform)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<ReleaseManifest>(manifestJson) ?? throw new InvalidOperationException();
            var canonical = Canonicalize(manifest);
            var computedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
            if (!string.Equals(computedHash, manifest.ManifestHash, StringComparison.OrdinalIgnoreCase) || !_verifier.Verify(canonical, manifest.KeyId, manifest.Signature))
                return new(ReleaseUpdateState.ManifestUntrusted, currentVersion, Message: "The release manifest could not be verified.");
            if (!Version.TryParse(Normalize(currentVersion), out var current) || !Version.TryParse(Normalize(manifest.Version), out var available))
                return new(ReleaseUpdateState.CheckFailed, currentVersion, Message: "The release version is not valid.");
            if (available <= current) return new(ReleaseUpdateState.UpToDate, currentVersion, manifest, Message: "SOCYVIA is up to date.");
            if (!manifest.Artifacts.TryGetValue(platform, out var artifact) || artifact.DownloadUri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(artifact.Sha256))
                return new(ReleaseUpdateState.CheckFailed, currentVersion, manifest, Message: "No verified artifact is available for this platform.");
            return new(ReleaseUpdateState.UpdateAvailable, currentVersion, manifest, artifact, "A verified update is available.");
        }
        catch (Exception) { return new(ReleaseUpdateState.CheckFailed, currentVersion, Message: "The release manifest could not be read."); }
    }

    private static string Canonicalize(ReleaseManifest manifest) => JsonSerializer.Serialize(new { manifest.Channel, manifest.Version, manifest.PublishedAtUtc, manifest.Artifacts, manifest.KeyId });
    private static string Normalize(string version) => version.Split('-', 2)[0];
}

/// <summary>Integrity and platform gates used after a researcher explicitly approves an update.</summary>
public static class ReleaseArtifactIntegrityService
{
    public static bool IsValidArtifact(ReleaseArtifact? artifact, string expectedPlatform)
    {
        return artifact is not null &&
               string.Equals(artifact.Platform, expectedPlatform, StringComparison.Ordinal) &&
               artifact.DownloadUri.IsAbsoluteUri && artifact.DownloadUri.Scheme == Uri.UriSchemeHttps &&
               artifact.Sha256.Length == 64 && artifact.Sha256.All(Uri.IsHexDigit);
    }

    public static async Task<bool> VerifySha256Async(Stream content, string expectedSha256, CancellationToken cancellationToken = default)
    {
        if (content is null || string.IsNullOrWhiteSpace(expectedSha256)) return false;
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(content, cancellationToken)).ToLowerInvariant();
        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Non-fatal official-manifest fetch boundary. No update is downloaded or
/// installed here; the researcher must explicitly approve that later step.
/// </summary>
public sealed class ReleaseUpdateCheckClient
{
    private readonly HttpClient _client;
    private readonly ReleaseUpdateService _updates;

    public ReleaseUpdateCheckClient(IReleaseManifestVerifier verifier, HttpClient? client = null)
    {
        _updates = new ReleaseUpdateService(verifier);
        _client = client ?? new HttpClient();
    }

    public async Task<ReleaseUpdateCheckResult> CheckAsync(string currentVersion, string platform, Uri? manifestUri = null, CancellationToken cancellationToken = default)
    {
        var uri = manifestUri ?? SocyviaProductUrls.ReleaseManifestUri;
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
            return new(ReleaseUpdateState.CheckFailed, currentVersion, Message: "The official update source is not configured securely.");
        try
        {
            var json = await _client.GetStringAsync(uri, cancellationToken);
            return _updates.Evaluate(currentVersion, json, platform);
        }
        catch (Exception)
        {
            return new(ReleaseUpdateState.CheckFailed, currentVersion, Message: "Updates could not be checked. SOCYVIA remains available offline.");
        }
    }
}

/// <summary>Prevents an updater UI from offering an unsafe interrupt during active work.</summary>
public sealed record ReleaseUpdateSafetyState(bool HasUnsavedResearchWork, bool IsPublishing, bool IsSynchronizing, bool IsLocalExperimentRunning)
{
    public bool CanOfferInstall => !HasUnsavedResearchWork && !IsPublishing && !IsSynchronizing && !IsLocalExperimentRunning;
}

/// <summary>Final local gate invoked before control may pass to a trusted, researcher-approved installer.</summary>
public static class ReleaseUpdateInstallGate
{
    public static async Task<bool> PrepareAsync(
        bool isPublishing,
        bool isSynchronizing,
        bool isLocalExperimentRunning,
        CancellationToken cancellationToken = default)
    {
        if (isPublishing || isSynchronizing || isLocalExperimentRunning) return false;
        if (!await StudySaveCoordinatorRegistry.FlushAllAsync(cancellationToken)) return false;
        return !StudySaveCoordinatorRegistry.HasUnsafeChanges;
    }
}
