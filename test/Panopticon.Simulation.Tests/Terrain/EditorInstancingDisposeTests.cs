#nullable enable

using System;
using System.Diagnostics;
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
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Terrain;

/// <summary>
/// B4a-G17 teardown regression: disposing an editor viewport that drew a repeated population
/// must terminate and be idempotent. The duration bound guards termination only, not performance.
/// Headless real runtime through GameViewportRenderer.
/// </summary>
[Collection("SerialGameTests")]
public sealed class EditorInstancingDisposeTests
{
    private const int RepeatedObjectCount = 1000;

    private const int SmallObjectCount = 5;

    private const string TypeName = "AmericaTankCrusader";

    private const int PlayableSize = 400;

    private const int BorderWidth = 6;

    private const int LatticeSize = PlayableSize + (2 * BorderWidth) + 1;

    private const float Spacing = 20f;

    private const int Columns = 142;

    private const uint ViewportWidth = 1280;

    private const uint ViewportHeight = 800;

    private const float CameraHeight = 3000f;

    private static readonly TimeSpan DisposeBound = TimeSpan.FromSeconds(120);

    private readonly ITestOutputHelper _output;

    public EditorInstancingDisposeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [GameFact(SageGame.CncGenerals)]
    public void EditorInstancingDispose_RepeatedPopulation_TeardownTerminatesAndIsIdempotent()
    {
        var tempDir = CreateTempDir();
        var originalInstancing = RenderScaleOptions.Instancing;
        var originalCulling = RenderScaleOptions.SmallFeatureCulling;
        GameViewportRenderer? renderer = null;

        try
        {
            RenderScaleOptions.Instancing = true;
            RenderScaleOptions.SmallFeatureCulling = true;

            var editorMap = EditorMap.OpenPackage(CreatePopulationPackage(tempDir, RepeatedObjectCount));
            renderer = new GameViewportRenderer { Installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals) };
            BootRenderer(renderer, editorMap);

            var game = GetBootedGame(renderer);
            var scene = game.Scene3D;
            Assert.NotNull(scene);
            Assert.Equal(RepeatedObjectCount, scene.GameObjects.Objects.Count(o => o.Definition.Name == TypeName));

            RenderCentreFrames(renderer, game);
            var stats = renderer.RenderStatistics;
            Assert.NotNull(stats);
            _output.WriteLine($"Repeated population stats: {stats}");
            Assert.True(stats.InstancedDrawCalls > 0, $"Expected InstancedDrawCalls > 0, got {stats.InstancedDrawCalls}. Stats: {stats}");

            DisposeWithinBound(renderer);
        }
        finally
        {
            RenderScaleOptions.Instancing = originalInstancing;
            RenderScaleOptions.SmallFeatureCulling = originalCulling;
            renderer?.Dispose();
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void EditorInstancingDispose_SmallPopulation_RendersAndDisposesNormally()
    {
        var tempDir = CreateTempDir();
        GameViewportRenderer? renderer = null;

        try
        {
            var editorMap = EditorMap.OpenPackage(CreatePopulationPackage(tempDir, SmallObjectCount));
            renderer = new GameViewportRenderer { Installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals) };
            BootRenderer(renderer, editorMap);

            var game = GetBootedGame(renderer);
            var scene = game.Scene3D;
            Assert.NotNull(scene);
            Assert.Equal(SmallObjectCount, scene.GameObjects.Objects.Count(o => o.Definition.Name == TypeName));

            RenderCentreFrames(renderer, game);
            var stats = renderer.RenderStatistics;
            Assert.NotNull(stats);
            _output.WriteLine($"Small population stats: {stats}");
            Assert.True(stats.DrawCalls > 0, $"Expected DrawCalls > 0, got {stats.DrawCalls}. Stats: {stats}");

            DisposeWithinBound(renderer);
        }
        finally
        {
            renderer?.Dispose();
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"editor-dispose-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private void BootRenderer(GameViewportRenderer renderer, EditorMap editorMap)
    {
        string? loadStatus = null;
        renderer.LoadStatus += status => loadStatus = status;
        renderer.Initialize(ViewportWidth, ViewportHeight);
        Assert.True(renderer.IsInitialized);

        renderer.LoadMap(editorMap);
        _output.WriteLine($"LoadStatus: {loadStatus}");
        Assert.NotNull(loadStatus);
        Assert.Contains("3D preview:", loadStatus);
    }

    private static void RenderCentreFrames(GameViewportRenderer renderer, Game game)
    {
        var view = game.Scene3D!.TacticalView;
        var worldWidth = PlayableSize * HeightMap.HorizontalScale;
        view.LookAt(new Vector3(worldWidth / 2f, worldWidth / 2f, 0f));
        view.SnapHeightAboveGround(CameraHeight);

        for (var settle = 0; settle < 3; settle++)
        {
            renderer.RenderFrame();
        }
    }

    private void DisposeWithinBound(GameViewportRenderer renderer)
    {
        var stopwatch = Stopwatch.StartNew();
        renderer.Dispose();
        stopwatch.Stop();
        _output.WriteLine($"Dispose duration: {stopwatch.ElapsedMilliseconds} ms (bound {DisposeBound.TotalSeconds} s).");
        Assert.True(stopwatch.Elapsed < DisposeBound, $"Dispose took {stopwatch.Elapsed}, exceeding the {DisposeBound} bound.");

        renderer.Dispose();
    }

    private static Game GetBootedGame(GameViewportRenderer renderer)
    {
        var field = typeof(GameViewportRenderer).GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        var game = (Game?)field.GetValue(renderer);
        Assert.NotNull(game);
        return game;
    }

    private static string CreatePopulationPackage(string tempDir, int objectCount)
    {
        var packagePath = Path.Combine(tempDir, $"dispose-{objectCount}-package");
        var mapFile = MapFile.CreateFromHeights(CreateHeights(), borderWidth: BorderWidth);

        var worldWidth = PlayableSize * HeightMap.HorizontalScale;
        var columns = Math.Min(Columns, objectCount);
        var rows = (objectCount + columns - 1) / columns;
        var originX = (worldWidth - columns * Spacing) / 2f;
        var originY = (worldWidth - rows * Spacing) / 2f;

        var batch = new MapObject[objectCount];
        for (var i = 0; i < objectCount; i++)
        {
            var col = i % columns;
            var row = i / columns;
            batch[i] = new MapObject(new Vector3(originX + col * Spacing, originY + row * Spacing, 0f), 0f, RoadType.None, TypeName);
        }

        var buildMap = EditorMap.CreateNew(mapFile);
        buildMap.UndoStack.Push(BulkObjectPlacementEdit.CreatePlacement(buildMap.MapFile.ObjectsList, batch));

        var interchange = MapPackageConverter.ToInterchange(buildMap.MapFile);
        var writeResult = LegacyMapPackageImporter.Write(interchange, packagePath);
        Assert.True(writeResult.Success, string.Join("; ", writeResult.Diagnostics));
        return packagePath;
    }

    private static ushort[,] CreateHeights()
    {
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

        return heights;
    }
}
