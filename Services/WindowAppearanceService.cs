using System;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Media;
using SOCYVIA.Views;

namespace SOCYVIA.Services;

public static class WindowAppearanceService
{
    private static readonly Uri AppIconUri =
        new(
            "avares://SOCYVIA/Assets/Branding/socyvia-mark.ico");


    public static void ApplyAppIcon(
        Window window)
    {
        try
        {
            using var stream =
                AssetLoader.Open(
                    AppIconUri);


            window.Icon =
                new WindowIcon(
                    stream);
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"SOCYVIA window icon error: {exception.Message}");
        }
    }


    public static void ConfigureCompactDialog(
        Window window,
        double width = 390,
        double height = 190)
    {
        window.Background =
            new SolidColorBrush(Color.Parse("#EEF3F8"));

        window.Width =
            width;


        window.Height =
            height;


        window.MinWidth =
            width;


        window.MinHeight =
            height;


        window.MaxWidth =
            width;


        window.MaxHeight =
            height;


        window.CanResize =
            false;


        window.ShowInTaskbar =
            false;


        window.WindowStartupLocation =
            WindowStartupLocation.CenterOwner;


        ApplyAppIcon(
            window);
    }


    public static void ApplySocyviaDialogChrome(Window window)
    {
        if (window.Content is not Control dialogContent ||
            dialogContent is SocyviaWindowChrome)
        {
            return;
        }

        var chrome = new SocyviaWindowChrome();
        var chromeAvailable = chrome.Attach(
            window,
            showMinimize: false,
            showMaximize: false);
        if (!chromeAvailable) return;

        window.Content = null;
        var shell = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*")
        };
        shell.Children.Add(chrome);
        Grid.SetRow(dialogContent, 1);
        shell.Children.Add(dialogContent);
        window.Content = shell;
    }
}
