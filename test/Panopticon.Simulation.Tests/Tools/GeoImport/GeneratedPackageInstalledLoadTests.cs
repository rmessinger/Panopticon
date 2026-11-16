using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Payload;
using Panopticon.Tests.Data;
using Panopticon.Tools.GeoImport;
using Xunit;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// Production load proof for generated H3 packages in the installed game. Both tests run the
/// public GeoImport CLI (import, package) and then <see cref="Game.LoadMap"/> on the package
/// directory, with no test hooks in the load path.
/// </summary>
[Collection("SerialGameTests")]
public sealed class GeneratedPackageInstalledLoadTests
{
    private const int SedanBuildingCount = 1676;

    private const string DiagnosticPrefix = "diagnostic: ";

    private const string SidesListSection = "SidesList";

    private const string GlobalLightingSection = "GlobalLighting";

    private const string BlendTileDataSection = "BlendTileData";

    private const float ResidentStep = 0.0390625f;

    private static readonly string[] InformationalCodes = ["GEO-PKG-008", "GEO-PKG-013", "GEO-PKG-015"];

    [GameFact(SageGame.CncGenerals)]
    public void OffGridPayloadWithoutSides_ConvertsAndLoadsWithQuantizedResidentHeights()
    {
        using var fixture = Card24ProofFixture.Create();
        var scratchRoot = NewScratchRoot();

        try
        {
            var payloadDir = Path.Combine(scratchRoot, "payload");
            var packageDir = Path.Combine(scratchRoot, "package");
            var userRoot = Path.Combine(scratchRoot, "user");

            RunCli(["import", "--manifest", fixture.ManifestPath, "--out", payloadDir]);

            var terrain = ReadTerrainChunks(payloadDir);
            var offGridSamples = CountCoreSamples(terrain.Chunks, height => !IsOnResidentGrid(height));
            Assert.True(offGridSamples > 0, "The synthetic payload must carry heights off the resident grid.");

            var texturesPath = Path.Combine(scratchRoot, "textures.json");
            File.WriteAllText(
                texturesPath,
                "{\"grass\":\"GrassType1\",\"dirt\":\"DirtMediumType1\",\"rock\":\"RockType1\",\"sand\":\"SandType1\",\"paved\":\"DirtMediumType1\"}");

            RunCli(["package", "--payload", payloadDir, "--textures", texturesPath, "--out", packageDir]);

            var package = new PackageReader(packageDir).Read();
            Assert.DoesNotContain(package.RetainedSections, section => section.SectionName == SidesListSection);
            Assert.Contains(package.RetainedSections, section => section.SectionName == BlendTileDataSection);
            Assert.Contains(package.RetainedSections, section => section.SectionName == GlobalLightingSection);

            using (var game = NewGame(userRoot))
            {
                game.LoadMap(packageDir, [], GameType.SinglePlayer);

                var heightMap = game.TerrainLogic.HeightMap;
                Assert.NotNull(heightMap);
                Assert.NotNull(game.Scene3D.Lighting);

                var mismatches = 0;
                string? firstMismatch = null;
                foreach (var chunk in terrain.Chunks)
                {
                    for (var j = 0; j < ChunkLayout.PointsPerChunkAxis; j++)
                    {
                        for (var i = 0; i < ChunkLayout.PointsPerChunkAxis; i++)
                        {
                            var latticeX = (chunk.ChunkX * ChunkLayout.IntervalsPerChunk) + i;
                            var latticeY = (chunk.ChunkY * ChunkLayout.IntervalsPerChunk) + j;
                            var expected = ResidentGridHeight(StoredHeight(chunk, i, j));
                            var actual = heightMap.GetHeight(latticeX, latticeY);

                            if (actual == expected)
                            {
                                continue;
                            }

                            mismatches++;
                            firstMismatch ??= $"({latticeX},{latticeY}) expected {expected} actual {actual}";
                        }
                    }
                }

                Assert.True(mismatches == 0, $"{mismatches} resident heights differ from the quantized payload; first: {firstMismatch}");
            }
        }
        finally
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
    }

