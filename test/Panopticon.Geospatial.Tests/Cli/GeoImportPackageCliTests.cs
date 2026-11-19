#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Preview;
using Panopticon.Geospatial.Tests.Payload;
using Panopticon.Geospatial.Tests.Pipeline;
using Panopticon.Tools.GeoImport;
using Xunit;
using MapPropertyValue = Panopticon.Content.Importing.Maps.MapPropertyValue;

namespace Panopticon.Geospatial.Tests.Cli;

/// <summary>
/// In-process `package` verb tests: a payload imported from the synthetic project fixture is
/// converted, and the resulting package is reloaded through the public package reader.
/// </summary>
public class GeoImportPackageCliTests
{
    private static readonly GeoImportCliOptions KnownCatalog = new()
    {
        TerrainTextureCatalogResolver = () => new FixedTerrainTextureCatalog(["GrassType1", "DirtMediumType1", "RockType1", "SandType1"]),
    };

    [Fact]
    public void Package_ImportedPayload_WritesThePackageNamedAfterThePayloadAndExitsZero()
    {
        using var project = ImportProjectFixture.Create();
        var payload = ImportPayload(project);
        var package = project.OutputPath("package");
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(["package", "--payload", payload, "--textures", ShrubTextures(project), "--out", package], standardOut, errorOut, CancellationToken.None, KnownCatalog);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, errorOut.ToString());
        Assert.True(File.Exists(Path.Combine(package, "manifest.json")));

        var lines = Lines(standardOut);

        Assert.Contains(lines, line => line.StartsWith("package: ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line == "chunks: 1");
        Assert.Equal(6, lines.Count(line => line.StartsWith("limitation: ", StringComparison.Ordinal)));

        var interchange = new PackageReader(package).Read();

        Assert.Equal("payload", interchange.Identity.Name);
        Assert.Single(interchange.Layout.Chunks);
    }

    [Fact]
    public void Package_WithName_UsesTheGivenMapName()
    {
        using var project = ImportProjectFixture.Create();
        var payload = ImportPayload(project);
        var package = project.OutputPath("package");

        var exitCode = GeoImportCli.Run(
            ["package", "--payload", payload, "--textures", ShrubTextures(project), "--out", package, "--name", "Custom Map"],
            new StringWriter(),
            new StringWriter(),
            CancellationToken.None, KnownCatalog);

        Assert.Equal(0, exitCode);
        Assert.Equal("Custom Map", new PackageReader(package).Read().Identity.Name);
    }

    [Fact]
    public void Package_MissingPayloadDirectory_IsAnOperationalFailure()
    {
        using var project = ImportProjectFixture.Create(manifest: false);
        var package = project.OutputPath("package");
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(
            ["package", "--payload", Path.Combine(project.Root, "missing"), "--out", package],
            standardOut,
            errorOut,
            CancellationToken.None, KnownCatalog);

        Assert.Equal(1, exitCode);
        Assert.False(Directory.Exists(package));
        Assert.Equal(string.Empty, standardOut.ToString());
        Assert.Contains(Lines(errorOut), line => line.StartsWith("package failed: ", StringComparison.Ordinal));
    }

    [Fact]
    public void Package_ExistingOutputWithoutForce_IsAnOperationalFailureAndLeavesItUntouched()
    {
        using var project = ImportProjectFixture.Create();
        var payload = ImportPayload(project);
        var package = project.OutputPath("package");
        Directory.CreateDirectory(package);
        File.WriteAllText(Path.Combine(package, "operator.txt"), "operator");
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(["package", "--payload", payload, "--textures", ShrubTextures(project), "--out", package], standardOut, errorOut, CancellationToken.None, KnownCatalog);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, standardOut.ToString());
        Assert.Equal("operator", File.ReadAllText(Path.Combine(package, "operator.txt")));
        Assert.False(File.Exists(Path.Combine(package, "manifest.json")));
        Assert.Equal(
            $"package failed: the output {package} already exists; pass --force to replace it",
            Assert.Single(Lines(errorOut)));
    }

