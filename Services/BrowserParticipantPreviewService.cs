using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

/// <summary>
/// Hosts the current study in a loopback-only, non-persistent participant
/// presentation. This is the researcher preview surface: it deliberately
/// reuses the web participant renderer without creating research evidence.
/// </summary>
public static class BrowserParticipantPreviewService
{
    private static readonly List<LoopbackPreviewHost> Hosts = [];

    public static async Task<Uri> OpenAsync(
        Study study,
        StudyGroup group,
        ExperimentalCondition condition,
        Action<Uri>? browserLauncher = null)
    {
        BrowserParticipantPreviewContext context;
        try
        {
            context = await CreateContextAsync(study, group, condition);
        }
        catch (BrowserParticipantPreviewException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new BrowserParticipantPreviewException(
                BrowserParticipantPreviewFailure.PreviewPreparationFailed,
                "The selected study could not be prepared for preview.",
                exception);
        }

        return await OpenContextAsync(context, browserLauncher);
    }

    /// <summary>
    /// Opens an already resolved, non-persistent preview context. Keeping this
    /// boundary explicit makes the browser-launch path independently verifiable
    /// without creating a participant or session.
    /// </summary>
    public static async Task<Uri> OpenContextAsync(
        BrowserParticipantPreviewContext context,
        Action<Uri>? browserLauncher = null)
    {
        var host = await StartHostAsync(context);
        var uri = host.PreviewUri;
        try
        {
            (browserLauncher ?? LaunchDefaultBrowser)(uri);
        }
        catch (Exception exception)
        {
            host.Dispose();
            throw new BrowserParticipantPreviewException(
                BrowserParticipantPreviewFailure.BrowserLaunchUnavailable,
                "The default browser could not be opened for the SOCYVIA preview.",
                exception);
        }

        lock (Hosts)
        {
            Hosts.RemoveAll(item => !item.IsRunning);
            Hosts.Add(host);
        }
        return uri;
    }

    /// <summary>
    /// Creates a loopback-only host without opening a browser. This is used by
    /// deterministic integration checks and contains no persistence path.
    /// </summary>
    public static Task<LoopbackPreviewHost> StartHostAsync(BrowserParticipantPreviewContext context) =>
        LoopbackPreviewHost.StartAsync(context);

    private static void LaunchDefaultBrowser(Uri uri)
    {
        var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        if (process is null)
        {
            throw new InvalidOperationException("The operating system did not start a default browser process.");
        }
    }

    internal static async Task<BrowserParticipantPreviewContext> CreateContextAsync(
        Study study,
        StudyGroup group,
        ExperimentalCondition condition)
    {
        var presentation = await ParticipantPreviewService.CreateAsync(study, group, condition);
        var ticket = Guid.NewGuid().ToString("N");
        var assignments = await QuestionnaireRepository.GetAssignmentsAsync(study.Id);
        // A study can retain multiple immutable/localized assignment records
        // for one stage. The web renderer needs one resolved definition per
        // stage, selected deterministically; never let a duplicate PRE/POST
        // record prevent a safe current-study preview from opening.
        var questionnaires = assignments
            .Where(assignment => assignment.Version is not null && assignment.Questionnaire is not null)
            .GroupBy(assignment => assignment.Placement == QuestionnairePlacements.PreExperiment ? "PRE" : "POST", StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(assignment => assignment.Version!.VersionNumber)
                .ThenByDescending(assignment => assignment.SortOrder)
                .ThenBy(assignment => assignment.Version!.Id, StringComparer.Ordinal)
                .First())
            .Select(assignment => ToQuestionnaire(assignment, ticket))
            .ToDictionary(item => item.Stage, StringComparer.OrdinalIgnoreCase);
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        var content = presentation.Posts.Select((post, index) =>
        {
            var source = post.Source;
            BrowserPreviewMedia? media = null;
            var localMediaPath = source.MediaPath ?? source.ThumbnailPath;
            if (!string.IsNullOrWhiteSpace(localMediaPath))
            {
                if (!File.Exists(localMediaPath))
                {
                    throw new BrowserParticipantPreviewException(
                        BrowserParticipantPreviewFailure.LocalMediaUnavailable,
                        $"A local preview media file is unavailable: {Path.GetFileName(localMediaPath)}");
                }
                var assetId = $"asset-{index:D3}";
                paths[assetId] = localMediaPath;
                media = new BrowserPreviewMedia(
                    $"/experimentfeed/api/preview/{ticket}/media/{assetId}",
                    MediaKind(source.ContentType, localMediaPath),
                    source.Title);
            }

            return new BrowserPreviewContentItem(
                source.ContentItemId ?? source.StimulusId,
                source.Title,
                source.BodyText,
                new BrowserPreviewInteractions(),
                media);
        }).ToArray();

        var hasPre = questionnaires.ContainsKey("PRE");
        var hasPost = questionnaires.ContainsKey("POST");
        var language = LocalizationService.IsArabic ? "ar" : "en";
        var interfaceLanguages = new[] { language };
        return new BrowserParticipantPreviewContext(
            ticket,
            new BrowserPreviewEntry(
                $"preview-{ticket}",
                language,
                interfaceLanguages,
                new BrowserPreviewStudy(
                    Both(study.Title),
                    Both(study.Description),
                    Both(study.PopulationDescription ?? study.ResearchQuestion),
                    Both(study.InclusionCriteria),
                    Both(study.ConsentText),
                    study.ExpectedSessionDurationMinutes),
                new BrowserPreviewFlow(hasPre, hasPost)),
            questionnaires,
            content,
            paths,
            DateTime.UtcNow);
    }

