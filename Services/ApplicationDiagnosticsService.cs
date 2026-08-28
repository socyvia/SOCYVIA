using System;
using System.IO;
using System.Text;

namespace SOCYVIA.Services;

public static class ApplicationDiagnosticsService
{
    private static readonly object Gate = new();

    public static string LogFolder => Path.Combine(StorageService.RootPath, "logs");

    public static void LogException(Exception exception, string context)
    {
        try
        {
            Directory.CreateDirectory(LogFolder);
            var path = Path.Combine(
                LogFolder,
                $"socyvia-{DateTime.UtcNow:yyyy-MM-dd}.log");
            var entry = new StringBuilder()
                .AppendLine("============================================================")
                .AppendLine($"UTC: {DateTime.UtcNow:O}")
                .AppendLine($"Context: {context}")
                .AppendLine($"OS: {Environment.OSVersion}")
                .AppendLine($"Runtime: {Environment.Version}")
                .AppendLine(exception.ToString())
                .ToString();
            lock (Gate)
            {
                File.AppendAllText(path, entry, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnostics must never become a second application failure.
        }
    }

    public static void LogInformation(string context, string message)
    {
        try
        {
            Directory.CreateDirectory(LogFolder);
            var path = Path.Combine(
                LogFolder,
                $"socyvia-{DateTime.UtcNow:yyyy-MM-dd}.log");
            var entry = new StringBuilder()
                .AppendLine("============================================================")
                .AppendLine($"UTC: {DateTime.UtcNow:O}")
                .AppendLine($"Context: {context}")
                .AppendLine(message)
                .ToString();
            lock (Gate)
            {
                File.AppendAllText(path, entry, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnostics must never become a second application failure.
        }
    }
}
