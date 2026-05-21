using System;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid;

namespace OpenSage.Graphics;

/// <summary>
/// On-demand frame capture service for use in headless test runs.
///
/// Captures are triggered by explicit calls — typically from replay action event handlers
/// or at specific replay frame milestones — rather than periodically. Each capture is saved
/// as a PNG and a <c>[CAPTURE]</c> line is written to stdout so that monitoring agents
/// can locate, inspect, and delete the file.
///
/// Log line format (machine-parseable):
/// <code>
///   [CAPTURE] /path/to/capture_0001.png | &lt;validation hint&gt;
/// </code>
///
/// Agent workflow:
/// <list type="number">
///   <item>Monitor stdout for lines starting with <c>[CAPTURE]</c>.</item>
///   <item>Split on <c> | </c> to get the file path and the validation hint.</item>
///   <item>Open the PNG and evaluate whether the scene matches the hint.</item>
///   <item>Delete the file after inspection to avoid disk accumulation.</item>
/// </list>
///
/// Example (in a headless game loop):
/// <code>
///   var capture = new FrameCaptureService();
///
///   monitor.ActionCompleted += (_, e) =>
///   {
///       if (e.ActionType == "DozerBuild")
///           capture.ScheduleCapture("Dozer has finished building. Expect a completed structure visible.");
///   };
///
///   while (game.IsRunning)
///   {
///       game.Update(...);
///       game.Render();
///       capture.FlushPending(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target);
///   }
/// </code>
/// </summary>
public sealed class FrameCaptureService
{
    /// <summary>
    /// Directory where capture PNGs are written.
    /// Defaults to <c>$TMPDIR/opensage_captures</c>.
    /// </summary>
    public static readonly string CaptureDirectory =
        Path.Combine(Path.GetTempPath(), "opensage_captures");

    private int _captureCount;
    private string _pendingHint;

    public FrameCaptureService()
    {
        Directory.CreateDirectory(CaptureDirectory);
    }

    /// <summary>
    /// Schedules a capture to be taken on the next call to <see cref="FlushPending"/>.
    /// Safe to call from action event handlers (which fire during game update, before render).
    /// If multiple hints are scheduled before the next flush, only the last one is used.
    /// </summary>
    public void ScheduleCapture(string hint)
    {
        Volatile.Write(ref _pendingHint, hint);
    }

    /// <summary>
    /// If a capture was scheduled via <see cref="ScheduleCapture"/>, reads back the current
    /// framebuffer, saves it, and writes a <c>[CAPTURE]</c> log line. Call this immediately
    /// after each <c>game.Render()</c> in the headless game loop.
    /// </summary>
    public void FlushPending(GraphicsDevice graphicsDevice, Texture texture)
    {
        var hint = Interlocked.Exchange(ref _pendingHint, null);
        if (hint == null)
        {
            return;
        }

        Capture(graphicsDevice, texture, hint);
    }

    /// <summary>
    /// Immediately captures the current framebuffer, saves it, and logs a <c>[CAPTURE]</c> line.
    /// Use this for captures at a predetermined replay frame number.
    /// </summary>
    public void Capture(GraphicsDevice graphicsDevice, Texture texture, string hint)
    {
        var count = Interlocked.Increment(ref _captureCount);
        var path = Path.Combine(CaptureDirectory, $"capture_{count:D4}.png");

        var frame = FrameCapturer.Capture(graphicsDevice, texture);
        using var image = ToImage(frame);
        image.SaveAsPng(path);

        Console.WriteLine($"[CAPTURE] {path} | {hint}");
    }

    // -------------------------------------------------------------------------

    private static Image<Rgba32> ToImage(CapturedFrame frame)
    {
        var image = new Image<Rgba32>(frame.Width, frame.Height);
        var isBgra = frame.Format is PixelFormat.B8_G8_R8_A8_UNorm
                                  or PixelFormat.B8_G8_R8_A8_UNorm_SRgb;
        var pixels = frame.PixelData;
        var stride = frame.Width * 4;

        for (var y = 0; y < frame.Height; y++)
        {
            var row = image.Frames[0].PixelBuffer.DangerousGetRowSpan(y);
            for (var x = 0; x < frame.Width; x++)
            {
                var i = y * stride + x * 4;
                row[x] = isBgra
                    ? new Rgba32(r: pixels[i + 2], g: pixels[i + 1], b: pixels[i], a: pixels[i + 3])
                    : new Rgba32(r: pixels[i], g: pixels[i + 1], b: pixels[i + 2], a: pixels[i + 3]);
            }
        }

        return image;
    }
}
