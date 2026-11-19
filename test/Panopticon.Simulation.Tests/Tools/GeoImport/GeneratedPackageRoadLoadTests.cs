using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Payload;
using Panopticon.Graphics;
using Panopticon.Mathematics;
using Panopticon.Tests.Data;
using Panopticon.Tools.GeoImport;
using Xunit;
using Xunit.Sdk;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// Discovery wiring of <see cref="GameFactAttribute"/> plus an explicit skip when the
/// WP1026 road-bearing Sedan proof root is not mounted, so the MCP container reports a
/// skip instead of a failure. The proof itself runs host-local.
/// </summary>
[XunitTestCaseDiscoverer("Panopticon.Tests.GameTestDiscoverer", "Panopticon.Simulation.Tests")]
[AttributeUsage(AttributeTargets.Method)]
internal sealed class RoadsProofFactAttribute : FactAttribute
{
    internal const string ProofRootPath = "/mnt/pit2/panopticon/geo-proof/r3-g6-roads-v1";

    public readonly SageGame[] Games;

    public RoadsProofFactAttribute(SageGame game, params SageGame[] otherGames)
    {
        Games = [game, .. otherGames];

        if (!Directory.Exists(Path.Combine(ProofRootPath, "prepared")))
        {
            Skip = $"Roads proof root not mounted: {ProofRootPath} (host-local proof; MCP container does not mount operator fixtures)";
        }
    }
}

/// <summary>
/// WP1026 installed-game proof: the public GeoImport CLI imports the road-bearing Sedan
/// manifest (line corridors painted into the material chunks), packages it, and
/// <see cref="Game.LoadMap"/> loads it in the real installed game. The payload must carry
/// non-default road corridor material cells, and an offscreen native render publishes an
/// overview PNG with the visible road corridors.
/// </summary>
[Collection("SerialGameTests")]
public sealed class GeneratedPackageRoadLoadTests
{
    private const string DiagnosticPrefix = "diagnostic: ";

    private const string BlendTileDataSection = "BlendTileData";

    private const string GlobalLightingSection = "GlobalLighting";

    /// <summary>Above the 900-1500 terrain-void camera band (docs/screenshots.md).</summary>
    private const float CameraHeight = 2400f;

    /// <summary>Below the 900-1500 terrain-void camera band, for the corridor close-up.</summary>
    private const float CloseUpCameraHeight = 700f;

    /// <summary>
    /// Densest painted-corridor window of the road payload (chunk 1_0, computed from the
    /// material chunk census), in game units.
    /// </summary>
    private static readonly Vector2 CloseUpLookAt = new(1580f, 500f);

    private static readonly string[] InformationalCodes = ["GEO-PKG-008", "GEO-PKG-013", "GEO-PKG-015"];