    private static BrowserPreviewQuestionnaire ToQuestionnaire(
        QuestionnaireAssignment assignment,
        string ticket)
    {
        var version = assignment.Version!;
        var questionnaire = assignment.Questionnaire!;
        var stage = assignment.Placement == QuestionnairePlacements.PreExperiment ? "PRE" : "POST";
        return new BrowserPreviewQuestionnaire(
            questionnaire.Id,
            version.Id,
            stage,
            Both(questionnaire.Title),
            Both(questionnaire.Description),
            Both(null),
            assignment.IsRequired,
            version.Questions.OrderBy(item => item.SortOrder).Select(question =>
                new BrowserPreviewQuestionnaireItem(
                    question.Id,
                    ToRuntimeType(question.QuestionType),
                    Both(question.QuestionText),
                    Both(null),
                    question.IsRequired,
                    question.SortOrder,
                    QuestionConfiguration(question))).ToArray());
    }

    private static object QuestionConfiguration(Question question)
    {
        try
        {
            using var document = JsonDocument.Parse(question.ConfigurationJson ?? "{}");
            var values = JsonSerializer.Deserialize<Dictionary<string, object?>>(document.RootElement.GetRawText())
                         ?? new Dictionary<string, object?>();
            if (question.Options.Count > 0)
            {
                values["options"] = question.Options.OrderBy(option => option.SortOrder)
                    .Select(option => new { value = option.ValueCode, label = option.DisplayLabel }).ToArray();
            }
            return values;
        }
        catch (JsonException)
        {
            return new { options = question.Options.OrderBy(option => option.SortOrder)
                .Select(option => new { value = option.ValueCode, label = option.DisplayLabel }).ToArray() };
        }
    }

    private static LocalizedPreviewText Both(string? value) => new(value ?? string.Empty, value ?? string.Empty);

    private static string ToRuntimeType(string type) => type == QuestionnaireQuestionTypes.Numeric ? "NUMBER" : type;

    private static string MediaKind(string contentType, string path)
    {
        if (contentType.Contains("video", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".mov", StringComparison.OrdinalIgnoreCase)) return "video";
        if (contentType.Contains("audio", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)) return "audio";
        return "image";
    }
}

public enum BrowserParticipantPreviewFailure
{
    PreviewPreparationFailed,
    LocalMediaUnavailable,
    AssetsUnavailable,
    LocalHostUnavailable,
    BrowserLaunchUnavailable
}

public sealed class BrowserParticipantPreviewException : Exception
{
    public BrowserParticipantPreviewException(
        BrowserParticipantPreviewFailure failure,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public BrowserParticipantPreviewFailure Failure { get; }
}

public sealed class LoopbackPreviewHost : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpListener _listener = new();
    private readonly BrowserParticipantPreviewContext _context;
    private readonly CancellationTokenSource _stop = new();
    private readonly string _webRoot;

    private LoopbackPreviewHost(int port, BrowserParticipantPreviewContext context, string webRoot)
    {
        _context = context;
        _webRoot = webRoot;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        PreviewUri = new Uri($"http://127.0.0.1:{port}/experimentfeed/preview/{context.Ticket}");
    }

    private bool _disposed;

    public Uri PreviewUri { get; }
    public bool IsRunning => !_disposed && _listener.IsListening;

