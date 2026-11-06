using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Panopticon.Benchmarks.Simulation;
using Panopticon.Content;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Gltf;
using Panopticon.Content.Loaders;
using Panopticon.Data;
using Panopticon.Graphics;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Shaders;
using Panopticon.Input;
using Panopticon.IO;
using Panopticon.Logic;
using Panopticon.Mathematics;
using Panopticon.Network;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// WI-R3-01 scratch investigation for Track R R3-1 (procedural buildings prototype), never merged.
/// Spawns generator-emitted building glTFs on the Sedan preview map through
/// <see cref="RenderPipeline.BuildingRenderList"/>, captures frames, and writes per-frame timings.
/// </summary>
internal static class R3BuildingPrototypeScenario
{
    private const int ViewportWidth = 1024;
    private const int ViewportHeight = 768;
    private const float BaseSinkUnits = 1f;
    private const int GroundSampleGrid = 16;
    private const float OverviewStandoff = 385f;
    private const float OverviewEyeHeight = 600f;
    private const float OverviewTargetLift = 50f;
    private const float CloseupStandoff = 92f;
    private const float CloseupEyeLift = 42f;
    private const float CloseupTargetLift = 17f;
    private const float CloseupBuildingHeight = 34.81f;
    private const string MetricsFileName = "metrics.json";

    private static readonly Vector2 OverviewFocus = new(1900f, 1900f);

    // BATIMENT0000002275658305 footprint center and half extents, from its glTF position bounds.
    private static readonly Vector2 CloseupFocus = new(1779.45f, 1949.50f);
    private static readonly Vector2 CloseupHalfExtents = new(15.74f, 15.57f);

    // The importer maps glTF (x, y, z) to engine (z, x, y), but the generator already wrote engine axes.
    // This matrix maps the imported engine coordinates back to the generator's engine axes.
    private static readonly Matrix4x4 ImportedToGeneratorAxes = new(
        0f, 0f, 1f, 0f,
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 0f, 1f);

    public static int Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        if (config.R3WarmupFrames < 0 || config.R3Frames <= config.R3WarmupFrames)
        {
            reporter.Error($"R3Frames ({config.R3Frames}) must exceed non-negative R3WarmupFrames ({config.R3WarmupFrames}).");
            return 1;
        }

        string? sourceDirectory = config.R3BuildingsMode switch
        {
            R3BuildingMode.Merged => Path.Combine(config.R3BuildingsRoot, "gltf", "merged"),
            R3BuildingMode.PerBuilding => Path.Combine(config.R3BuildingsRoot, "gltf", "perbuilding"),
            _ => null,
        };
        if (sourceDirectory is not null && !Directory.Exists(sourceDirectory))
        {
            reporter.Error($"R3 buildings directory not found: {sourceDirectory}");
            return 1;
        }

        var modeName = config.R3BuildingsMode.ToString().ToLowerInvariant();
        var cameraName = config.R3Camera.ToString();
        var outputDirectory = config.R3OutputDirectory;
        Directory.CreateDirectory(outputDirectory);

        using var game = new Game(
            installation,
            config.Renderer,
            new Configuration { NoAudio = true, SkipStartingBuildings = true },
            null);

        var playerSettings = new[]
        {
            new PlayerSetting(0, "FactionAmerica", new ColorRgb(0, 0, 255), 0, PlayerOwner.EasyAi, "USA"),
        };
        game.StartSkirmishOrMultiPlayerGame(config.R3MapPath, new EchoConnection(), playerSettings, 1, isMultiPlayer: false);
        game.StartRun();
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        foreach (var player in game.Scene3D.Players)
        {
            player.DisableAI();
        }

        game.EnableFixedLogicTimestep();
        while (game.Scene2D.WndWindowManager.OpenWindowCount > 0)
        {
            game.Scene2D.WndWindowManager.PopWindow();
        }

