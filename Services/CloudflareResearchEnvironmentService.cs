using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SOCYVIA.Services;

public enum CloudflareEnvironmentStage
{
    AuthorizationReceived,
    PreparingEnvironment,
    CheckingAccount,
    CheckingDatabase,
    CheckingRuntime,
    TestingConnection,
    Ready,
    ConnectionProblem
}

public sealed record CloudflareEnvironmentProgress(CloudflareEnvironmentStage Stage, string Message);

public sealed record CloudflareEnvironmentSetupResult(
    bool Succeeded,
    CloudflareProviderConfiguration Configuration,
    bool DatabaseCreated,
    bool RuntimeCreated,
    string Message);

public sealed record CloudflareMediaStorageSetupResult(
    bool Succeeded,
    CloudflareProviderConfiguration Configuration,
    bool BucketCreated,
    string Message);

/// <summary>
/// Idempotently prepares the researcher-owned SOCYVIA D1/runtime pair after OAuth.
/// Normal text-only preparation never creates R2, modifies zones, or overwrites
/// a pre-existing unhealthy Worker. Media preparation is a separate explicit,
/// idempotent operation.
/// </summary>
public sealed class CloudflareResearchEnvironmentService
{
    private readonly CloudflareApiClient _api;
    private readonly CloudflareConnectionService _connection;
    private readonly string _runtimeBundlePath;
    private readonly string _runtimeAssetsPath;
    private readonly string _schemaPath;

    public CloudflareResearchEnvironmentService(
        CloudflareApiClient? api = null,
        CloudflareConnectionService? connection = null,
        string? runtimeBundlePath = null,
        string? runtimeAssetsPath = null,
        string? schemaPath = null)
    {
        _api = api ?? new CloudflareApiClient();
        _connection = connection ?? new CloudflareConnectionService(_api);
        _runtimeBundlePath = runtimeBundlePath ?? Path.Combine(AppContext.BaseDirectory, "CloudflareRuntime", "socyvia-runtime.js");
        _runtimeAssetsPath = runtimeAssetsPath ?? Path.Combine(AppContext.BaseDirectory, "WebExperimentFeed");
        _schemaPath = schemaPath ?? Path.Combine(AppContext.BaseDirectory, "CloudflareRuntime", "schema.sql");
    }