    [RoadsProofFact(SageGame.CncGenerals)]
    public void RoadBearingSedanPayload_PaintsCorridors_ConvertsAndLoadsWithVisibleCorridors()
    {
        var proofRoot = RoadsProofFactAttribute.ProofRootPath;
        var manifestPath = Path.Combine(proofRoot, "manifest.json");
        Assert.True(File.Exists(manifestPath), $"The roads proof manifest is missing: {manifestPath}");

        // Kept after the run for evidence preservation; never deleted here.
        var scratchRoot = Path.Combine(Path.GetTempPath(), "opencode", "r3-g6-road-load", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchRoot);

        var payloadDir = Path.Combine(scratchRoot, "payload");
        var packageDir = Path.Combine(scratchRoot, "package");
        var userRoot = Path.Combine(scratchRoot, "user");
        var pngPath = Path.Combine(scratchRoot, "r3-g6-road-corridors-frame.png");

        try
        {
            RunCli(["import", "--manifest", manifestPath, "--out", payloadDir]);

            var (chunksX, chunksY, paletteNames, perIndexCounts) = MaterialIndexCensus(payloadDir);
            var nonDefault = perIndexCounts.Where(pair => pair.Index != 0).ToArray();
            var nonDefaultTotal = nonDefault.Sum(pair => pair.Cells);

            Assert.True(paletteNames.Contains("dirt"), $"the palette is missing 'dirt': {string.Join(", ", paletteNames)}");
            Assert.True(
                nonDefaultTotal > 0,
                "the road-bearing payload must carry non-default corridor material cells; "
                + $"census: {string.Join(", ", perIndexCounts.Select(pair => $"{pair.Index}:{pair.Cells}"))}");

            var texturesPath = Path.Combine(scratchRoot, "textures.json");
            File.WriteAllText(
                texturesPath,
                "{\"grass\":\"GrassType1\",\"dirt\":\"DirtMediumType1\",\"rock\":\"RockType1\",\"sand\":\"SandType1\"}");

            var packageOutput = RunCli(["package", "--payload", payloadDir, "--textures", texturesPath, "--out", packageDir]);

            var diagnosticCodes = packageOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.StartsWith(DiagnosticPrefix, StringComparison.Ordinal))
                .Select(line => line[DiagnosticPrefix.Length..].Split(':')[0])
                .ToArray();
            Assert.All(diagnosticCodes, code => Assert.Contains(code, InformationalCodes));

            var package = new PackageReader(packageDir).Read();
            Assert.Contains(package.RetainedSections, section => section.SectionName == BlendTileDataSection);
            Assert.Contains(package.RetainedSections, section => section.SectionName == GlobalLightingSection);
            Assert.NotNull(package.Layout.Chunks[0].Materials);

            using (var game = NewGame(userRoot))
            {
                game.LoadMap(packageDir, [], GameType.SinglePlayer);

                Assert.NotNull(game.TerrainLogic.HeightMap);
                Assert.NotNull(game.Scene3D.Lighting);
                Assert.NotNull(game.Scene3D.Terrain);

                // Overview camera: height first, then aim (docs/screenshots.md framing
                // contract); the Sedan grid spans chunksX x chunksY chunks.
                var view = game.Scene3D.TacticalView;
                view.InitHeightForMap();
                view.SetAngleAndPitchToDefault();
                view.SnapHeightAboveGround(CameraHeight);
                var centerX = (chunksX * ChunkLayout.IntervalsPerChunk * ChunkLayout.SampleSpacingGameUnits) / 2f;
                var centerY = (chunksY * ChunkLayout.IntervalsPerChunk * ChunkLayout.SampleSpacingGameUnits) / 2f;
                view.LookAt(new Vector3(centerX, centerY, 0f));

                for (var i = 0; i < 3; i++)
                {
                    game.Panel.EnsureFrame(new Rectangle(0, 0, 1280, 800));
                    game.Render();
                }

                var frame = FrameCapturer.Capture(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target);
                var luminance = LuminanceStats.Compute(frame.PixelData);
                Assert.True(
                    luminance.BrightFraction >= 0.005d,
                    $"Render is black: only {luminance.BrightFraction:P2} of pixels have luminance > 8 "
                    + $"(min {luminance.Min}, max {luminance.Max}, mean {luminance.Mean:F1}).");
                Assert.True(
                    luminance.StandardDeviation > 4.0d && luminance.Distinct > 32,
                    $"Render is uniform: stddev {luminance.StandardDeviation:F2}, distinct values {luminance.Distinct}.");

                FrameCapturer.CaptureToPng(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target, pngPath);
                Assert.True(new FileInfo(pngPath).Length > 0, "the capture file is empty");

                // Close-up under the fog band over the densest corridor area so the
                // painted road lines are individually visible.
                view.SnapHeightAboveGround(CloseUpCameraHeight);
                view.LookAt(new Vector3(CloseUpLookAt.X, CloseUpLookAt.Y, 0f));

                for (var i = 0; i < 3; i++)
                {
                    game.Panel.EnsureFrame(new Rectangle(0, 0, 1280, 800));
                    game.Render();
                }

                var closeUpPath = Path.Combine(scratchRoot, "r3-g6-road-corridors-closeup.png");
                FrameCapturer.CaptureToPng(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target, closeUpPath);
                Assert.True(new FileInfo(closeUpPath).Length > 0, "the close-up capture file is empty");

                PublishCapture(pngPath, "r3-g6-road-corridors-overview-v1.png");
                PublishCapture(closeUpPath, "r3-g6-road-corridors-closeup-v1.png");
            }

            // The corridor census is the acceptance artifact; print it for the run report.
            Console.WriteLine(
                "r3-g6 road corridor census: chunks "
                + $"{chunksX}x{chunksY}, non-default cells {nonDefaultTotal}, "
                + "per-index "
                + string.Join(", ", perIndexCounts.Select(pair => $"material {pair.Index}: {pair.Cells} cells"))
                + $", palette [{string.Join(", ", paletteNames)}]");
        }
        finally
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
    }

    /// <summary>Copies the capture to the proof root and publishes it top-level per docs/screenshots.md.</summary>
    private static void PublishCapture(string pngPath, string publishedName)
    {
        var proofCopy = Path.Combine(RoadsProofFactAttribute.ProofRootPath, "provenance", publishedName);
        Directory.CreateDirectory(Path.GetDirectoryName(proofCopy)!);
        File.Copy(pngPath, proofCopy, overwrite: true);

        var publishedPath = Path.Combine("/mnt/pit2/Network Storage/temp", publishedName);
        if (File.Exists(publishedPath))
        {
            // Never overwrite published evidence; a rerun publishes the next version.
            Console.WriteLine($"published capture already exists, kept: {publishedPath}");
            return;
        }

        File.Copy(pngPath, publishedPath);
        Console.WriteLine($"published capture: {publishedPath} ({new FileInfo(publishedPath).Length} bytes)");
    }

    /// <summary>Counts every material index across all payload material chunks.</summary>
    private static (int ChunksX, int ChunksY, string[] PaletteNames, List<(byte Index, long Cells)> Counts) MaterialIndexCensus(
        string payloadDir)
    {
        var diagnostics = new GeoDiagnosticBag();
        using var reader = GeneratedMapPayloadReader.Open(payloadDir, diagnostics);
        Assert.NotNull(reader);
        Assert.False(diagnostics.HasErrors);

        var grid = reader!.Metadata.Grid;
        var palette = reader.Metadata.Palette;
        var counts = new Dictionary<byte, long>();

        for (var chunkY = 0; chunkY < grid.ChunksY; chunkY++)
        {
            for (var chunkX = 0; chunkX < grid.ChunksX; chunkX++)
            {
                var material = reader.ReadMaterial(chunkX, chunkY, diagnostics);
                Assert.NotNull(material);
                Assert.False(diagnostics.HasErrors);

                var indices = material!.Indices.Span;
                for (var i = 0; i < indices.Length; i++)
                {
                    counts[indices[i]] = counts.GetValueOrDefault(indices[i]) + 1;
                }
            }
        }

        var ordered = counts.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value)).ToList();

        return (grid.ChunksX, grid.ChunksY, palette.Names.ToArray(), ordered);
    }

    private static Game NewGame(string userRoot)
    {
        Directory.CreateDirectory(userRoot);

        return new Game(
            InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
            preferredBackend: null,
            new Configuration { NoAudio = true, SkipStartingBuildings = true, UserDataRootPath = userRoot },
            window: null);
    }

    private static string RunCli(string[] arguments)
    {
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exit = GeoImportCli.Run(arguments, standardOut, errorOut, CancellationToken.None);

        Assert.True(exit == 0, $"GeoImport {arguments[0]} exited {exit}: {errorOut}");
        Assert.Equal(string.Empty, errorOut.ToString());

        return standardOut.ToString();
    }

    private sealed record LuminanceStats(
        double Min,
        double Max,
        double Mean,
        double StandardDeviation,
        int Distinct,
        double BrightFraction)
    {
        // PixelData is tightly packed 4 bytes per pixel in BGRA order (FrameCapturer).
        public static LuminanceStats Compute(byte[] pixels)
        {
            var count = pixels.Length / 4;
            var values = new int[count];
            long sum = 0;
            var bright = 0;

            for (var i = 0; i < count; i++)
            {
                var offset = i * 4;
                var r = pixels[offset + 2];
                var g = pixels[offset + 1];
                var b = pixels[offset];
                var luminance = (int)((0.2126d * r) + (0.7152d * g) + (0.0722d * b));
                values[i] = luminance;
                sum += luminance;

                if (luminance > 8)
                {
                    bright++;
                }
            }

            var mean = (double)sum / count;
            double squaredSum = 0;

            foreach (var value in values)
            {
                var delta = value - mean;
                squaredSum += delta * delta;
            }

            return new LuminanceStats(
                values.Min(),
                values.Max(),
                mean,
                Math.Sqrt(squaredSum / count),
                values.Distinct().Count(),
                (double)bright / count);
        }
    }
}
