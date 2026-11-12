#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Data.Map;
using Panopticon.Graphics;
using Panopticon.Graphics.Rendering.Instancing;
using Panopticon.Terrain;
using Panopticon.Tests.Data;
using Panopticon.Tools.WorldBuilder.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Terrain;

/// <summary>
/// B4a-G17: the WorldBuilder editor viewport draws repeated map objects through the landed
/// B5 GPU instancing. Headless real runtime (offscreen Game boot through GameViewportRenderer),
/// not interactive UI. B5 instancing is automatic for eligible repeated mesh parts when
/// RenderScaleOptions.Instancing is true (its default); this test proves the editor path uses
/// it via coverage plus the RenderStatistics readback seam, not a renderer rewrite.
/// Post-load EditorMap edits do not rebuild the booted scene; rendering assertions use the
/// initially loaded population. No selection/hover overlay exists in editor rendering (verified
/// by source inspection); selection identity is asserted at core level only.
/// </summary>
[Collection("SerialGameTests")]
public sealed class EditorInstancingInstalledTests
{
    private const int ObjectCount = 20000;

    private const string RepeatedTypeName = "AmericaTankCrusader";

    private const int PlayableSize = 400;

    private const int BorderWidth = 6;

    private const int LatticeSize = PlayableSize + (2 * BorderWidth) + 1;

    private const float Spacing = 20f;

    private const int Columns = 142;

    private const uint ViewportWidth = 1280;

    private const uint ViewportHeight = 800;

    private const float CameraHeight = 3000f;

    internal const string ArtifactRootVariable = "PANOPTICON_EDITOR_INSTANCING_ARTIFACT_ROOT";

    internal const string CaptureRootVariable = "PANOPTICON_EDITOR_INSTANCING_CAPTURE_ROOT";

    private readonly ITestOutputHelper _output;

    public EditorInstancingInstalledTests(ITestOutputHelper output)
    {
        _output = output;
    }

    internal static string ResolveArtifactDirectory(string runStamp)
    {
        var configuredRoot = System.Environment.GetEnvironmentVariable(ArtifactRootVariable);
        return string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(Path.GetTempPath(), $"b4a-g17-validation-{runStamp}-{Guid.NewGuid():N}")
            : Path.Combine(configuredRoot, $"b4a-g17-validation-{runStamp}");
    }

    internal static string? ResolveCaptureRoot()
    {
        var configuredRoot = System.Environment.GetEnvironmentVariable(CaptureRootVariable);
        return string.IsNullOrWhiteSpace(configuredRoot) ? null : configuredRoot;
    }

    [GameFact(SageGame.CncGenerals)]
    public void EditorViewport_DrawsRepeatedObjects_ThroughB5Instancing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"editor-instancing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var runStamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var logDir = ResolveArtifactDirectory(runStamp);
        Directory.CreateDirectory(logDir);
        MarkPhase(logDir, "start");

        var originalInstancing = RenderScaleOptions.Instancing;
        var originalCulling = RenderScaleOptions.SmallFeatureCulling;

        GameViewportRenderer? renderer = null;