    public async Task<CloudflareEnvironmentSetupResult> PrepareAsync(
        CloudflareOAuthAccount account,
        string token,
        IProgress<CloudflareEnvironmentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var plan = new CloudflareResourcePlan();
        Report(progress, CloudflareEnvironmentStage.PreparingEnvironment, "Preparing cloud environment…");
        Report(progress, CloudflareEnvironmentStage.CheckingAccount, "Checking account…");
        if (!await _api.VerifyAccountAsync(account.Id, token, cancellationToken))
            return Failed(account, plan, "SOCYVIA cannot access the selected Cloudflare account.");

        // R2 remains optional and is never provisioned here. Preserve a compatible
        // existing bucket only when the OAuth grant can discover one.
        var discovered = await _api.DiscoverResearchResourcesAsync(account.Id, token, plan, cancellationToken);

        Report(progress, CloudflareEnvironmentStage.CheckingDatabase, "Checking database…");
        var database = await _api.EnsureD1DatabaseAsync(account.Id, token, plan.D1DatabaseName, cancellationToken);
        await EnsureDatabaseSchemaAsync(account.Id, database.DatabaseId, token, cancellationToken);
        await ValidateDatabaseContractAsync(account.Id, database.DatabaseId, token, cancellationToken);

        Report(progress, CloudflareEnvironmentStage.CheckingRuntime, "Checking runtime…");
        var workerExists = await _api.WorkerExistsAsync(account.Id, token, plan.WorkerName, cancellationToken);
        var runtimeCreated = false;
        string endpoint;
        if (!workerExists)
        {
            await DeployRuntimeAsync(account.Id, token, plan.WorkerName, database.DatabaseId, cancellationToken);
            runtimeCreated = true;
            endpoint = await _api.EnsureWorkerSubdomainAsync(account.Id, token, plan.WorkerName, cancellationToken);
        }
        else
        {
            endpoint = await _api.EnsureWorkerSubdomainAsync(account.Id, token, plan.WorkerName, cancellationToken);
        }
        var runtimeBindingReady = await _api.WorkerUsesD1BindingAsync(
            account.Id, token, plan.WorkerName, database.DatabaseId, cancellationToken);

        var configuration = new CloudflareProviderConfiguration
        {
            AccountId = account.Id,
            AccountDisplayName = account.Name,
            D1DatabaseId = database.DatabaseId,
            D1DatabaseName = database.DatabaseName,
            WorkerName = plan.WorkerName,
            WorkerEndpoint = endpoint,
            R2BucketName = discovered.R2BucketName,
            ConnectionMode = CloudflareConnectionMode.OAuth,
            ProviderStatus = CloudflareProviderConnectionState.Checking,
            LastVerifiedAtUtc = DateTime.UtcNow
        };

        if (!runtimeBindingReady)
        {
            configuration = configuration with { ProviderStatus = CloudflareProviderConnectionState.NeedsAttention };
            return new(false, configuration, database.Created, runtimeCreated,
                "The socyvia-runtime Worker does not have the required DB binding to the selected SOCYVIA research database. No existing Worker was overwritten.");
        }

        Report(progress, CloudflareEnvironmentStage.TestingConnection, "Testing connection…");
        var inspection = await InspectWithPropagationWaitAsync(configuration, token, cancellationToken);
        if (inspection.RuntimeCompatibility == CloudflareRuntimeCompatibilityState.DnsPropagationPending)
        {
            configuration = configuration with { ProviderStatus = CloudflareProviderConnectionState.Checking };
            return new(false, configuration, database.Created, runtimeCreated,
                "The SOCYVIA runtime is prepared and its secure endpoint is still propagating. Retry shortly; no resource or research data was changed.");
        }
        if (inspection.State != CloudflareProviderConnectionState.Ready && workerExists)
        {
            configuration = configuration with { ProviderStatus = CloudflareProviderConnectionState.NeedsAttention };
            return new(false, configuration, database.Created, false,
                inspection.RuntimeCompatibility switch
                {
                    CloudflareRuntimeCompatibilityState.WrongRuntimeIdentity => "The existing socyvia-runtime endpoint has the wrong runtime identity. No existing Worker was overwritten.",
                    CloudflareRuntimeCompatibilityState.MissingD1Binding => "The existing socyvia-runtime is missing its required D1 binding. No existing Worker was overwritten.",
                    CloudflareRuntimeCompatibilityState.MissingAssetsBinding => "The existing socyvia-runtime is missing its required ASSETS binding. No existing Worker was overwritten.",
                    CloudflareRuntimeCompatibilityState.TemporarilyUnreachable => "The existing SOCYVIA runtime is temporarily unreachable. Retry the connection check.",
                    _ => inspection.Message
                });
        }
        if (inspection.State != CloudflareProviderConnectionState.Ready)
        {
            configuration = configuration with { ProviderStatus = CloudflareProviderConnectionState.ConnectionFailed };
            return new(false, configuration, database.Created, runtimeCreated,
                "SOCYVIA prepared the cloud resources, but the runtime health check did not become ready.");
        }

        configuration = configuration with { ProviderStatus = CloudflareProviderConnectionState.Ready, LastVerifiedAtUtc = DateTime.UtcNow };
        Report(progress, CloudflareEnvironmentStage.Ready, "Ready");
        return new(true, configuration, database.Created, runtimeCreated,
            "Cloudflare, the research database, and the experiment runtime are ready. Media storage remains optional.");
    }

