using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SOCYVIA.Services;

/// <summary>Forwards a protocol activation to the already-running same-user SOCYVIA process.</summary>
public sealed class DesktopProtocolActivationService : IDisposable
{
    private const string ActivationMessage = "activate";
    private readonly Mutex? _mutex;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _pipeName;
    private Task? _listener;

    private DesktopProtocolActivationService(Mutex? mutex, string pipeName, bool isPrimary)
    {
        _mutex = mutex;
        _pipeName = pipeName;
        IsPrimary = isPrimary;
    }

    public bool IsPrimary { get; }
    public event EventHandler? ActivationReceived;

    public static DesktopProtocolActivationService StartOrForward(IReadOnlyList<string> arguments)
    {
        var userKey = CurrentUserKey();
        var mutexName = OperatingSystem.IsWindows()
            ? $"Local\\SOCYVIA.Desktop.{userKey}"
            : $"SOCYVIA.Desktop.{userKey}";
        var pipeName = $"SOCYVIA.Desktop.Activation.{userKey}";
        var mutex = new Mutex(true, mutexName, out var createdNew);
        var service = new DesktopProtocolActivationService(mutex, pipeName, createdNew);
        if (createdNew)
        {
            service._listener = Task.Run(service.ListenAsync);
            return service;
        }

        service.ForwardToPrimaryAsync(FindCallback(arguments)).GetAwaiter().GetResult();
        return service;
    }

    private async Task ListenAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_shutdown.Token);
                using var reader = new StreamReader(server, new UTF8Encoding(false), false, 1024, leaveOpen: true);
                var payload = await reader.ReadLineAsync(_shutdown.Token);
                if (payload is { Length: > 0 and <= 8192 } &&
                    Uri.TryCreate(payload, UriKind.Absolute, out var callback) &&
                    CloudflareDesktopOAuth.IsExpectedApplicationHandoff(callback))
                    CloudflareOAuthCallbackInbox.Capture(callback);
                ActivationReceived?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                if (_shutdown.IsCancellationRequested) return;
            }
        }
    }

    private async Task ForwardToPrimaryAsync(Uri? callback)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                await using var client = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await client.ConnectAsync(timeout.Token);
                await using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true)
                {
                    AutoFlush = true
                };
                await writer.WriteLineAsync(callback?.AbsoluteUri ?? ActivationMessage);
                return;
            }
            catch (OperationCanceledException)
            {
                if (attempt == 3) return;
            }
            catch (IOException)
            {
                if (attempt == 3) return;
            }
            await Task.Delay(150);
        }
    }

    private static Uri? FindCallback(IEnumerable<string> arguments) => arguments
        .Select(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null)
        .FirstOrDefault(uri => uri is not null && CloudflareDesktopOAuth.IsExpectedApplicationHandoff(uri));

    private static string CurrentUserKey()
    {
        string identity;
        if (OperatingSystem.IsWindows())
            identity = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        else
            identity = Environment.UserName;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        try { _listener?.Wait(TimeSpan.FromSeconds(1)); }
        catch (AggregateException exception) when (exception.InnerExceptions.All(inner => inner is OperationCanceledException)) { }
        if (IsPrimary)
        {
            try { _mutex?.ReleaseMutex(); }
            catch (ApplicationException) { }
        }
        _mutex?.Dispose();
        _shutdown.Dispose();
    }
}

public static class DesktopActivationBroker
{
    public static event EventHandler? ActivationRequested;
    public static void RequestActivation() => ActivationRequested?.Invoke(null, EventArgs.Empty);
}

/// <summary>Registers the development/release executable as the current user's Windows socyvia: handler.</summary>
public static class WindowsSocyviaProtocolRegistration
{
    private const string ProtocolKey = @"Software\Classes\socyvia";

    public static bool EnsureCurrentUserRegistration()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return false;
        var entryAssembly = Assembly.GetEntryAssembly()?.Location;
        var command = Path.GetFileName(executable).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) &&
                      !string.IsNullOrWhiteSpace(entryAssembly)
            ? $"\"{executable}\" \"{entryAssembly}\" \"%1\""
            : $"\"{executable}\" \"%1\"";

        using var protocol = Registry.CurrentUser.CreateSubKey(ProtocolKey);
        if (protocol is null) return false;
        protocol.SetValue(null, "URL:SOCYVIA Protocol", RegistryValueKind.String);
        protocol.SetValue("URL Protocol", string.Empty, RegistryValueKind.String);
        using var icon = protocol.CreateSubKey("DefaultIcon");
        icon?.SetValue(null, $"\"{executable}\",0", RegistryValueKind.String);
        using var openCommand = protocol.CreateSubKey(@"shell\open\command");
        openCommand?.SetValue(null, command, RegistryValueKind.String);
        return openCommand is not null;
    }

    public static string? ReadCurrentUserCommand()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var command = Registry.CurrentUser.OpenSubKey(ProtocolKey + @"\shell\open\command", writable: false);
        return command?.GetValue(null) as string;
    }
}