        var placed = new List<PlacedModel>();
        var importSeconds = 0d;
        var placedBounds = default(AxisAlignedBoundingBox);
        if (sourceDirectory is not null)
        {
            var loadContext = CreateLoadContext(game, config.R3BuildingsRoot);
            var stopwatch = Stopwatch.StartNew();
            var models = ImportModels(loadContext, config.R3BuildingsRoot, sourceDirectory, config.R3BuildingsMode, config.R3PerBuildingLimit);
            importSeconds = stopwatch.Elapsed.TotalSeconds;

            foreach (var (_, model) in models)
            {
                var local = StandaloneModelCapture.ComputeEnclosingBoundingBox(model);
                var generatorBounds = AxisAlignedBoundingBox.Transform(local, ImportedToGeneratorAxes);
                var groundZ = config.R3BuildingsMode == R3BuildingMode.Merged
                    ? MeanGroundHeight(game, generatorBounds)
                    : GroundHeight(game, generatorBounds.GetCenter());
                var world = ImportedToGeneratorAxes * Matrix4x4.CreateTranslation(0f, 0f, groundZ - BaseSinkUnits);

                var instance = model.CreateInstance(loadContext);
                instance.Update(TimeInterval.Zero);
                placed.Add(new PlacedModel(instance, world, model.SubObjects.Length));

                var worldBounds = AxisAlignedBoundingBox.Transform(local, world);
                placedBounds = placed.Count == 1 ? worldBounds : AxisAlignedBoundingBox.CreateMerged(placedBounds, worldBounds);
            }
        }

        var instancesBuiltThisFrame = 0;
        void OnBuildingRenderList(object? sender, BuildingRenderListEventArgs e)
        {
            foreach (var placement in placed)
            {
                placement.Instance.SetWorldMatrix(placement.World);
                placement.Instance.BuildRenderList(
                    e.RenderList,
                    e.Camera,
                    true,
                    new MeshShaderResources.RenderItemConstantsPS
                    {
                        HouseColor = Vector3.One,
                        Opacity = 1.0f,
                        TintColor = Vector3.One,
                    });
                StandaloneModelCapture.FlushMeshWorldConstants(game, placement.Instance);
                instancesBuiltThisFrame++;
            }
        }

        var (eye, target) = config.R3Camera == R3CameraView.Overview
            ? OverviewView(game)
            : CloseupView(game);

        var renderPipeline = game.Graphics.RenderPipeline;
        renderPipeline.BuildingRenderList += OnBuildingRenderList;
        var logicSamples = new List<double>(config.R3Frames);
        var renderSamples = new List<double>(config.R3Frames);
        var captures = new List<string>();
        var lastFrameInstances = 0;
        try
        {
            for (var frame = 0; frame < config.R3Frames; frame++)
            {
                var logicStart = Stopwatch.GetTimestamp();
                game.Update(Array.Empty<InputMessage>());
                var logicElapsed = Stopwatch.GetElapsedTime(logicStart);

                game.Scene3D.Camera.SetLookAt(eye, target, Vector3.UnitZ);

                instancesBuiltThisFrame = 0;
                var renderStart = Stopwatch.GetTimestamp();
                game.Panel.EnsureFrame(new Mathematics.Rectangle(0, 0, ViewportWidth, ViewportHeight));
                game.Render();
                game.GraphicsDevice.WaitForIdle();
                var renderElapsed = Stopwatch.GetElapsedTime(renderStart);
                lastFrameInstances = instancesBuiltThisFrame;

                if (frame >= config.R3WarmupFrames)
                {
                    logicSamples.Add(logicElapsed.TotalMilliseconds);
                    renderSamples.Add(renderElapsed.TotalMilliseconds);
                }

                if (frame == 0 || frame == config.R3WarmupFrames || frame == config.R3Frames - 1)
                {
                    var fileName = $"{modeName}-{cameraName}-f{frame}.png";
                    FrameCapturer.CaptureToPng(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target, Path.Combine(outputDirectory, fileName));
                    captures.Add(fileName);
                }
            }
        }
        finally
        {
            renderPipeline.BuildingRenderList -= OnBuildingRenderList;
            foreach (var placement in placed)
            {
                placement.Instance.Dispose();
            }
        }

        if (config.R3Camera == R3CameraView.Closeup)
        {
            ReportCornerProjection(game, reporter);
        }

