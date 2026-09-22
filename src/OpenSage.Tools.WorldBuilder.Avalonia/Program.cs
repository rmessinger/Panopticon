using System;
using System.Linq;
using Avalonia;
using OpenSage.Data;
using OpenSage.Data.Map;
using OpenSage.Graphics;
using OpenSage.IO;
using OpenSage.Mods.Generals;
using OpenSage.Tools.WorldBuilder.Core;

namespace OpenSage.Tools.WorldBuilder.Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // --screenshot <path>: create a default map, render one frame headlessly
        // (no window, no display), write a PNG, and exit. Used for automated
        // validation on Linux.
        if (args.Length > 0 && args[0].Equals("--screenshot", StringComparison.OrdinalIgnoreCase))
        {
            var path = args.Length > 1 ? args[1] : "worldbuilder.png";
            var mapPath = args.Length > 2 ? args[2] : null;
            ScreenshotHeadless(path, mapPath);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

    private static void ScreenshotHeadless(string path, string? mapPath)
    {
        var installation = GameInstallation.FindAll([GeneralsDefinition.Instance]).FirstOrDefault();
        if (installation is null)
        {
            Console.Error.WriteLine(
                "No Generals installation found; set CNC_GENERALS_PATH for headless captures.");
            Environment.Exit(1);
            return;
        }

        using var renderer = new GameViewportRenderer
        {
            Installation = installation,
        };

        var map = mapPath is null
            ? NewMapFactory.Create(new NewMapParameters
            {
                PlayableWidth = 100,
                PlayableHeight = 100,
                InitialHeight = 50,
                BorderWidth = 6,
            })
            : EditorMap.Open(
                MapFile.FromStream(installation.CreateFileSystem().GetFile(mapPath)!.Open()),
                mapPath);
        renderer.LoadMap(map);
        renderer.Initialize(1280, 800);

        // Warm-up frames absorb first-execution engine-global uploads and let
        // terrain visual resources settle before capture.
        for (var i = 0; i < 3; i++)
        {
            renderer.RenderFrame();
        }

        FrameCapturer.CaptureToPng(
            renderer.GraphicsDevice!,
            renderer.FrameTexture!,
            path);
        Console.WriteLine($"Wrote {path}");
    }
}