    public async Task<CloudflareMediaStorageSetupResult> PrepareMediaStorageAsync(
        CloudflareProviderConfiguration configuration,
        string token,
        IProgress<CloudflareEnvironmentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var plan = new CloudflareResourcePlan();
        if (configuration.ConnectionMode == CloudflareConnectionMode.None ||
            string.IsNullOrWhiteSpace(configuration.AccountId) ||
            string.IsNullOrWhiteSpace(configuration.D1DatabaseId) ||
            string.IsNullOrWhiteSpace(configuration.WorkerName) ||
            !Uri.TryCreate(configuration.WorkerEndpoint, UriKind.Absolute, out _))
            return new(false, configuration with { ProviderStatus = CloudflareProviderConnectionState.ConfigurationRequired }, false,
                "The existing Cloudflare account, research database, and experiment runtime must be ready before media storage is prepared.");

        Report(progress, CloudflareEnvironmentStage.CheckingRuntime, "Checking the existing experiment runtime…");
        var before = await _connection.InspectAsync(configuration, token, false, cancellationToken);
        var bindingReady = before.State == CloudflareProviderConnectionState.Ready &&
                           before.RuntimeCompatibility == CloudflareRuntimeCompatibilityState.Ready &&
                           await _api.WorkerUsesD1BindingAsync(
                               configuration.AccountId, token, configuration.WorkerName,
                               configuration.D1DatabaseId, cancellationToken);
        if (!bindingReady)
            return new(false, configuration with { ProviderStatus = CloudflareProviderConnectionState.NeedsAttention }, false,
                "The existing SOCYVIA runtime could not be verified safely. Media setup did not alter the Worker or research database.");

        Report(progress, CloudflareEnvironmentStage.PreparingEnvironment, "Preparing media storage…");
        var bucket = await _api.EnsureR2BucketAsync(
            configuration.AccountId, token, plan.R2BucketName, cancellationToken);

        // The existing Worker has already passed the canonical SOCYVIA health
        // and DB-binding checks. Update that same Worker only to add MEDIA;
        // never create a competing runtime or touch its routes/D1 data.
        await DeployRuntimeAsync(configuration.AccountId, token, configuration.WorkerName,
            configuration.D1DatabaseId, cancellationToken, bucket.BucketName);

        var prepared = configuration with
        {
            R2BucketName = bucket.BucketName,
            ProviderStatus = CloudflareProviderConnectionState.Checking
        };
        Report(progress, CloudflareEnvironmentStage.TestingConnection, "Testing media storage…");
        var inspection = await InspectWithPropagationWaitAsync(prepared, token, cancellationToken, true);
        if (inspection.State != CloudflareProviderConnectionState.Ready || !inspection.R2Available)
        {
            prepared = prepared with { ProviderStatus = CloudflareProviderConnectionState.NeedsAttention };
            return new(false, prepared, bucket.Created,
                "SOCYVIA prepared the media resource, but the existing runtime did not confirm its MEDIA binding. Retry safely; no research data was deleted.");
        }

        prepared = prepared with
        {
            ProviderStatus = CloudflareProviderConnectionState.Ready,
            LastVerifiedAtUtc = DateTime.UtcNow
        };
        Report(progress, CloudflareEnvironmentStage.Ready, "Media storage is ready.");
        return new(true, prepared, bucket.Created,
            bucket.Created ? "SOCYVIA media storage was created and verified." : "Existing SOCYVIA media storage was reused and verified.");
    }

    private static CloudflareEnvironmentSetupResult Failed(CloudflareOAuthAccount account, CloudflareResourcePlan plan, string message) =>
        new(false, new CloudflareProviderConfiguration
        {
            AccountId = account.Id,
            AccountDisplayName = account.Name,
            D1DatabaseName = plan.D1DatabaseName,
            WorkerName = plan.WorkerName,
            ConnectionMode = CloudflareConnectionMode.OAuth,
            ProviderStatus = CloudflareProviderConnectionState.ConnectionFailed
        }, false, false, message);