    public static Task<LoopbackPreviewHost> StartAsync(BrowserParticipantPreviewContext context)
    {
        var root = ResolveWebRoot();
        if (root is null)
        {
            throw new BrowserParticipantPreviewException(
                BrowserParticipantPreviewFailure.AssetsUnavailable,
                "The SOCYVIA browser preview files are unavailable.");
        }

        Exception? lastFailure = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var host = new LoopbackPreviewHost(ReserveLoopbackPort(), context, root);
            try
            {
                host._listener.Start();
                _ = host.ListenAsync();
                return Task.FromResult(host);
            }
            catch (HttpListenerException exception)
            {
                lastFailure = exception;
                host.Dispose();
            }
            catch (InvalidOperationException exception)
            {
                lastFailure = exception;
                host.Dispose();
            }
        }

        throw new BrowserParticipantPreviewException(
            BrowserParticipantPreviewFailure.LocalHostUnavailable,
            "The local SOCYVIA preview server could not be started.",
            lastFailure);
    }

    private static int ReserveLoopbackPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private static string? ResolveWebRoot()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "WebExperimentFeed"),
            Path.Combine(Directory.GetCurrentDirectory(), "WebExperimentFeed")
        };

        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var level = 0; level < 6 && directory is not null; level++, directory = directory.Parent)
        {
            candidates.Add(Path.Combine(directory.FullName, "WebExperimentFeed"));
        }

        return candidates.FirstOrDefault(path =>
            Directory.Exists(path) && File.Exists(Path.Combine(path, "index.html")));
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext? request = null;
            try { request = await _listener.GetContextAsync(); }
            catch (HttpListenerException) when (_stop.IsCancellationRequested) { return; }
            catch (ObjectDisposedException) { return; }
            if (request is not null) _ = HandleAsync(request);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            if (context.Request.HttpMethod != "GET")
            {
                context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                return;
            }

            var prefix = $"/experimentfeed/api/preview/{_context.Ticket}";
            if (path == $"{prefix}/entry") await JsonAsync(context, _context.Entry);
            else if (path == $"{prefix}/content") await JsonAsync(context, new { items = _context.Content });
            else if (path.StartsWith($"{prefix}/questionnaires/", StringComparison.Ordinal))
            {
                var stage = path[(prefix.Length + "/questionnaires/".Length)..];
                if (_context.Questionnaires.TryGetValue(stage, out var questionnaire)) await JsonAsync(context, questionnaire);
                else context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            }
            else if (path.StartsWith($"{prefix}/media/", StringComparison.Ordinal))
            {
                var asset = path[(prefix.Length + "/media/".Length)..];
                if (_context.LocalMediaPaths.TryGetValue(asset, out var file)) await MediaAsync(context, file);
                else context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            }
            else await StaticAsync(context, path);
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Browser participant preview");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        }
        finally { context.Response.Close(); }
    }

    private async Task StaticAsync(HttpListenerContext context, string path)
    {
        var relative = path.StartsWith("/experimentfeed/", StringComparison.Ordinal)
            ? path["/experimentfeed/".Length..] : string.Empty;
        if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith("preview/", StringComparison.Ordinal)) relative = "index.html";
        relative = relative.Replace('/', Path.DirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(_webRoot, relative));
        if (!candidate.StartsWith(Path.GetFullPath(_webRoot), StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return;
        }
        await MediaAsync(context, candidate);
    }

    private static async Task JsonAsync(HttpListenerContext context, object value)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.OutputStream, value, Json);
    }

    private static async Task MediaAsync(HttpListenerContext context, string file)
    {
        context.Response.ContentType = ContentType(file);
        context.Response.Headers["Cache-Control"] = "no-store";
        await using var stream = File.OpenRead(file);
        context.Response.ContentLength64 = stream.Length;
        await stream.CopyToAsync(context.Response.OutputStream);
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8", ".js" => "text/javascript; charset=utf-8", ".css" => "text/css; charset=utf-8",
        ".svg" => "image/svg+xml", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".webp" => "image/webp",
        ".mp4" => "video/mp4", ".webm" => "video/webm", ".mov" => "video/quicktime", ".mp3" => "audio/mpeg", ".wav" => "audio/wav", ".m4a" => "audio/mp4", ".ogg" => "audio/ogg",
        ".ttf" => "font/ttf", _ => "application/octet-stream"
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        _listener.Close();
        _stop.Dispose();
    }
}
