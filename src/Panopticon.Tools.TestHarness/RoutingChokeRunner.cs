using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Panopticon.Data;
using Panopticon.Graphics;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Panopticon.Navigation;
using Panopticon.Network;
using Panopticon.Simulation;
using NavigationSystem = Panopticon.Navigation.Navigation;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// The routing-choke scenario: one group move order drives a block of ground units through a
/// single gap in a structure wall, exercising the multi-unit routing stack end to end
/// (OrderProcessor.MoveUnits and the swept-footprint gate) in a constrained space.
/// The spawned units have no squads and request individual paths. The report records per-unit arrival
/// frames so throughput and deadlock regressions are visible as data; the optional PNG
/// sequence plus manifest feeds scripts/annotate_capture_gif.py for a visual record.
/// </summary>
public static class RoutingChokeRunner
{
    public const string ScenarioId = "RoutingChoke";

    private const int ViewportWidth = 1280;
    private const int ViewportHeight = 720;
    private const string DefaultMapPath = "maps/alpine assault/alpine assault.map";
    private const string UnitDefinitionName = "AmericaTankCrusader";
    private const string WallDefinitionName = "AmericaPowerPlant";

    // The corridor runs north (+Y): units spawn south of the wall, the destination lies
    // behind it, and the wall's only opening is the configured gap.
    private const float CorridorCenterX = 1000f;
    private const float WallY = 1000f;
    private const float SpawnCenterY = 700f;
    private const float DestinationY = 1280f;
    private const float WallSpanFrom = 550f;
    private const float WallSpanTo = 1450f;
    private const float WallSpacing = 80f;
    private const float UnitSpacing = 60f;
    private const int UnitColumns = 8;
    private const float ArrivalRadius = 90f;
    private const int SettleFrames = 30;
    private const float CameraHeight = 1600f;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // scripts/annotate_capture_gif.py reads PascalCase keys (Frames, ViewportWidth, ...),
    // matching the ScenarioCapture manifest writer.
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
    };

    public static int Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(reporter);

        var unitCount = config.RoutingChokeUnitCount ?? 40;
        var gapWidth = config.RoutingChokeGapWidth ?? 120;
        var frameLimit = config.RoutingChokeFrameLimit ?? 9000;
        var captureStride = config.RoutingChokeCaptureStride ?? 15;
        var mapPath = string.IsNullOrWhiteSpace(config.RoutingChokeMapPath) ? DefaultMapPath : config.RoutingChokeMapPath!;
        var reportPath = config.RoutingChokeReportPath ?? "artifacts/routing-choke/routing-choke.json";
        var captureDirectory = config.RoutingChokeCaptureDirectory;

        if (unitCount < 1 || gapWidth < 40 || frameLimit < 1 || captureStride < 1 || config.RoutingChokeProgressTimeoutFrames < 1)
        {
            reporter.Error("RoutingChoke knobs must satisfy: unit count >= 1, gap width >= 40, frame limit >= 1, capture stride >= 1, progress timeout >= 1.");
            return 1;
        }

        reporter.Info($"RoutingChoke: {unitCount} units through a {gapWidth}-unit gap, frame limit {frameLimit}, map {mapPath}.");

        using var game = new Game(
            installation,
            config.Renderer,
            new Configuration { NoAudio = true, SkipStartingBuildings = true },
            null,
            null);

        var playerSettings = new[]
        {
            new PlayerSetting(0, "FactionAmerica", new ColorRgb(64, 159, 255), 0, PlayerOwner.EasyAi, "RoutingChoke"),
        };

        game.StartSkirmishOrMultiPlayerGame(mapPath, new EchoConnection(), playerSettings, 1, isMultiPlayer: false);
        game.StartRun();
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        // Hide visual clutter without changing particle updates or simulation RNG consumption.
        game.Scene3D.RenderScene.GetRenderBucket("Particles").Visible = false;

        foreach (var player in game.Scene3D.Players)
        {
            player.DisableAI();
        }

        game.EnableFixedLogicTimestep();
        game.EnableFixedRenderTimestep();

        // The capture wants the 3-D scene only.
        while (game.Scene2D.WndWindowManager.OpenWindowCount > 0)
        {
            game.Scene2D.WndWindowManager.PopWindow();
        }

        var unitDefinition = game.AssetStore.ObjectDefinitions.GetByName(UnitDefinitionName);
        var wallDefinition = game.AssetStore.ObjectDefinitions.GetByName(WallDefinitionName);
        if (unitDefinition == null || wallDefinition == null)
        {
            reporter.Error($"Unknown object definition '{(unitDefinition == null ? UnitDefinitionName : WallDefinitionName)}'.");
            return 1;
        }

        // Skirmish player slots lead with neutral and civilian; the first setting owns slot 2.
        var owner = game.Scene3D.Players[2];
        var neutral = game.Scene3D.Players[0];

        var wallCount = 0;
        for (var x = WallSpanFrom; x <= WallSpanTo; x += WallSpacing)
        {
            if (MathF.Abs(x - CorridorCenterX) < gapWidth / 2f + WallSpacing / 2f)
            {
                continue;
            }

            Spawn(game, wallDefinition, neutral, x, WallY);
            wallCount++;
        }

        if (wallCount < 4)
        {
            reporter.Error($"Wall layout produced only {wallCount} segments; gap width {gapWidth} leaves no constrained space.");
            return 1;
        }

        var destination = new Vector3(CorridorCenterX, DestinationY, 0);
        var units = new List<GameObject>(unitCount);
        var rows = (unitCount + UnitColumns - 1) / UnitColumns;
        for (var i = 0; i < unitCount; i++)
        {
            var column = i % UnitColumns;
            var row = i / UnitColumns;
            var x = CorridorCenterX + (column - (UnitColumns - 1) / 2f) * UnitSpacing;
            var y = SpawnCenterY - row * UnitSpacing;
            units.Add(Spawn(game, unitDefinition, owner, x, y));
        }

        for (var frame = 0; frame < SettleFrames; frame++)
        {
            game.Update(Array.Empty<Input.InputMessage>());
        }

        var startPositions = units.ToDictionary(unit => unit.Id, unit => unit.Translation);

        RenderFrame(game);

        var blockedFollowersBefore = GroupMovement.BlockedFollowerCount;
        var pathRequestsBefore = NavigationSystem.PathRequestCount;

        // The order under test: one selection + one move, exactly like a player drag-move
        // or the army AI's advance orders.
        var orderProcessor = new OrderProcessor(game);
        orderProcessor.Process(
        [
            Order.CreateSetSelection(2, units.Select(unit => unit.Id)),
            Order.CreateMoveOrder(2, destination),
        ]);

        reporter.Info("Group move dispatched; watching arrivals.");

        string? preparedCaptureDirectory = null;
        if (!string.IsNullOrWhiteSpace(captureDirectory))
        {
            preparedCaptureDirectory = PrepareCaptureDirectory(captureDirectory!);
        }

        var arrivalFrame = new Dictionary<ObjectId, int>();
        var captureRecords = new List<RoutingChokeCaptureFrame>();
        var captureIndex = 0;
        var framesRun = 0;
        var progress = new RoutingChokeProgressMonitor(
            units.Select(unit => unit.Translation.Vector2XY()).ToArray(), config.RoutingChokeProgressTimeoutFrames);
        var terminationReason = "FrameLimit";
        for (; framesRun < frameLimit; framesRun++)
        {
            game.Update(Array.Empty<Input.InputMessage>());

            foreach (var unit in units)
            {
                if (arrivalFrame.ContainsKey(unit.Id))
                {
                    continue;
                }

                var delta = unit.Translation - destination;
                if (delta.Vector2XY().LengthSquared() < ArrivalRadius * ArrivalRadius)
                {
                    arrivalFrame[unit.Id] = framesRun + 1;
                }
            }

            var stopReason = arrivalFrame.Count == units.Count
                ? "AllArrived"
                : progress.Observe(framesRun + 1,
                    units.Select(unit => unit.Translation.Vector2XY()).ToArray(),
                    units.Any(unit => unit.AIUpdate?.TargetPoints is { Count: > 0 }),
                    config.RoutingChokeHasTimedGate);
            var captured = preparedCaptureDirectory != null && (framesRun % captureStride == 0 || stopReason != null);
            if (captured)
            {
                RenderFrame(game);
                var file = $"frame_{captureIndex:D4}.png";
                FrameCapturer.CaptureToPng(
                    game.GraphicsDevice,
                    game.Panel.Framebuffer.ColorTargets[0].Target,
                    Path.Combine(preparedCaptureDirectory!, file));
                var actors = new List<RoutingChokeManifestActor>(units.Count);
                for (var i = 0; i < units.Count; i++)
                {
                    var screen = game.Scene3D.Camera.WorldToScreenPoint(units[i].Translation);
                    actors.Add(new RoutingChokeManifestActor("unit", $"unit {i}", screen.X, screen.Y, 16f));
                }

                captureRecords.Add(new RoutingChokeCaptureFrame(captureIndex, framesRun + SettleFrames + 1, file, actors, [],
                    RoutingChokeNavigationOverlay.Capture(game, units)));
                captureIndex++;
            }

            if (stopReason != null)
            {
                terminationReason = stopReason;
                framesRun++;
                break;
            }
        }

        var arrivals = arrivalFrame.Values.OrderBy(frame => frame).ToArray();
        var stuck = units.Where(unit => !arrivalFrame.ContainsKey(unit.Id)).Select(unit => (int)unit.Id.Index).ToArray();
        var perUnit = units
            .Select(unit => new RoutingChokeUnitRecord(
                (int)unit.Id.Index,
                startPositions[unit.Id].X,
                startPositions[unit.Id].Y,
                unit.Translation.X,
                unit.Translation.Y,
                Vector2.Distance(unit.Translation.Vector2XY(), startPositions[unit.Id].Vector2XY()),
                Vector2.Distance(unit.Translation.Vector2XY(), destination.Vector2XY()),
                arrivalFrame.TryGetValue(unit.Id, out var frame) ? frame : null))
            .OrderBy(record => record.Id)
            .ToArray();

        var report = new RoutingChokeReport(
            ScenarioId,
            mapPath,
            unitCount,
            gapWidth,
            framesRun,
            frameLimit,
            arrivals.Length,
            arrivals.Length > 0 ? arrivals[0] : null,
            arrivals.Length > 0 ? arrivals[arrivals.Length / 2] : null,
            arrivals.Length > 0 ? arrivals[^1] : null,
            stuck,
            GroupMovement.BlockedFollowerCount - blockedFollowersBefore,
            NavigationSystem.PathRequestCount - pathRequestsBefore,
            perUnit,
            terminationReason);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonOptions));
        reporter.Info($"RoutingChoke report saved to: {reportPath}");

        if (preparedCaptureDirectory != null)
        {
            WriteCaptureManifest(preparedCaptureDirectory, captureRecords);
            reporter.Info($"Captured {captureRecords.Count} frames to: {preparedCaptureDirectory}");
        }

        reporter.Info(
            $"RoutingChoke: {arrivals.Length}/{unitCount} arrived"
            + (arrivals.Length > 0 ? $", first {arrivals[0]}, median {arrivals[arrivals.Length / 2]}, last {arrivals[^1]}" : string.Empty)
            + $", blocked followers {report.BlockedFollowerDelta}, path requests {report.PathRequestDelta}, stopped: {terminationReason} at frame {framesRun}.");

        return arrivals.Length == unitCount ? 0 : 5;
    }

    private static GameObject Spawn(Game game, ObjectDefinition definition, Player owner, float x, float y)
    {
        var planar = new Vector2(x, y);
        var height = game.Scene3D.Terrain.GetGroundHeight(planar) ?? 0f;
        var gameObject = game.Scene3D.GameObjects.CreateObject(definition, owner);
        gameObject.UpdateTransform(
            new Vector3(planar.X, planar.Y, height),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f));
        gameObject.UpdateColliders();

        return gameObject;
    }

    private static string PrepareCaptureDirectory(string captureDirectory)
    {
        if (Directory.Exists(captureDirectory))
        {
            foreach (var png in Directory.GetFiles(captureDirectory, "*.png"))
            {
                File.Delete(png);
            }

            var ownedManifest = Path.Combine(captureDirectory, "manifest.json");
            if (File.Exists(ownedManifest))
            {
                File.Delete(ownedManifest);
            }
        }
        else
        {
            Directory.CreateDirectory(captureDirectory);
        }

        return captureDirectory;
    }

    private static void WriteCaptureManifest(string captureDirectory, IReadOnlyList<RoutingChokeCaptureFrame> records)
    {
        var manifest = new RoutingChokeCaptureManifest(ViewportWidth, ViewportHeight, records);
        File.WriteAllText(
            Path.Combine(captureDirectory, "manifest.json"),
            JsonSerializer.Serialize(manifest, ManifestJsonOptions));
    }

    private static void RenderFrame(Game game)
    {
        game.Panel.EnsureFrame(new Mathematics.Rectangle(0, 0, ViewportWidth, ViewportHeight));
        var center = new Vector2(CorridorCenterX, 925f);
        var target = new Vector3(center, game.Scene3D.Terrain.GetGroundHeight(center) ?? 0);
        // Apply after tactical camera updates; a horizontal up vector supports exact overhead.
        game.Scene3D.Camera.SetLookAt(target + Vector3.UnitZ * CameraHeight, target, Vector3.UnitY);
        game.Render();
    }
}