    private async Task<CloudflareResourceInspectionResult> InspectWithPropagationWaitAsync(
        CloudflareProviderConfiguration configuration,
        string token,
        CancellationToken cancellationToken,
        bool requireMediaStorage = false)
    {
        var delays = new[]
        {
            TimeSpan.Zero,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(12),
            TimeSpan.FromSeconds(15)
        };
        CloudflareResourceInspectionResult? inspection = null;
        foreach (var delay in delays)
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            inspection = await _connection.InspectAsync(configuration, token, requireMediaStorage, cancellationToken);
            if (inspection.State == CloudflareProviderConnectionState.Ready) return inspection;
            if (!inspection.AccountAvailable || !inspection.D1Available) return inspection;
        }
        return inspection!;
    }

    private async Task EnsureDatabaseSchemaAsync(string accountId, string databaseId, string token, CancellationToken cancellationToken)
    {
        if (!File.Exists(_schemaPath))
            throw new FileNotFoundException("The packaged SOCYVIA database schema is unavailable.", _schemaPath);
        var schema = await File.ReadAllTextAsync(_schemaPath, cancellationToken);
        var statements = SplitSqlStatements(schema);

        // Establish every table first. CREATE TABLE IF NOT EXISTS is safe for
        // both a fresh database and an existing researcher-owned database.
        foreach (var statement in statements.Where(IsCreateTable))
            await _api.ExecuteD1StatementAsync(accountId, databaseId, token, NormalizeForD1Query(statement), cancellationToken);

        // Reconcile the four additive runtime migrations by inspecting columns
        // before ALTER TABLE. This makes reconnect/retry genuinely idempotent.
        await EnsureColumnAsync(accountId, databaseId, token, "participants", "pre_session_token", "TEXT", cancellationToken);
        await EnsureColumnAsync(accountId, databaseId, token, "participants", "pre_questionnaire_completed_at", "TEXT", cancellationToken);
        await EnsureColumnAsync(accountId, databaseId, token, "sessions", "lifecycle_state", "TEXT NOT NULL DEFAULT 'SESSION_STARTED'", cancellationToken);
        await EnsureColumnAsync(accountId, databaseId, token, "sessions", "feed_end_at", "TEXT", cancellationToken);
        await EnsureColumnAsync(accountId, databaseId, token, "sessions", "post_questionnaire_completed_at", "TEXT", cancellationToken);
        await EnsureColumnAsync(accountId, databaseId, token, "deployments", "run_type", "TEXT NOT NULL DEFAULT 'Main'", cancellationToken);
        await EnsureColumnAsync(accountId, databaseId, token, "sessions", "run_type", "TEXT NOT NULL DEFAULT 'Main'", cancellationToken);

        foreach (var statement in statements.Where(statement => !IsCreateTable(statement)))
            await _api.ExecuteD1StatementAsync(accountId, databaseId, token, NormalizeForD1Query(statement), cancellationToken);
    }

    private async Task EnsureColumnAsync(
        string accountId,
        string databaseId,
        string token,
        string table,
        string column,
        string declaration,
        CancellationToken cancellationToken)
    {
        var rows = await _api.QueryD1RowsAsync(accountId, databaseId, token, $"PRAGMA table_info('{table}');", cancellationToken);
        if (rows.Any(row => row.TryGetValue("name", out var name) && string.Equals(name, column, StringComparison.OrdinalIgnoreCase))) return;
        await _api.ExecuteD1StatementAsync(accountId, databaseId, token,
            $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {declaration};", cancellationToken);
    }

    private static bool IsCreateTable(string statement) =>
        statement.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Cloudflare's D1 query endpoint accepts ordinary DDL with its trailing
    /// terminator, but trigger bodies contain an internal terminator and the
    /// final END terminator can be interpreted as the start of another query.
    /// A single-line `...; END` form is accepted by D1 and remains valid SQLite.
    /// </summary>
    internal static string NormalizeForD1Query(string statement)
    {
        if (!statement.StartsWith("CREATE TRIGGER", StringComparison.OrdinalIgnoreCase)) return statement;
        var singleLine = string.Join(" ", statement
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return singleLine.TrimEnd().TrimEnd(';');
    }

    private Task ValidateDatabaseContractAsync(string accountId, string databaseId, string token, CancellationToken cancellationToken) =>
        _api.ExecuteD1StatementAsync(accountId, databaseId, token, """
            SELECT d.run_type, s.run_type, s.lifecycle_state, s.feed_end_at,
                   p.pre_session_token, q.stage, c.configuration_hash
            FROM deployments AS d
            LEFT JOIN sessions AS s ON 1 = 0
            LEFT JOIN participants AS p ON 1 = 0
            LEFT JOIN deployment_questionnaires AS q ON 1 = 0
            LEFT JOIN deployment_content AS c ON 1 = 0
            LIMIT 0;
            """, cancellationToken);

    private async Task DeployRuntimeAsync(
        string accountId,
        string token,
        string workerName,
        string databaseId,
        CancellationToken cancellationToken,
        string? r2BucketName = null)
    {
        if (!File.Exists(_runtimeBundlePath))
            throw new FileNotFoundException("The packaged SOCYVIA runtime module is unavailable.", _runtimeBundlePath);
        if (!Directory.Exists(_runtimeAssetsPath))
            throw new DirectoryNotFoundException("The packaged SOCYVIA participant assets are unavailable.");

        var assets = Directory.EnumerateFiles(_runtimeAssetsPath, "*", SearchOption.AllDirectories)
            .Select(path => RuntimeAsset.Read(path, _runtimeAssetsPath))
            .ToArray();
        if (assets.Length == 0) throw new InvalidOperationException("The packaged SOCYVIA participant assets are empty.");
        var manifest = assets.ToDictionary(
            asset => asset.ManifestPath,
            asset => (object)new { hash = asset.Hash, size = asset.Bytes.LongLength },
            StringComparer.Ordinal);
        var sessionJson = await _api.BeginWorkerAssetsUploadAsync(accountId, token, workerName, manifest, cancellationToken);
        using var session = JsonDocument.Parse(sessionJson);
        var uploadToken = session.RootElement.GetProperty("jwt").GetString()
                          ?? throw new InvalidOperationException("Cloudflare did not return an asset upload token.");
        var buckets = session.RootElement.GetProperty("buckets").EnumerateArray().Select(bucket => bucket.Clone()).ToArray();
        var completionToken = uploadToken;
        var completionReceived = buckets.Length == 0;
        foreach (var bucket in buckets)
        {
            var requested = bucket.EnumerateArray().Select(value => value.GetString()).Where(value => value is not null).ToHashSet(StringComparer.Ordinal);
            // The asset manifest may map several public paths to the same
            // content hash. Cloudflare requests content-addressed blobs, so
            // upload each requested hash once rather than treating a shared
            // file body as a duplicate-key failure.
            var batch = assets
                .Where(asset => requested.Contains(asset.Hash))
                .GroupBy(asset => asset.Hash, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        var asset = group.First();
                        return (Convert.ToBase64String(asset.Bytes), asset.MimeType);
                    },
                    StringComparer.Ordinal);
            if (batch.Count != requested.Count) throw new InvalidOperationException("Cloudflare requested an unknown participant asset.");
            var nextToken = await _api.UploadWorkerAssetBatchAsync(accountId, uploadToken, batch, cancellationToken);
            if (!string.IsNullOrWhiteSpace(nextToken))
            {
                completionToken = nextToken;
                completionReceived = true;
            }
        }
        if (!completionReceived)
            throw new InvalidOperationException("Cloudflare did not complete the participant asset upload.");
        var module = await File.ReadAllBytesAsync(_runtimeBundlePath, cancellationToken);
        await _api.UploadWorkerModuleAsync(accountId, token, workerName, databaseId, module,
            completionToken, r2BucketName, cancellationToken);
    }

    internal static IReadOnlyList<string> SplitSqlStatements(string schema)
    {
        var statements = new List<string>();
        var current = new StringBuilder();
        var inTrigger = false;
        foreach (var rawLine in schema.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("--", StringComparison.Ordinal)) continue;
            if (line.StartsWith("CREATE TRIGGER", StringComparison.OrdinalIgnoreCase)) inTrigger = true;
            current.AppendLine(line);
            if ((!inTrigger && line.EndsWith(';')) || (inTrigger && line.Equals("END;", StringComparison.OrdinalIgnoreCase)))
            {
                statements.Add(current.ToString().Trim());
                current.Clear();
                inTrigger = false;
            }
        }
        if (current.Length > 0) statements.Add(current.ToString().Trim());
        return statements;
    }

    private static void Report(IProgress<CloudflareEnvironmentProgress>? progress, CloudflareEnvironmentStage stage, string message) =>
        progress?.Report(new(stage, message));

    private sealed record RuntimeAsset(string ManifestPath, string Hash, byte[] Bytes, string MimeType)
    {
        public static RuntimeAsset Read(string path, string root)
        {
            var bytes = File.ReadAllBytes(path);
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var extension = Path.GetExtension(relative).TrimStart('.').ToLowerInvariant();
            var hashInput = Encoding.UTF8.GetBytes(Convert.ToBase64String(bytes) + extension);
            var hash = Convert.ToHexString(SHA256.HashData(hashInput)).ToLowerInvariant()[..32];
            return new('/' + relative, hash, bytes, Mime(extension));
        }

        private static string Mime(string extension) => extension switch
        {
            "html" => "text/html",
            "css" => "text/css",
            "js" => "application/javascript",
            "json" => "application/json",
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "svg" => "image/svg+xml",
            "woff2" => "font/woff2",
            "ttf" => "font/ttf",
            "mp3" => "audio/mpeg",
            "mp4" => "video/mp4",
            _ => "application/octet-stream"
        };
    }
}
