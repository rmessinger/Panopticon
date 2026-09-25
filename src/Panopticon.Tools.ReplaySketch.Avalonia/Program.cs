using System;
using Avalonia;

namespace Panopticon.Tools.ReplaySketch.Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // --screenshot <path> : render the validation-ground editor scene headlessly to a PNG and
        // exit, without opening a window. Runs on Linux (no display required) so CI / automated
        // capture works there, unlike the MAUI front-end which needs maccatalyst/Windows.
        if (args.Length > 0 && args[0].Equals("--screenshot", StringComparison.OrdinalIgnoreCase))
        {
            var path = args.Length > 1 ? args[1] : "replaysketch.png";
            ScreenshotCapture.Run(path);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
