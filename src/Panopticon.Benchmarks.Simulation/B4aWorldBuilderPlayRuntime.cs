using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Panopticon.Data;
using Panopticon.Data.Map;
using Panopticon.Graphics;
using Panopticon.Tools.WorldBuilder.Core;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// Play stage over the real WorldBuilder viewport. Each call opens the package, boots an offscreen
/// game through <see cref="GameViewportRenderer"/> under a fixed logic timestep, renders the frames,
/// and disposes the game. Timings and render counters are kept per call in <see cref="Runs"/>.
/// The seed is not applied: the editor boot path has no seed input.
/// </summary>
public sealed class B4aWorldBuilderPlayRuntime : IB4aPlayRuntime
{
    public const string BootStage = "b4a.play.boot";
    public const string FramesStage = "b4a.play.frames";
    public const string CleanupStage = "b4a.cleanup";

    private const uint ViewportWidth = 1280;
    private const uint ViewportHeight = 800;
    private const string PreviewStatusPrefix = "3D preview:";

    private readonly GameInstallation _installation;
    private readonly string? _captureDirectory;
    private readonly Action<string>? _stageStarting;
    private readonly List<RunInfo> _runs = [];

    public B4aWorldBuilderPlayRuntime(
        GameInstallation installation,
        string? captureDirectory = null,
        Action<string>? stageStarting = null)
    {
        ArgumentNullException.ThrowIfNull(installation);
        _installation = installation;
        _captureDirectory = captureDirectory;
        _stageStarting = stageStarting;

        if (captureDirectory is not null)
        {
            Directory.CreateDirectory(captureDirectory);
        }
    }

    public IReadOnlyList<RunInfo> Runs => _runs;

    public int RunFrames(string packageDirectory, int frames, uint seed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        ArgumentOutOfRangeException.ThrowIfNegative(frames);

        var map = EditorMap.OpenPackage(packageDirectory);
        var renderer = new GameViewportRenderer { Installation = _installation };
        RunInfo info;
        double disposeMs;
        try
        {
            info = Play(renderer, map, frames, _runs.Count);
        }
        finally
        {
            _stageStarting?.Invoke(CleanupStage);
            var disposeWatch = Stopwatch.StartNew();
            renderer.Dispose();
            disposeMs = disposeWatch.Elapsed.TotalMilliseconds;
        }

        _runs.Add(info with { DisposeMs = disposeMs });
        return info.FramesCompleted;
    }

    private RunInfo Play(GameViewportRenderer renderer, EditorMap map, int frames, int runIndex)
    {
        _stageStarting?.Invoke(BootStage);
        var bootWatch = Stopwatch.StartNew();
        string? loadStatus = null;
        renderer.LoadStatus += status => loadStatus = status;
        renderer.Initialize(ViewportWidth, ViewportHeight);
        if (!renderer.IsInitialized)
        {
            throw new InvalidOperationException("Viewport renderer did not initialize; check the Generals installation.");
        }

        renderer.LoadMap(map);
        if (loadStatus is null || !loadStatus.StartsWith(PreviewStatusPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Map did not load into the 3D preview: {loadStatus ?? "no status reported"}");
        }

        renderer.EnableFixedLogicTimestep();
        bootWatch.Stop();

        _stageStarting?.Invoke(FramesStage);
        var captureFrames = CaptureFrameIndices(frames);
        var capturePaths = new List<string>(captureFrames.Length);
        var frameWallMs = new List<double>(frames);
        var logicStart = renderer.CurrentLogicFrame;
        for (var frame = 0; frame < frames; frame++)
        {
            var start = Stopwatch.GetTimestamp();
            renderer.RenderFrame();
            frameWallMs.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);

            if (_captureDirectory is not null && Array.IndexOf(captureFrames, frame) >= 0)
            {
                capturePaths.Add(CaptureFrame(renderer, _captureDirectory, runIndex, frame));
            }
        }

        var framesCompleted = (int)(renderer.CurrentLogicFrame - logicStart);
        var stats = renderer.RenderStatistics
            ?? throw new InvalidOperationException("Render statistics are unavailable after the frame loop.");

        return new RunInfo(
            runIndex,
            frames,
            framesCompleted,
            bootWatch.Elapsed.TotalMilliseconds,
            frameWallMs,
            0,
            stats.DrawCalls,
            stats.InstancedDrawCalls,
            stats.InstancedBatches,
            stats.InstancesDrawn,
            map.MapFile.ObjectsList.Objects.Length,
            renderer.LoadedGameObjectCount,
            renderer.StreamedTerrainPatchCount,
            renderer.StreamedTerrainVertexBytes,
            capturePaths);
    }

    // Capture happens after the frame's stopwatch stops, so readback does not inflate frame times.
    private static string CaptureFrame(GameViewportRenderer renderer, string captureDirectory, int runIndex, int frame)
    {
        var path = Path.Combine(captureDirectory, $"run-{runIndex}-frame-{frame:D4}.png");
        FrameCapturer.CaptureToPng(
            renderer.GraphicsDevice ?? throw new InvalidOperationException("Viewport has no graphics device to capture."),
            renderer.FrameTexture ?? throw new InvalidOperationException("Viewport has no frame texture to capture."),
            path);
        return path;
    }

    private static int[] CaptureFrameIndices(int frames)
    {
        if (frames == 0)
        {
            return [];
        }

        return [.. new[] { 0, frames / 3, 2 * frames / 3, frames - 1 }.Distinct().Order()];
    }

    /// <param name="FrameWallMs">Wall time of each <c>RenderFrame</c> call, in order, excluding captures.</param>
    /// <param name="DisposeMs">Time to dispose the renderer and its game after the frames.</param>
    /// <param name="DrawCalls">Last frame's <c>RenderStatistics</c> draw-call count.</param>
    /// <param name="MapObjectCount">Objects in the opened map package.</param>
    /// <param name="LoadedGameObjectCount">Game objects in the booted scene after the frame loop.</param>
    /// <param name="StreamedTerrainPatchCount">Terrain patches resident in the streamer after the frame loop.</param>
    /// <param name="StreamedTerrainVertexBytes">Terrain vertex bytes resident in the streamer after the frame loop.</param>
    public sealed record RunInfo(
        int RunIndex,
        int FramesRequested,
        int FramesCompleted,
        double BootMs,
        IReadOnlyList<double> FrameWallMs,
        double DisposeMs,
        int DrawCalls,
        int InstancedDrawCalls,
        int InstancedBatches,
        int InstancesDrawn,
        int MapObjectCount,
        int LoadedGameObjectCount,
        int StreamedTerrainPatchCount,
        long StreamedTerrainVertexBytes,
        IReadOnlyList<string> CapturePaths);
}