        try
        {
            RenderScaleOptions.Instancing = true;
            RenderScaleOptions.SmallFeatureCulling = true;

            var swBuild = Stopwatch.StartNew();
            var packagePath = CreatePopulationPackage(tempDir);
            swBuild.Stop();
            _output.WriteLine($"Built {ObjectCount} '{RepeatedTypeName}' package at '{packagePath}' in {swBuild.ElapsedMilliseconds} ms.");
            MarkPhase(logDir, "package built");

            var editorMap = EditorMap.OpenPackage(packagePath);
            var crusaderCount = editorMap.MapFile.ObjectsList.Objects.Count(o => o.TypeName == RepeatedTypeName);
            Assert.Equal(ObjectCount, crusaderCount);
            Assert.Equal(ObjectCount + 2, editorMap.MapFile.ObjectsList.Objects.Length);
            _output.WriteLine($"Opened package: total {editorMap.MapFile.ObjectsList.Objects.Length} (2 waypoints + {crusaderCount} '{RepeatedTypeName}').");

            var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
            renderer = new GameViewportRenderer { Installation = installation };
            string? loadStatus = null;
            renderer.LoadStatus += s => loadStatus = s;
            renderer.Initialize(ViewportWidth, ViewportHeight);
            Assert.True(renderer.IsInitialized);
            Assert.NotNull(renderer.RenderStatistics);
            MarkPhase(logDir, "renderer initialized");

            renderer.LoadMap(editorMap);
            MarkPhase(logDir, $"map loaded: {loadStatus}");
            _output.WriteLine($"LoadStatus: {loadStatus}");
            Assert.NotNull(loadStatus);
            Assert.Contains("3D preview:", loadStatus);

            var game = GetBootedGame(renderer);
            Assert.NotNull(game.Scene3D);

            var gameObjects = game.Scene3D.GameObjects.Objects.ToArray();
            var gameCrusaders = gameObjects.Count(o => o.Definition.Name == RepeatedTypeName);
            _output.WriteLine($"Booted scene: total GameObjects {gameObjects.Length}, '{RepeatedTypeName}' {gameCrusaders}.");
            Assert.Equal(ObjectCount, gameCrusaders);

            MarkPhase(logDir, $"scene ready: {gameCrusaders} '{RepeatedTypeName}'");
            var view = game.Scene3D.TacticalView;
            var cameraTargets = new Vector3[]
            {
                new(2000f, 2000f, 0f),
                new(1500f, 1500f, 0f),
                new(2500f, 2500f, 0f),
                new(2000f, 1500f, 0f),
            };

            var frameFiles = new string[cameraTargets.Length];
            var instancedStats = new string[cameraTargets.Length];

            for (var i = 0; i < cameraTargets.Length; i++)
            {
                var target = cameraTargets[i];
                view.LookAt(target);
                view.SnapHeightAboveGround(CameraHeight);

                for (var settle = 0; settle < 3; settle++)
                {
                    renderer.RenderFrame();
                }

                var stats = renderer.RenderStatistics;
                Assert.NotNull(stats);
                instancedStats[i] = stats.ToString();
                MarkPhase(logDir, $"frame {i} stats: {stats}");
                _output.WriteLine($"Frame {i} LookAt ({target.X},{target.Y}) height {CameraHeight}: {stats}");

                if (i == 0)
                {
                    Assert.True(stats.InstancedBatches >= 1, $"Expected InstancedBatches >= 1, got {stats.InstancedBatches}. Stats: {stats}");
                    Assert.True(stats.InstancedDrawCalls >= 1, $"Expected InstancedDrawCalls >= 1, got {stats.InstancedDrawCalls}. Stats: {stats}");
                    Assert.True(stats.InstancesDrawn >= 10000, $"Expected InstancesDrawn >= 10000, got {stats.InstancesDrawn}. Stats: {stats}");
                    Assert.True(
                        (long)stats.DrawCalls * 10 < stats.InstancesDrawn,
                        $"Expected DrawCalls ({stats.DrawCalls}) at least 10x below InstancesDrawn ({stats.InstancesDrawn}). Stats: {stats}");
                }
                else
                {
                    Assert.True(stats.InstancedBatches >= 1, $"Frame {i}: expected InstancedBatches >= 1, got {stats.InstancedBatches}.");
                    Assert.True(stats.InstancesDrawn >= 5000, $"Frame {i}: expected InstancesDrawn >= 5000, got {stats.InstancesDrawn}.");
                }

                var framePath = Path.Combine(logDir, $"frame_{i:D4}.png");
                Assert.NotNull(renderer.GraphicsDevice);
                Assert.NotNull(renderer.FrameTexture);
                FrameCapturer.CaptureToPng(renderer.GraphicsDevice, renderer.FrameTexture, framePath);
                Assert.True(File.Exists(framePath), $"Captured frame {framePath} does not exist.");
                frameFiles[i] = framePath;
            }

            _output.WriteLine($"Instanced (on) centre stats: {instancedStats[0]}");

            RenderScaleOptions.Instancing = false;
            try
            {
                view.LookAt(cameraTargets[0]);
                view.SnapHeightAboveGround(CameraHeight);
                renderer.RenderFrame();
                renderer.RenderFrame();

                var baseline = renderer.RenderStatistics;
                Assert.NotNull(baseline);
                _output.WriteLine($"Baseline (instancing off) centre stats: {baseline}");
                MarkPhase(logDir, $"baseline stats: {baseline}");
                Assert.Equal(0, baseline.InstancedDrawCalls);
                Assert.Equal(0, baseline.InstancedBatches);
                Assert.Equal(0, baseline.InstancesDrawn);
                _output.WriteLine($"Contrast: instanced draws vs baseline draws recorded above; baseline InstancedDrawCalls==0 as required.");
            }
            finally
            {
                RenderScaleOptions.Instancing = true;
            }

            VerifySelectionIdentityAtCoreLevel(editorMap);
            MarkPhase(logDir, "selection identity verified");

            var gifPath = Path.Combine(logDir, "editor-instancing-camera-sweep.gif");
            VerifyFramesAndAssembleGif(frameFiles, gifPath);
            MarkPhase(logDir, "gif assembled");
            var captureRoot = ResolveCaptureRoot();
            if (captureRoot is null)
            {
                MarkPhase(logDir, $"captures not published: {CaptureRootVariable} unset; captures remain in artifact root");
                _output.WriteLine($"Shared capture publication skipped: {CaptureRootVariable} unset; captures remain in artifact root.");
            }
            else
            {
                PublishCaptures(captureRoot, gifPath, frameFiles);
                MarkPhase(logDir, "captures published");
            }

            _output.WriteLine($"Evidence (headless real runtime, not interactive UI):");
            foreach (var file in frameFiles)
            {
                _output.WriteLine($"  {file}");
            }

            _output.WriteLine($"  {gifPath}");
        }
        finally
        {
            RenderScaleOptions.Instancing = originalInstancing;
            RenderScaleOptions.SmallFeatureCulling = originalCulling;
            MarkPhase(logDir, "renderer dispose start");
            renderer?.Dispose();
            MarkPhase(logDir, "renderer dispose done");

            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }

