using System;
using System.Collections.Generic;

namespace SOCYVIA.Models;

/// <summary>Trusted-manifest contract only. SOCYVIA never silently downloads or installs a release.</summary>
public sealed record ReleaseManifest(
    string Channel,
    string Version,
    DateTime PublishedAtUtc,
    IReadOnlyDictionary<string, ReleaseArtifact> Artifacts,
    string KeyId,
    string Signature,
    string ManifestHash);

public sealed record ReleaseArtifact(
    string Platform,
    Uri DownloadUri,
    string Sha256,
    long? SizeBytes,
    string? ReleaseNotesUrl = null);

public enum ReleaseUpdateState { NotChecked, UpToDate, UpdateAvailable, ManifestUntrusted, CheckFailed }

public sealed record ReleaseUpdateCheckResult(
    ReleaseUpdateState State,
    string CurrentVersion,
    ReleaseManifest? Manifest = null,
    ReleaseArtifact? Artifact = null,
    string? Message = null);
