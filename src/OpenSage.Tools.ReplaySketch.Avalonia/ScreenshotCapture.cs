using System;
using System.IO;
using System.Runtime.InteropServices;
using OpenSage.Data;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Profiles;
using OpenSage.Tools.ReplaySketch.Services;
using SkiaSharp;

namespace OpenSage.Tools.ReplaySketch.Avalonia;

/// <summary>
/// Headless render of the validation-ground editor scene to a PNG, with no window and no display.
/// Reuses the Core renderer verbatim, so the captured image is exactly what the interactive editor
/// draws. Invoked by <c>Program</c>'s <c>--screenshot &lt;path&gt;</c> mode; works on Linux so CI /
/// automated capture succeeds there (unlike the MAUI front-end).
/// </summary>
internal static class ScreenshotCapture
{
    // Square canvas so the square map preview fills it entirely (no grey canvas-clear strips down the
    // sides). The map (2400x2600) is near-square, so the slight stretch is negligible.
    private const int CanvasWidth = 1100;
    private const int CanvasHeight = 1100;
    private const string DefaultMapPath = "maps/alpine assault/alpine assault.map";

    public static void Run(string path)
    {
        try
        {
            var install = ResolveInstallation();
            if (install == null)
            {
                WriteError(path, "Could not locate a C&C Generals installation. Set CNC_GENERALS_PATH.");
                return;
            }

            var map = MapMetadataService.Load(install, DefaultMapPath);

            var model = new ValidationGroundEditorModel();
            model.LoadScenario(ValidationGroundScenario.Standard());
            model.AttachMap(map);

            using var previewBitmap = BuildPreviewBitmap(map);
            var png = RenderToPng(model, previewBitmap);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllBytes(path, png);
            Console.WriteLine($"[ScreenshotCapture] Wrote {path} ({png.Length} bytes).");
        }
        catch (Exception ex)
        {
            WriteError(path, ex.ToString());
        }
    }

    private static GameInstallation? ResolveInstallation()
    {
        var envPath = Environment.GetEnvironmentVariable("CNC_GENERALS_PATH");
        var svc = new GeneralsInstallationService();
        if (!string.IsNullOrEmpty(envPath))
        {
            svc.SetPath(envPath);
        }
        return svc.TryGetInstallation(out var install) ? install : null;
    }

    private static SKBitmap? BuildPreviewBitmap(MapMetadataService map)
    {
        if (map.PreviewImagePixels == null || map.PreviewImageWidth <= 0 || map.PreviewImageHeight <= 0)
        {
            return null;
        }

        var info = new SKImageInfo(map.PreviewImageWidth, map.PreviewImageHeight, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var bitmap = new SKBitmap(info);
        Marshal.Copy(map.PreviewImagePixels, 0, bitmap.GetPixels(), map.PreviewImagePixels.Length);
        return bitmap;
    }

    private static byte[] RenderToPng(ValidationGroundEditorModel model, SKBitmap? previewBitmap)
    {
        using var surface = SKSurface.Create(new SKImageInfo(CanvasWidth, CanvasHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0x2A, 0x2A, 0x2A));

        var previewSize = (float)Math.Min(CanvasWidth, CanvasHeight);
        var offsetX = (CanvasWidth - previewSize) / 2f;
        var offsetY = (CanvasHeight - previewSize) / 2f;

        ValidationGroundMapRenderer.Render(canvas, model.Map!, previewBitmap, model.Plan!, model.Selected, offsetX, offsetY, previewSize);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void WriteError(string path, string message)
    {
        Console.Error.WriteLine($"[ScreenshotCapture] {message}");
        try
        {
            File.WriteAllText(path + ".error", message);
        }
        catch
        {
            // Best-effort diagnostic; ignore failures writing the error file itself.
        }
    }
}