    [Fact]
    public void Package_ExistingOutputWithForce_ReplacesItAndExitsZero()
    {
        using var project = ImportProjectFixture.Create();
        var payload = ImportPayload(project);
        var package = project.OutputPath("package");
        Directory.CreateDirectory(package);
        File.WriteAllText(Path.Combine(package, "old.bin"), "old");
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(
            ["package", "--payload", payload, "--textures", ShrubTextures(project), "--out", package, "--force"],
            standardOut,
            errorOut,
            CancellationToken.None, KnownCatalog);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, errorOut.ToString());
        Assert.False(File.Exists(Path.Combine(package, "old.bin")));
        Assert.Single(new PackageReader(package).Read().Layout.Chunks);
    }

    [Fact]
    public void Package_DuplicateSamePartFeatures_PrintsDedupeDiagnosticsAndExitsZero()
    {
        // One source part mapped by two rules into a passability and a buildability feature:
        // the converter merges the provenance entries and the success path must print the
        // informational dedupe diagnostics, not swallow them.
        using var scratch = new CliScratchRoot();
        StageSamePartPayload(scratch.Root, out var stableId);
        var package = Path.Combine(scratch.Root, "package");
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(
            ["package", "--payload", Path.Combine(scratch.Root, "payload"), "--out", package, "--name", "dup-part"],
            standardOut,
            errorOut,
            CancellationToken.None, KnownCatalog);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, errorOut.ToString());
        Assert.True(File.Exists(Path.Combine(package, MapPackageFormat.ManifestFileName)));

        var lines = Lines(standardOut);

        Assert.Contains(lines, line => line.StartsWith("package: ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line == "chunks: 1");
        Assert.Equal(6, lines.Count(line => line.StartsWith("limitation: ", StringComparison.Ordinal)));
        Assert.Contains(
            lines,
            line => line.StartsWith("diagnostic: ", StringComparison.Ordinal) && line.Contains("GEO-PKG-008", StringComparison.Ordinal) && line.Contains(stableId, StringComparison.Ordinal));
    }

    [Fact]
    public void Package_SkirmishSlots_WriteTheSidesSectionAndThePlayerStarts()
    {
        using var project = ImportProjectFixture.Create();
        var payload = ImportPayload(project);
        var package = project.OutputPath("package");
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(
            ["package", "--payload", payload, "--textures", ShrubTextures(project), "--out", package, "--skirmish", "FactionAmerica:100,100;FactionGLA:300,300"],
            standardOut,
            errorOut,
            CancellationToken.None, KnownCatalog);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, errorOut.ToString());

        var lines = Lines(standardOut);
        Assert.Contains(
            lines,
            line => line.StartsWith("diagnostic: ", StringComparison.Ordinal) && line.Contains("GEO-PKG-018", StringComparison.Ordinal) && line.Contains("Player_1_Start", StringComparison.Ordinal));

        var interchange = new PackageReader(package).Read();

        var sidesSection = Assert.Single(interchange.RetainedSections, section => section.SectionName == "SidesList");
        Assert.Equal(3, (int)sidesSection.SectionVersion);

        var objects = interchange.Objects!.Objects;
        var start1 = Assert.Single(objects, placed => placed.StableId == "Player_1_Start");
        var start2 = Assert.Single(objects, placed => placed.StableId == "Player_2_Start");
        Assert.Equal("*Waypoints/Waypoint", start1.Type.Name);
        Assert.Equal("*Waypoints/Waypoint", start2.Type.Name);
        Assert.Equal(100d, start1.X.Value);
        Assert.Equal(100d, start1.Y.Value);
        Assert.Equal(300d, start2.X.Value);
        Assert.Equal(300d, start2.Y.Value);
        Assert.Equal(1, Assert.IsType<MapPropertyValue.IntegerValue>(start1.LegacyProperties["waypointID"]).Value);
        Assert.Equal(2, Assert.IsType<MapPropertyValue.IntegerValue>(start2.LegacyProperties["waypointID"]).Value);
    }

    [Fact]
    public void Package_SkirmishWithUnknownFaction_FailsWithTheTypedDiagnosticBeforePublishing()
    {
        using var project = ImportProjectFixture.Create();
        var payload = ImportPayload(project);
        var package = project.OutputPath("package");
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(
            ["package", "--payload", payload, "--textures", ShrubTextures(project), "--out", package, "--skirmish", "FactionAmerica:100,100;FactionMars:300,300"],
            standardOut,
            errorOut,
            CancellationToken.None, KnownCatalog);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, standardOut.ToString());
        Assert.False(Directory.Exists(package));
        Assert.Contains(
            Lines(errorOut),
            line => line.StartsWith("GEO-PKG-017:", StringComparison.Ordinal) && line.Contains("FactionMars", StringComparison.Ordinal) && line.Contains("FactionAmerica, FactionChina, FactionGLA", StringComparison.Ordinal));
        Assert.Contains(Lines(errorOut), line => line.StartsWith("package failed: ", StringComparison.Ordinal));
    }

    [Fact]
    public void Package_SkirmishBeyondTheMapExtent_FailsWithTheTypedDiagnosticBeforePublishing()
    {
        // The single-chunk fixture extent is 1280 game units; slot 2 lies outside it.
        using var project = ImportProjectFixture.Create();
        var payload = ImportPayload(project);
        var package = project.OutputPath("package");
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(
            ["package", "--payload", payload, "--textures", ShrubTextures(project), "--out", package, "--skirmish", "FactionAmerica:100,100;FactionGLA:5000,500"],
            standardOut,
            errorOut,
            CancellationToken.None, KnownCatalog);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, standardOut.ToString());
        Assert.False(Directory.Exists(package));
        Assert.Contains(
            Lines(errorOut),
            line => line.StartsWith("GEO-PKG-017:", StringComparison.Ordinal) && line.Contains("outside the map extent", StringComparison.Ordinal));
        Assert.Contains(Lines(errorOut), line => line.StartsWith("package failed: ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData(";;")]
    [InlineData("FactionAmerica")]
    [InlineData("FactionAmerica:100")]
    [InlineData("FactionAmerica:abc,5")]
    [InlineData("FactionAmerica:nan,5")]
    [InlineData("FactionAmerica:-5,5")]
    [InlineData("FactionAmerica:0,0;FactionGLA:0,0")]
    public void Package_SkirmishWithMalformedValue_FailsWithTheTypedDiagnosticBeforePublishing(string skirmish)
    {
        using var project = ImportProjectFixture.Create();
        var payload = ImportPayload(project);
        var package = project.OutputPath("package");
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(
            ["package", "--payload", payload, "--textures", ShrubTextures(project), "--out", package, "--skirmish", skirmish],
            standardOut,
            errorOut,
            CancellationToken.None, KnownCatalog);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, standardOut.ToString());
        Assert.False(Directory.Exists(package));
        Assert.Contains(Lines(errorOut), line => line.StartsWith("GEO-PKG-017:", StringComparison.Ordinal));
        Assert.Contains(Lines(errorOut), line => line.StartsWith("package failed: ", StringComparison.Ordinal));
    }

    private static void StageSamePartPayload(string root, out string stableId)
    {
        var payloadRoot = Path.Combine(root, "payload");

        var diagnostics = new GeoDiagnosticBag();
        var writer = GeneratedMapPayloadWriter.Create(payloadRoot, [], diagnostics);
        Assert.NotNull(writer);
        Assert.False(diagnostics.HasErrors);

        var palette = new MaterialPalette(ReaderTestSupport.PaletteNames);
        var terrain = writer.WriteTerrain(new TerrainChunk(0, 0, ReaderTestSupport.TerrainFromLattice(0, 0)));
        var materials = writer.WriteMaterial(new MaterialChunk(0, 0, palette, ReaderTestSupport.MaterialFromLattice(0, 0)));
        var masks = writer.WriteMask(new TerrainMaskChunk(0, 0, ReaderTestSupport.MaskFromLattice(0, 0)));

        Assert.NotNull(terrain);
        Assert.NotNull(materials);
        Assert.NotNull(masks);

        var geometry = new GeoPolygon(Ring(
            (500000d, 6000000d),
            (500010d, 6000000d),
            (500010d, 6000010d),
            (500000d, 6000010d),
            (500000d, 6000000d)));
        stableId = "BATIMENT-0/buildings/BATIMENT-0#0";
        var features = new List<GeneratedFeature>
        {
            new(GeneratedFeatureKind.Passability, geometry, "buildings", "BATIMENT-0", "buildings/BATIMENT-0#0", 0, passable: false),
            new(GeneratedFeatureKind.Buildability, geometry, "buildings", "BATIMENT-0", "buildings/BATIMENT-0#0", 1, buildable: false),
        };
        var descriptors = new List<PayloadChunkDescriptor> { new(0, 0, terrain!, materials!, masks!, null) };
        var metadata = new GeneratedMapPayloadMetadata(
            new PayloadGridDefinition(1, 1, ReaderTestSupport.Transform(), ReaderTestSupport.PlayableArea(1, 1), null, new SlopeThresholds(45d, 15d)),
            palette,
            features,
            ReaderTestSupport.ManifestSha256Hex,
            ReaderTestSupport.Provenance(),
            descriptors);

        var payloadSha256Hex = writer.WriteMetadata(metadata);
        Assert.NotNull(payloadSha256Hex);
        Assert.True(writer.WriteReport(ReaderTestSupport.CleanReport(metadata, payloadSha256Hex!)));
        Assert.True(writer.TryCommit());
    }

    private static GeoRing Ring(params (double X, double Y)[] positions)
    {
        var ring = positions.Select(position => new GeoPosition(new Vector2d(position.X, position.Y))).ToList();
        ring.Add(ring[0]);

        return new GeoRing(ring);
    }

    private static string ShrubTextures(ImportProjectFixture project)
    {
        var path = project.OutputPath("textures.json");
        File.WriteAllText(path, "{\"grass\":\"GrassType1\",\"dirt\":\"DirtMediumType1\",\"rock\":\"RockType1\",\"shrub\":\"GrassType1\"}");
        return path;
    }

    private static string ImportPayload(ImportProjectFixture project)
    {
        var payload = project.OutputPath("payload");
        var exitCode = GeoImportCli.Run(
            ["import", "--manifest", project.ManifestPath, "--out", payload],
            new StringWriter(),
            new StringWriter(),
            CancellationToken.None, KnownCatalog);

        Assert.Equal(0, exitCode);
        return payload;
    }

    private sealed class FixedTerrainTextureCatalog(string[] names) : ITerrainTextureCatalog
    {
        public IReadOnlyCollection<string> TerrainTextureNames => names;
    }

    private static string[] Lines(StringWriter writer)
    {
        return writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private sealed class CliScratchRoot : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "panopticon-package-cli-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
