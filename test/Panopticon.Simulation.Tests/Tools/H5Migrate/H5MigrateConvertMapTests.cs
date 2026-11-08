#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Data.Map;
using Panopticon.Tools.H5Migrate;
using Xunit;

namespace Panopticon.Tests.Tools.H5Migrate;

/// <summary>
/// In-process CLI tests for WI-H5-01 `convert-map`: a synthetic playable map converts with
/// every verification gate passing, and the failure classes (missing input, relative input,
/// corrupt input, occupied output) exit non-zero with named diagnostics and no package left
/// at the output.
/// </summary>
public sealed class H5MigrateConvertMapTests
{
    [Fact]
    public void ConvertMap_SyntheticFixture_PassesEveryGate()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var mapPath = WriteSyntheticMapFile(tempRoot);
            var packageDir = Path.Combine(tempRoot, "package");
            using var standardOut = new StringWriter();
            using var errorOut = new StringWriter();

            var exitCode = H5MigrateCli.Run(
                ["convert-map", "--input", mapPath, "--out", packageDir],
                standardOut,
                errorOut,
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, errorOut.ToString());
            Assert.True(Directory.Exists(packageDir));
            Assert.True(File.Exists(Path.Combine(packageDir, MapPackageFormat.ManifestFileName)));
            Assert.Contains("converted: ", standardOut.ToString(), StringComparison.Ordinal);

            // Every verification gate passes when re-run outside the CLI.
            var mapFile = ParseMapFile(mapPath);
            var original = new LegacyMapReader(mapFile, mapPath).Read();
            var reloaded = new PackageReader(packageDir).Read();

            var verification = MapPackageConversionVerifier.Verify(mapFile, original, reloaded);

            Assert.True(verification.Success, string.Join("; ", verification.Diagnostics.Select(d => d.Message)));

            // The manifest accounts for every present source section.
            var retainedNames = reloaded.RetainedSections.Select(section => section.SectionName).ToHashSet(StringComparer.Ordinal);

            foreach (var section in MapSourceSectionInventory.Describe(mapFile))
            {
                if (section.Disposition == MapSourceSectionDisposition.Retained)
                {
                    Assert.True(retainedNames.Contains(section.Name), $"retained section '{section.Name}' missing from the manifest.");
                }
            }

            // Conversion copies values verbatim: no polarity inference, no normalization.
            Assert.Empty(original.Blend.Palette);
            Assert.Empty(reloaded.Blend.Palette);
            Assert.Equal(original.Layout.Chunks.Count, reloaded.Layout.Chunks.Count);

            for (var index = 0; index < original.Layout.Chunks.Count; index++)
            {
                Assert.Null(original.Layout.Chunks[index].Masks);
                Assert.Null(reloaded.Layout.Chunks[index].Masks);

                var originalSamples = original.Layout.Chunks[index].Heights.Samples;
                var reloadedSamples = reloaded.Layout.Chunks[index].Heights.Samples;
                Assert.Equal(originalSamples.Count, reloadedSamples.Count);

                for (var sample = 0; sample < originalSamples.Count; sample++)
                {
                    Assert.Equal(
                        BitConverter.SingleToUInt32Bits(originalSamples[sample]),
                        BitConverter.SingleToUInt32Bits(reloadedSamples[sample]));
                }
            }

            // The blend section is retained byte-exact.
            var originalBlend = Assert.Single(original.RetainedSections, section => section.SectionName == BlendTileData.AssetName);
            var reloadedBlend = Assert.Single(reloaded.RetainedSections, section => section.SectionName == BlendTileData.AssetName);
            Assert.Equal(originalBlend.Sha256, reloadedBlend.Sha256);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ConvertMap_MissingInputFile_FailsTypedWithoutOutput()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var mapPath = Path.Combine(tempRoot, "missing.map");
            var packageDir = Path.Combine(tempRoot, "package");
            using var standardOut = new StringWriter();
            using var errorOut = new StringWriter();

            var exitCode = H5MigrateCli.Run(
                ["convert-map", "--input", mapPath, "--out", packageDir],
                standardOut,
                errorOut,
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.False(Directory.Exists(packageDir));
            Assert.Contains(H5MigrateCliDiagnostics.InputMissing, errorOut.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ConvertMap_RelativeInput_IsAUsageFailure()
    {
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = H5MigrateCli.Run(
            ["convert-map", "--input", "relative/map.map", "--out", "out"],
            standardOut,
            errorOut,
            CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, standardOut.ToString());
        Assert.StartsWith("usage: h5migrate convert-map", errorOut.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertMap_CorruptInput_FailsTypedWithoutOutput()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var mapPath = Path.Combine(tempRoot, "corrupt.map");
            File.WriteAllBytes(mapPath, [0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0]);
            var packageDir = Path.Combine(tempRoot, "package");
            using var standardOut = new StringWriter();
            using var errorOut = new StringWriter();

            var exitCode = H5MigrateCli.Run(
                ["convert-map", "--input", mapPath, "--out", packageDir],
                standardOut,
                errorOut,
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.False(Directory.Exists(packageDir));
            Assert.Contains(H5MigrateCliDiagnostics.InputUnparseable, errorOut.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ConvertMap_OccupiedOutput_FailsAndLeavesNoPartialPackage()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var mapPath = WriteSyntheticMapFile(tempRoot);
            var packageDir = Path.Combine(tempRoot, "package");
            File.WriteAllText(packageDir, "occupied by a regular file");
            using var standardOut = new StringWriter();
            using var errorOut = new StringWriter();

            var exitCode = H5MigrateCli.Run(
                ["convert-map", "--input", mapPath, "--out", packageDir],
                standardOut,
                errorOut,
                CancellationToken.None);

            Assert.Equal(1, exitCode);

            // The staged writer never leaves a partial package at the output.
            Assert.False(Directory.Exists(packageDir));
            Assert.True(File.Exists(packageDir));
            Assert.Contains("target-path-invalid", errorOut.ToString(), StringComparison.Ordinal);
            Assert.Contains("no package was left at", errorOut.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string NewTempRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }

    /// <summary>Builds a playable synthetic map (heights, blend, sides, lighting, waypoints) and saves it as a legacy .map file.</summary>
    private static string WriteSyntheticMapFile(string tempRoot)
    {
        const int lattice = 129;
        const int border = 6;

        var heights = new ushort[lattice, lattice];

        for (var y = 0; y < lattice; y++)
        {
            for (var x = 0; x < lattice; x++)
            {
                heights[x, y] = (ushort)(24 + (((x * 3) + (y * 5)) % 17));
            }
        }

        var mapFile = MapFile.CreateFromHeights(heights, border);

        var mapPath = Path.Combine(tempRoot, "Synthetic.map");

        using (var stream = File.Create(mapPath))
        {
            mapFile.WriteTo(stream);
        }

        return mapPath;
    }

    private static MapFile ParseMapFile(string mapPath)
    {
        using var stream = File.OpenRead(mapPath);
        using var mapStream = MapFile.Decompress(stream);
        return MapFile.FromStream(mapStream);
    }
}