            MarkPhase(logDir, "test method end");
        }
    }

    private static void MarkPhase(string logDir, string message)
    {
        var line = $"{DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture)} {message}";
        File.AppendAllText(Path.Combine(logDir, "phases.log"), line + System.Environment.NewLine);
    }

    private static bool CaptureVersionTaken(string captureRoot, string stem)
    {
        return File.Exists(Path.Combine(captureRoot, stem + ".gif"))
            || File.Exists(Path.Combine(captureRoot, stem + ".png"))
            || Directory.Exists(Path.Combine(captureRoot, "frames", stem));
    }

    private void PublishCaptures(string captureRoot, string gifPath, string[] frameFiles)
    {
        Assert.True(Directory.Exists(captureRoot), $"Shared capture root '{captureRoot}' is not available.");

        var version = 1;
        while (CaptureVersionTaken(captureRoot, $"b4a-g17-editor-instancing-v{version}"))
        {
            version++;
        }

        var stem = $"b4a-g17-editor-instancing-v{version}";
        var gifTarget = Path.Combine(captureRoot, stem + ".gif");
        var stillTarget = Path.Combine(captureRoot, stem + ".png");
        var framesDir = Path.Combine(captureRoot, "frames", stem);

        File.Copy(gifPath, gifTarget, overwrite: false);
        File.Copy(frameFiles[0], stillTarget, overwrite: false);
        Directory.CreateDirectory(framesDir);
        foreach (var frame in frameFiles)
        {
            File.Copy(frame, Path.Combine(framesDir, Path.GetFileName(frame)), overwrite: false);
        }

        _output.WriteLine($"Shared capture GIF: {gifTarget} ({new FileInfo(gifTarget).Length} bytes)");
        _output.WriteLine($"Shared capture still: {stillTarget} ({new FileInfo(stillTarget).Length} bytes)");
        _output.WriteLine($"Shared capture frames dir: {framesDir}");
    }

    private static Game GetBootedGame(GameViewportRenderer renderer)
    {
        var field = typeof(GameViewportRenderer).GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        var game = (Game?)field.GetValue(renderer);
        Assert.NotNull(game);
        return game;
    }

    private string CreatePopulationPackage(string tempDir)
    {
        var packagePath = Path.Combine(tempDir, "instancing-20000-package");
        var heights = new ushort[LatticeSize, LatticeSize];
        for (var y = 0; y < LatticeSize; y++)
        {
            var fy = y * 0.05f;
            for (var x = 0; x < LatticeSize; x++)
            {
                var fx = x * 0.05f;
                var h = 2500f + 300f * MathF.Sin(fx) * MathF.Cos(fy) + 100f * MathF.Sin(fx * 2.3f + fy * 1.7f);
                heights[x, y] = (ushort)Math.Clamp((int)h, 100, 60000);
            }
        }

        var mapFile = MapFile.CreateFromHeights(heights, borderWidth: BorderWidth);
        Assert.Equal(2, mapFile.ObjectsList.Objects.Length);

        var worldWidth = PlayableSize * HeightMap.HorizontalScale;
        var rows = (ObjectCount + Columns - 1) / Columns;
        var spanX = Columns * Spacing;
        var spanY = rows * Spacing;
        var originX = (worldWidth - spanX) / 2f;
        var originY = (worldWidth - spanY) / 2f;

        var batch = new MapObject[ObjectCount];
        for (var i = 0; i < ObjectCount; i++)
        {
            var col = i % Columns;
            var row = i / Columns;
            var x = originX + col * Spacing;
            var y = originY + row * Spacing;
            batch[i] = new MapObject(new Vector3(x, y, 0f), 0f, RoadType.None, RepeatedTypeName);
        }

        var buildMap = EditorMap.CreateNew(mapFile);
        var bulkEdit = BulkObjectPlacementEdit.CreatePlacement(buildMap.MapFile.ObjectsList, batch);
        buildMap.UndoStack.Push(bulkEdit);
        Assert.Equal(ObjectCount + 2, buildMap.MapFile.ObjectsList.Objects.Length);
        for (var i = 0; i < ObjectCount; i++)
        {
            Assert.Same(batch[i], buildMap.MapFile.ObjectsList.Objects[2 + i]);
        }

        var interchange = MapPackageConverter.ToInterchange(buildMap.MapFile);
        var writeResult = LegacyMapPackageImporter.Write(interchange, packagePath);
        Assert.True(writeResult.Success, string.Join("; ", writeResult.Diagnostics));
        return packagePath;
    }

    private void VerifySelectionIdentityAtCoreLevel(EditorMap editorMap)
    {
        var objectsList = editorMap.MapFile.ObjectsList;
        Assert.NotNull(objectsList.Objects);
        _output.WriteLine($"Selection identity: no selection/hover overlay exists in editor rendering; asserting core-level identity only.");
        _output.WriteLine($"Selection identity runs on the EditorMap after boot; it does not rebuild the booted scene.");

        var worldWidth = PlayableSize * HeightMap.HorizontalScale;
        var rows = (ObjectCount + Columns - 1) / Columns;
        var spanX = Columns * Spacing;
        var spanY = rows * Spacing;
        var originX = (worldWidth - spanX) / 2f;
        var originY = (worldWidth - spanY) / 2f;

        var min = new Vector2(originX - 1f, originY - 1f);
        var max = new Vector2(originX + 9 * Spacing + 1f, originY + 9 * Spacing + 1f);
        var expected = SelectTool.QueryInRectangle(objectsList, min, max);
        Assert.Equal(100, expected.Count);
        Assert.All(expected, o => Assert.Equal(RepeatedTypeName, o.TypeName));

        var tool = new SelectTool();
        tool.OnPointerDown(
            new ViewportPointerEvent
            {
                Position = new Vector2(0, 0),
                EventType = ViewportPointerEventType.Down,
                Button = ViewportMouseButton.Left,
                WorldPosition = new Vector3(min.X, min.Y, 0f),
            },
            editorMap);
        tool.OnPointerMove(
            new ViewportPointerEvent
            {
                Position = new Vector2(200, 200),
                EventType = ViewportPointerEventType.Move,
                Button = ViewportMouseButton.Left,
                WorldPosition = new Vector3(max.X, max.Y, 0f),
            },
            editorMap);
        tool.OnPointerUp(
            new ViewportPointerEvent
            {
                Position = new Vector2(200, 200),
                EventType = ViewportPointerEventType.Up,
                Button = ViewportMouseButton.Left,
                WorldPosition = new Vector3(max.X, max.Y, 0f),
            },
            editorMap);

        Assert.Equal(100, tool.SelectedObjects.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Same(expected[i], tool.SelectedObjects[i]);
        }

        editorMap.UndoStack.Clear();
        var beforePositions = new Dictionary<MapObject, Vector3>(tool.SelectedObjects.Count);
        foreach (var obj in tool.SelectedObjects)
        {
            beforePositions[obj] = obj.Position;
        }

        var delta = new Vector3(10f, 20f, 0f);
        tool.MoveSelectionBy(editorMap, delta);
        Assert.True(editorMap.UndoStack.CanUndo);
        Assert.False(editorMap.UndoStack.CanRedo);

        foreach (var obj in tool.SelectedObjects)
        {
            Assert.Equal(beforePositions[obj] + delta, obj.Position);
            Assert.Contains(obj, objectsList.Objects);
        }

        editorMap.UndoStack.Undo();
        Assert.False(editorMap.UndoStack.CanUndo);
        Assert.True(editorMap.UndoStack.CanRedo);
        foreach (var obj in tool.SelectedObjects)
        {
            Assert.Equal(beforePositions[obj], obj.Position);
            Assert.Contains(obj, objectsList.Objects);
        }

        editorMap.UndoStack.Redo();
        Assert.True(editorMap.UndoStack.CanUndo);
        Assert.False(editorMap.UndoStack.CanRedo);
        foreach (var obj in tool.SelectedObjects)
        {
            Assert.Equal(beforePositions[obj] + delta, obj.Position);
        }

        editorMap.UndoStack.Undo();
        foreach (var obj in tool.SelectedObjects)
        {
            Assert.Equal(beforePositions[obj], obj.Position);
        }

        editorMap.UndoStack.Clear();
        var preDeleteSnapshot = objectsList.Objects;
        var selectedForDelete = tool.SelectedObjects.ToArray();
        Assert.Equal(100, selectedForDelete.Length);

        tool.DeleteSelection(editorMap);
        Assert.Empty(tool.SelectedObjects);
        Assert.Equal(preDeleteSnapshot.Length - selectedForDelete.Length, objectsList.Objects.Length);
        foreach (var obj in selectedForDelete)
        {
            Assert.DoesNotContain(obj, objectsList.Objects);
        }

        editorMap.UndoStack.Undo();
        Assert.Equal(preDeleteSnapshot.Length, objectsList.Objects.Length);
        Assert.Same(preDeleteSnapshot, objectsList.Objects);
        for (var i = 0; i < preDeleteSnapshot.Length; i++)
        {
            Assert.Same(preDeleteSnapshot[i], objectsList.Objects[i]);
        }

        editorMap.UndoStack.Redo();
        Assert.Equal(preDeleteSnapshot.Length - selectedForDelete.Length, objectsList.Objects.Length);

        editorMap.UndoStack.Undo();
        Assert.Same(preDeleteSnapshot, objectsList.Objects);

        _output.WriteLine($"Selection identity: 100-object move/delete each cycled as a single undo entry with exact positions and identical reference order.");
    }

    private void VerifyFramesAndAssembleGif(string[] frameFiles, string gifPath)
    {
        for (var f = 0; f < frameFiles.Length; f++)
        {
            var framePath = frameFiles[f];
            Assert.True(File.Exists(framePath), $"Captured frame {framePath} does not exist.");

            using var image = Image.Load<Rgba32>(framePath);
            Assert.True(image.Width > 0 && image.Height > 0);

            var distinctColors = new HashSet<Rgba32>();
            var sumR = 0.0;
            var sumG = 0.0;
            var sumB = 0.0;
            var sampledPixels = 0;

            for (var y = 0; y < image.Height; y += 8)
            {
                for (var x = 0; x < image.Width; x += 8)
                {
                    var pixel = image[x, y];
                    distinctColors.Add(pixel);
                    sumR += pixel.R;
                    sumG += pixel.G;
                    sumB += pixel.B;
                    sampledPixels++;
                }
            }

            var meanR = sumR / sampledPixels;
            var meanG = sumG / sampledPixels;
            var meanB = sumB / sampledPixels;

            Assert.True(meanR > 0 || meanG > 0 || meanB > 0, $"Frame {f} is completely black!");
            Assert.True(distinctColors.Count > 10, $"Frame {f} is uniform! Distinct sampled colours: {distinctColors.Count}");

            _output.WriteLine($"Frame {f} ({Path.GetFileName(framePath)}): size {image.Width}x{image.Height}, sampled {sampledPixels} pixels, distinct colours {distinctColors.Count}, mean RGB=({meanR:F1}, {meanG:F1}, {meanB:F1})");
        }

        using (var gif = Image.Load<Rgba32>(frameFiles[0]))
        {
            for (var f = 1; f < frameFiles.Length; f++)
            {
                using var nextFrame = Image.Load<Rgba32>(frameFiles[f]);
                gif.Frames.AddFrame(nextFrame.Frames.RootFrame);
            }

            gif.SaveAsGif(gifPath);
        }

        Assert.True(File.Exists(gifPath), $"Animated GIF '{gifPath}' was not created.");
        var gifInfo = new FileInfo(gifPath);
        Assert.True(gifInfo.Length > 0, $"Animated GIF '{gifPath}' is empty.");
        _output.WriteLine($"Animated GIF assembled: {gifPath} ({gifInfo.Length / 1024} KB)");
    }
}
