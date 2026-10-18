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

        // --export-results <run.json> --frame <n> --out <dir> : headlessly export one bundle
        // frame (frame.png + chart.png + self-contained report.html) without a window or GPU.
        // WI-A5-20; documented in docs/screenshots.md.
        if (args.Length > 0 && args[0].Equals("--export-results", StringComparison.OrdinalIgnoreCase))
        {
            string bundle = string.Empty;
            uint frame = 0;
            string output = ".";
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i].Equals("--frame", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    frame = uint.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (args[i].Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    output = args[++i];
                }
                else
                {
                    bundle = args[i];
                }
            }

            var result = Panopticon.Tools.ReplaySketch.Services.ResultsExportService.ExportAsync(
                bundle, frame, output).GetAwaiter().GetResult();
            Console.WriteLine(
                $"Exported frame {result.Frame} to {result.OutputDirectory}: " +
                $"{result.FrameImagePath}, {result.ChartImagePath}, {result.ReportHtmlPath}");
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
