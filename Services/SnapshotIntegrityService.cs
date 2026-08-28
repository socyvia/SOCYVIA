using System;
using System.Security.Cryptography;
using System.Text;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

public enum SnapshotIntegrityStatus
{
    Valid,
    Invalid,
    UnverifiableLegacy
}

public sealed class SnapshotIntegrityResult
{
    public SnapshotIntegrityStatus Status { get; init; }
    public bool IsValid => Status == SnapshotIntegrityStatus.Valid;
    public string? ExpectedHash { get; init; }
    public string? ActualHash { get; init; }
}

public static class SnapshotIntegrityService
{
    public const string Algorithm = "SHA-256";

    public static string ComputeHash(string snapshotJson)
    {
        ArgumentNullException.ThrowIfNull(snapshotJson);
        var bytes = Encoding.UTF8.GetBytes(snapshotJson);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static SnapshotIntegrityResult Verify(
        string snapshotJson,
        string? storedHash,
        string? storedAlgorithm)
    {
        if (string.IsNullOrWhiteSpace(storedHash) ||
            string.IsNullOrWhiteSpace(storedAlgorithm))
        {
            return new SnapshotIntegrityResult
            {
                Status = SnapshotIntegrityStatus.UnverifiableLegacy
            };
        }

        if (!string.Equals(storedAlgorithm, Algorithm,
                StringComparison.OrdinalIgnoreCase))
        {
            return new SnapshotIntegrityResult
            {
                Status = SnapshotIntegrityStatus.Invalid,
                ExpectedHash = storedHash
            };
        }

        var actualHash = ComputeHash(snapshotJson);
        bool hashesMatch;
        try
        {
            hashesMatch = CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(storedHash),
                Convert.FromHexString(actualHash));
        }
        catch (FormatException)
        {
            hashesMatch = false;
        }

        return new SnapshotIntegrityResult
        {
            Status = hashesMatch
                ? SnapshotIntegrityStatus.Valid
                : SnapshotIntegrityStatus.Invalid,
            ExpectedHash = storedHash,
            ActualHash = actualHash
        };
    }

    public static SnapshotIntegrityResult Verify(
        ExperimentConfigurationSnapshot snapshot)
    {
        return Verify(
            snapshot.PersistedSnapshotJson ??
                ExperimentSnapshotSerializer.Serialize(snapshot),
            snapshot.IntegrityHash,
            snapshot.IntegrityHashAlgorithm);
    }
}
