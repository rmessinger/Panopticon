using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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
/// Files written by an instance are deleted when the instance is disposed, so a
/// <c>using</c> scope (the standard pattern in tests and the TestHarness) prevents
/// disk accumulation. Inspect captures during the run via the <c>[CAPTURE]</c>
/// stdout lines. <see cref="ClearCaptureDirectory"/> removes every file in the
/// capture directory regardless of which instance wrote it (test teardown).
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
public sealed class FrameCaptureService : IDisposable
{
    /// <summary>
    /// Directory where capture PNGs are written.
    /// Defaults to <c>$TMPDIR/opensage_captures</c>.
    /// </summary>
    public static readonly string CaptureDirectory =
        Path.Combine(Path.GetTempPath(), "opensage_captures");

    private int _captureCount;
    private string _pendingHint;
    private readonly List<string> _writtenPaths = new();
    private bool _disposed;

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

        FrameCapturer.CaptureToPng(graphicsDevice, texture, path);

        _writtenPaths.Add(path);
        Console.WriteLine($"[CAPTURE] {path} | {hint}");
    }

    /// <summary>
    /// Deletes every file this instance wrote. Called automatically by <see cref="Dispose()"/>;
    /// safe to call explicitly for mid-run cleanup. Missing files are ignored.
    /// </summary>
    public void DeleteCaptures()
    {
        foreach (var path in _writtenPaths)
        {
            try { File.Delete(path); }
            catch (IOException) { }
        }
        _writtenPaths.Clear();
    }

    /// <summary>
    /// Deletes every file in <see cref="CaptureDirectory"/>, regardless of which process or
    /// instance wrote it. Intended for test teardown.
    /// </summary>
    public static void ClearCaptureDirectory()
    {
        if (!Directory.Exists(CaptureDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(CaptureDirectory))
        {
            try { File.Delete(file); }
            catch (IOException) { }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        DeleteCaptures();
    }
}
