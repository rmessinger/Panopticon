using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Avalonia;
using Panopticon;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Data;
using Panopticon.Data.Map;
using Panopticon.Graphics;
using Panopticon.Input;
using Panopticon.Mathematics;
using Panopticon.Mods.Generals;
using Panopticon.Terrain;
using Panopticon.Tools.WorldBuilder.Core;
using Environment = System.Environment;
using Rectangle = Panopticon.Mathematics.Rectangle;

namespace Panopticon.Tools.WorldBuilder.Avalonia;

internal static class Program
{
    private const int RenderWidth = 1280;

    private const int RenderHeight = 800;

    private const int WarmupFrameCount = 3;

    [STAThread]
    public static void Main(string[] args)
    {
        // --screenshot <path> [mapPath]: render one frame headlessly (no window, no
        // display), write a PNG, and exit. Used for automated validation on Linux.
        // mapPath may be a legacy .map (archive-capable, unchanged behavior) or an H3
        // package directory containing manifest.json.
        if (args.Length > 0 && args[0].Equals("--screenshot", StringComparison.OrdinalIgnoreCase))
        {
            // --screenshot <packageDirectory> --wb-workflow <outputDirectory>: scripted
            // edit / save / reload workflow with before-edit, after-edit, and after-reload
            // stills plus ordered per-stage frames for GIF assembly.
            var workflowIndex = Array.FindIndex(
                args,
                a => a.Equals("--wb-workflow", StringComparison.OrdinalIgnoreCase));

            if (workflowIndex > 0)
            {
                var packageDirectory = args.Length > 1 ? args[1] : null;
                var outputDirectory = workflowIndex + 1 < args.Length ? args[workflowIndex + 1] : null;

                if (packageDirectory is null || outputDirectory is null)
                {
                    Console.Error.WriteLine(
                        "usage: --screenshot <packageDirectory> --wb-workflow <outputDirectory>");
                    Environment.Exit(1);
                    return;
                }

                Environment.Exit(ScreenshotPackageEditWorkflow(packageDirectory, outputDirectory));
                return;
            }

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
        var installation = FindInstallationOrExit();

        if (mapPath is not null && IsPackageAddressed(mapPath))
        {
            var editorMap = EditorMap.OpenPackage(mapPath);
            CaptureRenderStage(
                installation,
                mapPath,
                editorMap.MapFile.HeightMapData!,
                path,
                framesDirectory: null,
                frameCount: 0);
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
        renderer.Initialize(RenderWidth, RenderHeight);

        // Warm-up frames absorb first-execution engine-global uploads and let
        // terrain visual resources settle before capture.
        for (var i = 0; i < WarmupFrameCount; i++)
        {
            renderer.RenderFrame();
        }

        FrameCapturer.CaptureToPng(
            renderer.GraphicsDevice!,
            renderer.FrameTexture!,
            path);
        Console.WriteLine($"Wrote {path}");
    }

    /// <summary>
    /// WI-H6-02 package edit workflow: opens the package through the editor's public
    /// boundary, renders it through the runtime's directory-package load branch, applies one
    /// scripted height brush stroke and one placed-object edit plus companion edits through
    /// the editor's public edit surface, saves through the package save path, reopens, and
    /// renders after-edit and after-reload stages from the saved package. Each stage writes
    /// a still plus ordered real rendered frames for GIF assembly.
    /// </summary>
    private static int ScreenshotPackageEditWorkflow(string packageDirectory, string outputDirectory)
    {
        var installation = FindInstallationOrExit();

        try
        {
            Directory.CreateDirectory(outputDirectory);
            var framesRoot = Path.Combine(outputDirectory, "frames");
            const int workflowFrameCount = 6;

            // Stage 1: before-edit, straight from the saved package.
            var editorMap = EditorMap.OpenPackage(packageDirectory);
            Console.WriteLine($"Opened package: {packageDirectory}");
            CaptureRenderStage(
                installation,
                packageDirectory,
                editorMap.MapFile.HeightMapData!,
                Path.Combine(outputDirectory, "before-edit.png"),
                Path.Combine(framesRoot, "before-edit"),
                workflowFrameCount);

            // Stage 2: scripted edits through the editor's public edit surface.
            var heightMapData = editorMap.MapFile.HeightMapData!;
            var beforeElevations = (ushort[,])heightMapData.Elevations.Clone();
            var beforeObjectCount = editorMap.MapFile.ObjectsList.Objects.Length;

            var brush = new HeightBrushTool
            {
                Mode = HeightBrushMode.Mound,
                Brush = { RadiusCells = 5, Strength = 1f, HeightDelta = 100 },
            };
            var brushWorld = OffsetWorld(heightMapData, 0, 0);
            brush.OnPointerDown(PointerEvent(ViewportPointerEventType.Down, brushWorld), editorMap);
            brush.OnPointerUp(PointerEvent(ViewportPointerEventType.Up, brushWorld), editorMap);

            var placer = new PlaceObjectTool { SelectedObjectType = "AmericaInfantryRanger" };
            var placeWorld = OffsetWorld(heightMapData, 40, -30);
            placer.OnPointerDown(PointerEvent(ViewportPointerEventType.Down, placeWorld), editorMap);

            editorMap.MapIniContent += "; h6-02 scripted companion edit" + Environment.NewLine;
            editorMap.MarkDirty();

            var changedSamples = CountChangedSamples(beforeElevations, heightMapData.Elevations);
            Console.WriteLine(
                $"Edited: {changedSamples} elevation samples changed; objects " +
                $"{beforeObjectCount} -> {editorMap.MapFile.ObjectsList.Objects.Length}.");

            // Stage 3: save through the editor's public package save path.
            editorMap.Save();

            // Stage 4: after-edit. Render the saved package through the runtime's own
            // directory-package load branch; the runtime re-reads exactly what the editor
            // wrote. (Materializing the live state to a legacy .map is not viable today:
            // PolygonTrigger.WriteTo writes version-gated fields unconditionally, so
            // re-parsed Generals polygon triggers misalign on read.)
            CaptureRenderStage(
                installation,
                packageDirectory,
                heightMapData,
                Path.Combine(outputDirectory, "after-edit.png"),
                Path.Combine(framesRoot, "after-edit"),
                workflowFrameCount);

            // Stage 5: reload through the editor's public boundary; verify the reopened
            // state equals the edited in-memory state (runtime mirror of the round-trip
            // tests; mismatch is a non-zero exit, not a silent pass).
            var reopened = EditorMap.OpenPackage(packageDirectory);
            var report = BuildEqualityReport(editorMap, reopened);
            Console.WriteLine(report);

            // Stage 6: after-reload, straight from the saved package.
            CaptureRenderStage(
                installation,
                packageDirectory,
                reopened.MapFile.HeightMapData!,
                Path.Combine(outputDirectory, "after-reload.png"),
                Path.Combine(framesRoot, "after-reload"),
                workflowFrameCount);

            Console.WriteLine($"Workflow complete: {outputDirectory}");
            return report.Contains("MISMATCH", StringComparison.Ordinal) ? 2 : 0;
        }
        catch (MapImportException exception)
        {
            foreach (var diagnostic in exception.Diagnostics)
            {
                Console.Error.WriteLine($"diagnostic {diagnostic.Code} package-workflow {diagnostic.Message}");
            }

            return 1;
        }
    }

    private static GameInstallation FindInstallationOrExit()
    {
        var installation = GameInstallation.FindAll([GeneralsDefinition.Instance]).FirstOrDefault();
        if (installation is null)
        {
            Console.Error.WriteLine(
                "No Generals installation found; set CNC_GENERALS_PATH for headless captures.");
            Environment.Exit(1);
        }

        return installation!;
    }

    private static bool IsPackageAddressed(string path)
    {
        if (Directory.Exists(path))
        {
            return File.Exists(Path.Combine(path, MapPackageFormat.ManifestFileName));
        }

        return File.Exists(path) &&
            Path.GetFileName(path).Equals(MapPackageFormat.ManifestFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static void CaptureRenderStage(
        GameInstallation installation,
        string mapArgument,
        HeightMapData framingHeightMap,
        string stillPath,
        string? framesDirectory,
        int frameCount)
    {
        using var game = new Game(
            installation,
            null,
            new Configuration
            {
                NoAudio = true,
                SkipStartingBuildings = true,
            },
            null!);

        // The runtime's own load path: Game.LoadMap accepts an H3 package directory
        // (directory-package branch) or a user-data-relative legacy .map for materialized
        // editor state.
        game.StartSinglePlayerGame(mapArgument);
        FrameCameraOnMap(game, framingHeightMap);
        game.StartRun();

        for (var i = 0; i < WarmupFrameCount; i++)
        {
            RenderOne(game);
        }

        if (framesDirectory is not null)
        {
            Directory.CreateDirectory(framesDirectory);

            for (var i = 0; i < frameCount; i++)
            {
                RenderOne(game);
                CaptureFrame(game, Path.Combine(framesDirectory, $"frame_{i:D4}.png"));
            }
        }

        RenderOne(game);
        CaptureFrame(game, stillPath);
        Console.WriteLine(
            $"Wrote {stillPath}" +
            (framesDirectory is null ? string.Empty : $" (+{frameCount} frames in {framesDirectory})"));
    }

    private static void RenderOne(Game game)
    {
        game.Update(Array.Empty<InputMessage>());
        game.Panel.EnsureFrame(new Rectangle(0, 0, RenderWidth, RenderHeight));
        game.Render();
    }

    private static void CaptureFrame(Game game, string path)
    {
        FrameCapturer.CaptureToPng(game.GraphicsDevice!, game.Panel.Framebuffer.ColorTargets[0].Target, path);
    }

    private static void FrameCameraOnMap(Game game, HeightMapData heightMap)
    {
        var centerX = heightMap.Width * HeightMap.HorizontalScale / 2f;
        var centerY = heightMap.Height * HeightMap.HorizontalScale / 2f;
        game.Scene3D!.TacticalView.LookAt(new Vector3(centerX, centerY, 0f));
        game.Scene3D.TacticalView.InitHeightForMap();
        game.Scene3D.TacticalView.SetAngleAndPitchToDefault();
        game.Scene3D.TacticalView.SetZoomToDefault();
    }

    private static string BuildEqualityReport(EditorMap edited, EditorMap reopened)
    {
        var heightsEqual = ElevationSamplesEqual(
            edited.MapFile.HeightMapData!.Elevations,
            reopened.MapFile.HeightMapData!.Elevations);
        var objectsEqual = ObjectListsEqual(edited.MapFile.ObjectsList.Objects, reopened.MapFile.ObjectsList.Objects);
        var companionsEqual = edited.MapIniContent == reopened.MapIniContent &&
            edited.MapStrContent == reopened.MapStrContent;

        return "Reload equality: heights=" + (heightsEqual ? "OK" : "MISMATCH") +
            " objects=" + (objectsEqual ? "OK" : "MISMATCH") +
            " companions=" + (companionsEqual ? "OK" : "MISMATCH");
    }

    private static bool ElevationSamplesEqual(ushort[,] expected, ushort[,] actual)
    {
        if (expected.GetLength(0) != actual.GetLength(0) || expected.GetLength(1) != actual.GetLength(1))
        {
            return false;
        }

        for (var y = 0; y < expected.GetLength(1); y++)
        {
            for (var x = 0; x < expected.GetLength(0); x++)
            {
                if (expected[x, y] != actual[x, y])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static int CountChangedSamples(ushort[,] before, ushort[,] after)
    {
        var count = 0;

        for (var y = 0; y < before.GetLength(1); y++)
        {
            for (var x = 0; x < before.GetLength(0); x++)
            {
                if (before[x, y] != after[x, y])
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool ObjectListsEqual(MapObject[] edited, MapObject[] reopened)
    {
        if (edited.Length != reopened.Length)
        {
            return false;
        }

        for (var index = 0; index < edited.Length; index++)
        {
            if (!string.Equals(edited[index].TypeName, reopened[index].TypeName, StringComparison.Ordinal) ||
                edited[index].Position != reopened[index].Position ||
                edited[index].Angle != reopened[index].Angle ||
                edited[index].RoadType != reopened[index].RoadType)
            {
                return false;
            }
        }

        return true;
    }

    private static Vector3 OffsetWorld(HeightMapData heightMapData, int offsetX, int offsetY)
    {
        var cellX = heightMapData.Elevations.GetLength(0) / 2 + offsetX;
        var cellY = heightMapData.Elevations.GetLength(1) / 2 + offsetY;
        var border = (int)heightMapData.BorderWidth;
        return new Vector3((cellX - border) * HeightMap.HorizontalScale + 5f, (cellY - border) * HeightMap.HorizontalScale + 5f, 0f);
    }

    private static ViewportPointerEvent PointerEvent(ViewportPointerEventType eventType, Vector3 worldPosition) => new()
    {
        Position = new Vector2(worldPosition.X, worldPosition.Y),
        EventType = eventType,
        Button = ViewportMouseButton.Left,
        WorldPosition = worldPosition,
    };
}
