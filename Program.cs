using Avalonia;
using System;
using System.Threading.Tasks;
using SOCYVIA.Services;

namespace SOCYVIA;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        using var activation = DesktopProtocolActivationService.StartOrForward(args);
        if (!activation.IsPrimary) return;
        CloudflareOAuthCallbackInbox.Capture(args);
        activation.ActivationReceived += (_, _) => DesktopActivationBroker.RequestActivation();
        try { WindowsSocyviaProtocolRegistration.EnsureCurrentUserRegistration(); }
        catch (Exception exception) { ApplicationDiagnosticsService.LogException(exception, "Windows protocol registration"); }
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
                ApplicationDiagnosticsService.LogException(exception, "AppDomain.UnhandledException");
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            ApplicationDiagnosticsService.LogException(eventArgs.Exception, "TaskScheduler.UnobservedTaskException");
            eventArgs.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "Program.Main");
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