        var logicMs = Summarize(logicSamples);
        var renderMs = Summarize(renderSamples);
        var metrics = new R3Metrics(
            Scenario: nameof(BuiltInScenario.R3BuildingPrototype),
            Mode: modeName,
            Camera: cameraName,
            CameraEye: ToPoint(eye),
            CameraTarget: ToPoint(target),
            Map: config.R3MapPath,
            BuildingsRoot: config.R3BuildingsRoot,
            Frames: config.R3Frames,
            WarmupFrames: config.R3WarmupFrames,
            PerBuildingLimit: config.R3PerBuildingLimit,
            ImportSeconds: importSeconds,
            ModelCount: placed.Count,
            MeshSubObjectCount: placed.Sum(p => p.SubObjectCount),
            InstancesBuiltPerFrame: lastFrameInstances,
            PlacedBoundsMin: ToPoint(placed.Count > 0 ? placedBounds.Min : Vector3.Zero),
            PlacedBoundsMax: ToPoint(placed.Count > 0 ? placedBounds.Max : Vector3.Zero),
            LogicMs: logicMs,
            RenderMs: renderMs,
            Captures: captures);
        File.WriteAllText(
            Path.Combine(outputDirectory, MetricsFileName),
            JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));

        reporter.Info(
            $"R3 {modeName} {cameraName}: models {placed.Count}, import {importSeconds:F2}s, " +
            $"logic mean {logicMs.Mean:F3} ms p95 {logicMs.P95:F3} ms, " +
            $"render mean {renderMs.Mean:F3} ms p95 {renderMs.P95:F3} ms ({renderMs.Samples} samples)");
        reporter.Info($"Metrics: {Path.GetFullPath(Path.Combine(outputDirectory, MetricsFileName))}");
        return 0;
    }

    private static AssetLoadContext CreateLoadContext(Game game, string root)
    {
        var fileSystem = new DiskFileSystem(root);
        var gameLoadContext = game.AssetStore.LoadContext;
        return new AssetLoadContext(
            new CompositeFileSystem(fileSystem, gameLoadContext.FileSystem),
            gameLoadContext.Language,
            gameLoadContext.GraphicsDevice,
            gameLoadContext.StandardGraphicsResources,
            gameLoadContext.ShaderResources,
            gameLoadContext.ShaderSetStore,
            gameLoadContext.AssetStore);
    }

    private static List<(string Name, Model Model)> ImportModels(
        AssetLoadContext loadContext,
        string root,
        string sourceDirectory,
        R3BuildingMode mode,
        int perBuildingLimit)
    {
        var importContext = new AssetImportContext(new DiskFileSystem(root), language: null);
        var modelImporter = new GltfModelImporter(new GltfImportOptions(unitsPerMeter: 1f));

        var files = Directory.GetFiles(sourceDirectory, "*.gltf").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        if (mode == R3BuildingMode.PerBuilding && perBuildingLimit > 0)
        {
            files = files.Take(perBuildingLimit).ToArray();
        }

        var models = new List<(string Name, Model Model)>(files.Length);
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var imported = modelImporter.ImportModel(relative, importContext)
                ?? throw new InvalidOperationException($"glTF import returned null: {relative}");
            models.Add((relative, ImportedModelMaterializer.MaterializeModel(imported, loadContext)));
        }

        return models;
    }

    private static float MeanGroundHeight(Game game, in AxisAlignedBoundingBox bounds)
    {
        var sum = 0d;
        var count = 0;
        var width = bounds.Max.X - bounds.Min.X;
        var depth = bounds.Max.Y - bounds.Min.Y;
        for (var i = 0; i < GroundSampleGrid; i++)
        {
            var x = bounds.Min.X + width * (i + 0.5f) / GroundSampleGrid;
            for (var j = 0; j < GroundSampleGrid; j++)
            {
                var y = bounds.Min.Y + depth * (j + 0.5f) / GroundSampleGrid;
                if (game.Scene3D.Terrain.GetGroundHeight(new Vector2(x, y)) is { } height)
                {
                    sum += height;
                    count++;
                }
            }
        }

        return count == 0 ? 0f : (float)(sum / count);
    }

    private static float GroundHeight(Game game, Vector3 point)
    {
        return game.Scene3D.Terrain.GetGroundHeight(new Vector2(point.X, point.Y)) ?? 0f;
    }

    private static (Vector3 Eye, Vector3 Target) OverviewView(Game game)
    {
        var ground = GroundHeight(game, new Vector3(OverviewFocus, 0f));
        var target = new Vector3(OverviewFocus, ground + OverviewTargetLift);
        var standoff = OverviewStandoff * MathF.Sqrt(0.5f);
        var eye = new Vector3(OverviewFocus.X - standoff, OverviewFocus.Y - standoff, ground + OverviewEyeHeight);
        return (eye, target);
    }

    private static (Vector3 Eye, Vector3 Target) CloseupView(Game game)
    {
        var ground = GroundHeight(game, new Vector3(CloseupFocus, 0f));
        var target = new Vector3(CloseupFocus, ground + CloseupTargetLift);
        var standoff = CloseupStandoff * MathF.Sqrt(0.5f);
        var eye = new Vector3(CloseupFocus.X + standoff, CloseupFocus.Y - standoff, ground + CloseupEyeLift);
        return (eye, target);
    }

    private static void ReportCornerProjection(Game game, ITestHarnessReporter reporter)
    {
        var camera = game.Scene3D.Camera;
        var viewProjection = camera.View * camera.Projection;
        var baseZ = GroundHeight(game, new Vector3(CloseupFocus, 0f)) - BaseSinkUnits;
        var worstExtent = 0f;
        var allInside = true;
        var corners = new List<string>(8);
        foreach (var sx in new[] { -1f, 1f })
        {
            foreach (var sy in new[] { -1f, 1f })
            {
                foreach (var z in new[] { 0f, CloseupBuildingHeight })
                {
                    var corner = new Vector3(
                        CloseupFocus.X + sx * CloseupHalfExtents.X,
                        CloseupFocus.Y + sy * CloseupHalfExtents.Y,
                        baseZ + z);
                    var clip = Vector4.Transform(new Vector4(corner, 1f), viewProjection);
                    var ndcX = clip.X / clip.W;
                    var ndcY = clip.Y / clip.W;
                    var extent = MathF.Max(MathF.Abs(ndcX), MathF.Abs(ndcY));
                    allInside &= clip.W > 0f && extent <= 0.95f;
                    worstExtent = MathF.Max(worstExtent, extent);
                    corners.Add($"({ndcX:F3},{ndcY:F3})");
                }
            }
        }

        reporter.Info(
            $"R3 closeup corner NDC: {string.Join(" ", corners)}; worst |ndc| {worstExtent:F3}; " +
            $"all inside 5% margin: {allInside}");
    }

    private static TimingSummary Summarize(List<double> samples)
    {
        var sorted = samples.OrderBy(v => v).ToArray();
        var count = sorted.Length;
        var mean = sorted.Average();
        var variance = count > 1 ? sorted.Sum(v => (v - mean) * (v - mean)) / (count - 1) : 0d;
        var p95 = sorted[(int)Math.Ceiling(0.95 * count) - 1];
        var statistics = count >= 2 ? BenchmarkStatistics.FromSamples("frame-ms", sorted, seed: 1) : null;
        return new TimingSummary(
            Samples: count,
            Mean: mean,
            Variance: variance,
            P95: p95,
            Min: sorted[0],
            Max: sorted[^1],
            Median: statistics?.Median,
            ConfidenceLow: statistics?.ConfidenceLow,
            ConfidenceHigh: statistics?.ConfidenceHigh);
    }

    private static Point3 ToPoint(Vector3 v) => new(v.X, v.Y, v.Z);

    private sealed record PlacedModel(ModelInstance Instance, Matrix4x4 World, int SubObjectCount);

    private sealed record Point3(float X, float Y, float Z);

    private sealed record TimingSummary(
        int Samples,
        double Mean,
        double Variance,
        double P95,
        double Min,
        double Max,
        double? Median,
        double? ConfidenceLow,
        double? ConfidenceHigh);

    private sealed record R3Metrics(
        string Scenario,
        string Mode,
        string Camera,
        Point3 CameraEye,
        Point3 CameraTarget,
        string Map,
        string BuildingsRoot,
        int Frames,
        int WarmupFrames,
        int PerBuildingLimit,
        double ImportSeconds,
        int ModelCount,
        int MeshSubObjectCount,
        int InstancesBuiltPerFrame,
        Point3 PlacedBoundsMin,
        Point3 PlacedBoundsMax,
        TimingSummary LogicMs,
        TimingSummary RenderMs,
        IReadOnlyList<string> Captures);
}

public enum R3BuildingMode
{
    Off,
    Merged,
    PerBuilding,
}

public enum R3CameraView
{
    Overview,
    Closeup,
}