    [SedanOperatorFixtureFact(SageGame.CncGenerals)]
    public void SedanRawV3Payload_ConvertsAndLoadsWithFullBuildingLayer()
    {
        Assert.True(Directory.Exists(SedanOperatorFixtureFactAttribute.OperatorFixturePath), $"The Sedan payload is missing: {SedanOperatorFixtureFactAttribute.OperatorFixturePath}");

        var scratchRoot = NewScratchRoot();

        try
        {
            var packageDir = Path.Combine(scratchRoot, "package");
            var userRoot = Path.Combine(scratchRoot, "user");

            var output = RunCli(["package", "--payload", SedanOperatorFixtureFactAttribute.OperatorFixturePath, "--out", packageDir]);

            var diagnosticCodes = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.StartsWith(DiagnosticPrefix, StringComparison.Ordinal))
                .Select(line => line[DiagnosticPrefix.Length..].Split(':')[0])
                .ToArray();
            Assert.Contains("GEO-PKG-013", diagnosticCodes);
            Assert.All(diagnosticCodes, code => Assert.Contains(code, InformationalCodes));

            var package = new PackageReader(packageDir).Read();
            Assert.NotNull(package.Buildings);
            Assert.Equal(SedanBuildingCount, package.Buildings!.Buildings.Count);

            Assert.Contains(package.RetainedSections, section => section.SectionName == BlendTileDataSection);
            Assert.Contains(package.RetainedSections, section => section.SectionName == GlobalLightingSection);

            using var game = NewGame(userRoot);
            game.LoadMap(packageDir, [], GameType.SinglePlayer);

            Assert.NotNull(game.TerrainLogic.HeightMap);
            Assert.NotNull(game.Scene3D.Lighting);
        }
        finally
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
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

    private static string NewScratchRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "opencode", "r3-g4-package-load", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
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

    private static (int ChunksX, int ChunksY, IReadOnlyList<TerrainChunk> Chunks) ReadTerrainChunks(string payloadDir)
    {
        var diagnostics = new GeoDiagnosticBag();
        using var reader = GeneratedMapPayloadReader.Open(payloadDir, diagnostics);
        Assert.NotNull(reader);
        Assert.False(diagnostics.HasErrors);

        var grid = reader!.Metadata.Grid;
        var chunks = new List<TerrainChunk>();
        for (var chunkY = 0; chunkY < grid.ChunksY; chunkY++)
        {
            for (var chunkX = 0; chunkX < grid.ChunksX; chunkX++)
            {
                var chunk = reader.ReadTerrain(chunkX, chunkY, diagnostics);
                Assert.NotNull(chunk);
                chunks.Add(chunk!);
            }
        }

        Assert.False(diagnostics.HasErrors);
        return (grid.ChunksX, grid.ChunksY, chunks);
    }

    private static int CountCoreSamples(IReadOnlyList<TerrainChunk> chunks, Func<float, bool> predicate)
    {
        var count = 0;
        foreach (var chunk in chunks)
        {
            for (var j = 0; j < ChunkLayout.PointsPerChunkAxis; j++)
            {
                for (var i = 0; i < ChunkLayout.PointsPerChunkAxis; i++)
                {
                    if (predicate(StoredHeight(chunk, i, j)))
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    private static float StoredHeight(TerrainChunk chunk, int coreX, int coreY)
    {
        return chunk.Heights.Span[((coreY + 1) * ChunkLayout.StoredPointsPerChunkAxis) + coreX + 1];
    }

    private static bool IsOnResidentGrid(float height)
    {
        return ResidentGridHeight(height) == height;
    }

    private static float ResidentGridHeight(float height)
    {
        return MathF.Round(height / ResidentStep, MidpointRounding.ToEven) * ResidentStep;
    }
}
