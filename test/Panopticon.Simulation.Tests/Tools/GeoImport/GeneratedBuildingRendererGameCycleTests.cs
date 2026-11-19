using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Projection;
using Panopticon.Graphics.Rendering;
using Panopticon.Mathematics;
using Panopticon.Tests.Data;
using Panopticon.Tools.GeoImport;
using Xunit;
using RenderItem = Panopticon.Graphics.Rendering.RenderItem;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// Real Game.LoadMap lifecycle for GeneratedBuildingRenderer on a synthetic generated
/// package (GeoImport CLI, no test hooks in the load path): A(buildings) -> B(same payload,
/// different map directory) -> none(cleabs stripped) -> A again, then Game.Dispose. Frame
/// items and Veldrid buffer disposal are observed through the real RenderPipeline delegate;
/// the only reflection use is reading Game's current renderer field.
/// </summary>
[Collection("SerialGameTests")]
public sealed class GeneratedBuildingRendererGameCycleTests
{
    [GameFact(Panopticon.SageGame.CncGenerals)]
    public void LoadMapCycleReplacesTheBuildingRendererAndGameDisposeFreesTheCurrentOne()
    {
        using var fixture = CycleMapFixture.Create();
        var scratchRoot = NewScratchRoot();

        try
        {
            var payloadBuildings = Path.Combine(scratchRoot, "payload-buildings");
            var payloadNone = Path.Combine(scratchRoot, "payload-none");
            RunCli(["import", "--manifest", fixture.ManifestPath, "--out", payloadBuildings]);
            RunCli(["import", "--manifest", fixture.ManifestWithoutCleabsPath, "--out", payloadNone]);

            var texturesPath = Path.Combine(scratchRoot, "textures.json");
            File.WriteAllText(
                texturesPath,
                "{\"grass\":\"GrassType1\",\"paved\":\"AsphaltType1\"}");

            var packageA = Package(payloadBuildings, scratchRoot, "package-a", texturesPath);
            var packageB = Package(payloadBuildings, scratchRoot, "package-b", texturesPath);
            var packageNone = Package(payloadNone, scratchRoot, "package-none", texturesPath);
            var packageA2 = Package(payloadBuildings, scratchRoot, "package-a2", texturesPath);

            var expectedSegments = SegmentCount(packageA);
            Assert.Equal(2, new PackageReader(packageA).Read().Buildings!.Buildings.Count);
            Assert.Equal(expectedSegments, SegmentCount(packageB));
            Assert.Null(new PackageReader(packageNone).Read().Buildings);

            var userRoot = Path.Combine(scratchRoot, "user");
            using var game = NewGame(userRoot);
            game.Panel.EnsureFrame(new Rectangle(0, 0, 320, 240));

            game.LoadMap(packageA, [], GameType.SinglePlayer);
            var rendererA = CurrentRenderer(game);
            Assert.NotNull(rendererA);
            Assert.NotNull(game.TerrainLogic.HeightMap);
            var frameA = RenderFrame(game);
            Assert.Equal(expectedSegments, frameA.Opaque.Count);
            Assert.Equal(expectedSegments, frameA.Shadow.Count);
            var buffersA = IndexBuffers(frameA);

            game.LoadMap(packageB, [], GameType.SinglePlayer);
            var rendererB = CurrentRenderer(game);
            Assert.NotNull(rendererB);
            Assert.NotSame(rendererA, rendererB);
            var frameB = RenderFrame(game);
            Assert.Equal(expectedSegments, frameB.Opaque.Count);
            var buffersB = IndexBuffers(frameB);

            // Exactly the new renderer publishes: none of the disposed A buffers reappear,
            // and the A buffers are destroyed on the device.
            Assert.Empty(buffersA.Intersect(buffersB));
            Assert.All(buffersA, buffer => Assert.True(
                buffer.IsDisposed,
                "Old renderer buffers must be disposed after the next LoadMap."));

            game.LoadMap(packageNone, [], GameType.SinglePlayer);
            Assert.Null(CurrentRenderer(game));
            var frameNone = RenderFrame(game);
            Assert.Empty(frameNone.Opaque);
            Assert.Empty(frameNone.Shadow);
            Assert.All(buffersB, buffer => Assert.True(
                buffer.IsDisposed,
                "B renderer buffers must be disposed after the buildings-free LoadMap."));

            game.LoadMap(packageA2, [], GameType.SinglePlayer);
            Assert.NotNull(CurrentRenderer(game));
            var frameA2 = RenderFrame(game);
            Assert.Equal(expectedSegments, frameA2.Opaque.Count);
            var buffersA2 = IndexBuffers(frameA2);
            Assert.NotEmpty(buffersA2);

            game.Dispose();
            Assert.All(buffersA2, buffer => Assert.True(
                buffer.IsDisposed,
                "Game.Dispose must free the current renderer's buffers."));
        }
        finally
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
    }

    private static GeneratedBuildingRenderer? CurrentRenderer(Game game) =>
        (GeneratedBuildingRenderer?)typeof(Game).GetField(
            "_generatedBuildingRenderer",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game);

    private static List<Veldrid.DeviceBuffer> IndexBuffers((List<RenderItem> Opaque, List<RenderItem> Shadow) frame) =>
        frame.Opaque.Select(item => item.IndexBuffer).ToList();

    private static (List<RenderItem> Opaque, List<RenderItem> Shadow) RenderFrame(Game game)
    {
        var opaque = new List<RenderItem>();
        var shadow = new List<RenderItem>();

        void OnBuildingRenderList(object? sender, BuildingRenderListEventArgs args)
        {
            Collect(args.RenderList.Opaque.RenderItems, opaque);
            Collect(args.RenderList.Shadow.RenderItems, shadow);
        }

        var pipeline = game.Graphics.RenderPipeline!;
        pipeline.BuildingRenderList += OnBuildingRenderList;

        try
        {
            game.Render();
        }
        finally
        {
            pipeline.BuildingRenderList -= OnBuildingRenderList;
        }

        return (opaque, shadow);

        static void Collect(Panopticon.Graphics.Rendering.RenderItemCollection items, List<RenderItem> into)
        {
            for (var i = 0; i < items.Length; i++)
            {
                into.Add(items[i]);
            }
        }
    }

    private static int SegmentCount(string packageDirectory)
    {
        var package = new PackageReader(packageDirectory).Read();
        Assert.NotNull(package.Buildings);
        return package.Buildings!.MeshChunks.Sum(chunk => chunk.Segments.Count);
    }

    private static string Package(
        string payloadDirectory,
        string scratchRoot,
        string name,
        string texturesPath)
    {
        var packageDirectory = Path.Combine(scratchRoot, name);
        RunCli(["package", "--payload", payloadDirectory, "--textures", texturesPath, "--out", packageDirectory]);
        return packageDirectory;
    }

    private static Game NewGame(string userRoot)
    {
        Directory.CreateDirectory(userRoot);

        return new Game(
            InstalledFilesTestData.GetInstallation(Panopticon.SageGame.CncGenerals),
            preferredBackend: null,
            new Configuration { NoAudio = true, SkipStartingBuildings = true, UserDataRootPath = userRoot },
            window: null);
    }

    private static string NewScratchRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "opencode", "r3-g4-render-cycle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void RunCli(string[] arguments)
    {
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exit = GeoImportCli.Run(arguments, standardOut, errorOut, CancellationToken.None);

        Assert.True(exit == 0, $"GeoImport {arguments[0]} exited {exit}: {errorOut}");
        Assert.Equal(string.Empty, errorOut.ToString());
    }
}
