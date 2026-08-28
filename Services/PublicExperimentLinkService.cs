using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

/// <summary>
/// Produces human-facing publication links resolved by the canonical SOCYVIA
/// website route while the established /experimentfeed runtime remains internal.
/// </summary>
public static class PublicExperimentLinkService
{
    public const string LiveRoutingStatus = "LIVE";
    public const string LegacyPreparedRoutingStatus = "PREPARED — WEBSITE ROUTE INTEGRATION REQUIRED";
    public const string RoutingStatus = LiveRoutingStatus;

    public static bool IsCanonicalRouteLive(string? routingStatus) =>
        string.Equals(routingStatus, LiveRoutingStatus, StringComparison.Ordinal);

    public static Uri? DistributableCanonicalUri(PublishedExperimentStatus publication)
    {
        if (!IsCanonicalRouteLive(publication.RoutingStatus) ||
            !Uri.TryCreate(publication.CanonicalParticipantUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("socyvia.com", StringComparison.OrdinalIgnoreCase)) return null;
        return uri;
    }

    public static string CreateResearcherHandle(string publicName)
    {
        var normalized = (publicName ?? string.Empty).Normalize(NormalizationForm.FormD);
        var words = new StringBuilder();
        var separatorPending = false;
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (character <= 0x7f && char.IsLetterOrDigit(character))
            {
                if (separatorPending && words.Length > 0) words.Append('-');
                words.Append(char.ToLowerInvariant(character));
                separatorPending = false;
            }
            else if (char.IsWhiteSpace(character) || char.IsPunctuation(character) || char.IsSeparator(character))
            {
                separatorPending = words.Length > 0;
            }
        }

        var slug = words.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "researcher" : slug;
    }

    public static string CreateUniqueResearcherHandle(string publicName, IEnumerable<string> reservedHandles)
    {
        var baseHandle = CreateResearcherHandle(publicName);
        var reserved = new HashSet<string>(reservedHandles ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        if (!reserved.Contains(baseHandle)) return baseHandle;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseHandle}-{suffix}";
            if (!reserved.Contains(candidate)) return candidate;
        }
    }

    /// <summary>Stable non-sequential eight-digit identifier derived from the immutable deployment identity.</summary>
    public static string CreateResearchNumber(string deploymentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"SOCYVIA-public-research/1|{deploymentId}"));
        var value = BitConverter.ToUInt32(hash, 0) % 90_000_000 + 10_000_000;
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public static Uri CanonicalLiveUri(string researcherHandle, string researchNumber) =>
        new($"https://socyvia.com/{Uri.EscapeDataString(RequireSegment(researcherHandle))}/{Uri.EscapeDataString(RequireSegment(researchNumber))}", UriKind.Absolute);

    public static Uri RuntimeUri(string runtimeBaseUrl, string researcherHandle, string experimentCode) =>
        new($"{runtimeBaseUrl.TrimEnd('/')}/experimentfeed/{Uri.EscapeDataString(RequireSegment(researcherHandle))}/{Uri.EscapeDataString(RequireSegment(experimentCode))}", UriKind.Absolute);

    public static PublishedExperimentLink? ForPublishedDeployment(ExperimentDeployment? deployment)
    {
        if (deployment is null || deployment.Status != ExperimentDeploymentStatus.Published ||
            string.IsNullOrWhiteSpace(deployment.ResearcherHandle)) return null;
        var researchNumber = string.IsNullOrWhiteSpace(deployment.ExperimentCode)
            ? CreateResearchNumber(deployment.DeploymentId)
            : deployment.ExperimentCode;
        return new PublishedExperimentLink(
            CanonicalLiveUri(deployment.ResearcherHandle, researchNumber),
            deployment.ResearcherHandle,
            researchNumber,
            deployment.DeploymentVersion,
            deployment.PublishedAtUtc,
            RoutingStatus);
    }

    private static string RequireSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A public link segment is required.", nameof(value));
        return value.Trim();
    }
}

public sealed record PublishedExperimentLink(
    Uri CanonicalUri,
    string ResearcherHandle,
    string ResearchNumber,
    int DeploymentVersion,
    DateTime? PublishedAtUtc,
    string RoutingStatus);
