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
using Panopticon.IO;
using Panopticon.Tests.Data;
using Panopticon.Tools.H5Migrate;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Tools.H5Migrate;

/// <summary>
/// Installed-corpus conversion coverage for WI-H5-01: the first supported installed maps
/// convert through the CLI with every verification gate passing, and mask and blend bits
/// stay byte-identical on the first covered map. Vacuous skip without an install, matching
/// the repo corpus convention. Requires CNC_GENERALS_PATH.
/// </summary>
public sealed class H5MigrateCorpusTests
{
    private const int MaxCorpusMaps = 3;

    private readonly ITestOutputHelper _output;

    public H5MigrateCorpusTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void InstalledCorpus_FirstMaps_ConvertThroughCliWithGatesPassing()
    {
        var covered = 0;
        var filesSeen = 0;
        var verbatimAsserted = false;
        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            InstalledFilesTestData.ReadFiles(".map", _output, entry =>
            {
                filesSeen++;

                if (covered >= MaxCorpusMaps)
                {
                    return;
                }

                // Mirror the corpus gate: parseable maps with a supported SidesList version only.
                MapFile mapFile;

                using (var entryStream = OpenOrNull(entry))
                {
                    if (entryStream is null)
                    {
                        _output.WriteLine($"{entry.FilePath}: unreadable stream, skipped.");
                        return;
                    }

                    try
                    {
                        using var mapStream = MapFile.Decompress(entryStream);
                        mapFile = MapFile.FromStream(mapStream);
                    }
                    catch (Exception exception)
                    {
                        _output.WriteLine($"{entry.FilePath}: unparseable ({exception.GetType().Name}: {exception.Message}), skipped.");
                        return;
                    }
                }

                if (mapFile.SidesList is { } sidesList && !LegacyMapReader.IsSupportedSidesListVersion(sidesList.Version))
                {
                    _output.WriteLine($"{entry.FilePath}: unsupported SidesList version {sidesList.Version}, skipped.");
                    return;
                }

                // Extract the raw entry bytes to a real file: the CLI reads absolute disk paths.
                var mapPath = Path.Combine(tempRoot, $"corpus-{covered}.map");

                using (var raw = OpenOrNull(entry))
                using (var file = File.Create(mapPath))
                {
                    Assert.NotNull(raw);
                    raw!.CopyTo(file);
                }

                var packageDir = Path.Combine(tempRoot, $"pkg-{covered}");
                using var standardOut = new StringWriter();
                using var errorOut = new StringWriter();

                var exitCode = H5MigrateCli.Run(
                    ["convert-map", "--input", mapPath, "--out", packageDir],
                    standardOut,
                    errorOut,
                    CancellationToken.None);

                Assert.True(exitCode == 0, $"CLI conversion failed for {entry.FilePath} (exit {exitCode}): {errorOut}");

                // Re-run the gates outside the CLI: manifest accounting, semantic equality, retained SHA-256.
                var original = new LegacyMapReader(mapFile, mapPath).Read();
                var reloaded = new PackageReader(packageDir).Read();
                var verification = MapPackageConversionVerifier.Verify(mapFile, original, reloaded);

                Assert.True(verification.Success, $"Gates failed for {entry.FilePath}: {string.Join("; ", verification.Diagnostics.Select(d => d.Message))}");

                if (!verbatimAsserted)
                {
                    AssertVerbatimMaskAndBlend(original, reloaded);
                    verbatimAsserted = true;
                }

                covered++;
                _output.WriteLine(
                    $"{entry.FilePath}: converted ({covered}/{MaxCorpusMaps}), retained {reloaded.RetainedSections.Count}.");
            });

            if (filesSeen == 0)
            {
                _output.WriteLine("No installed .map corpus found; skipped.");
                return;
            }

            _output.WriteLine($"Corpus conversion covered {covered} installed maps ({filesSeen} files seen).");
            Assert.Equal(MaxCorpusMaps, covered);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verbatim rule on one corpus map: no Buildability polarity inference and no normalization,
    /// so mask and blend bits are byte-identical (legacy chunks carry no mask or palette data,
    /// and the retained blend bytes match by SHA-256).
    /// </summary>
    private static void AssertVerbatimMaskAndBlend(MapInterchange original, MapInterchange reloaded)
    {
        Assert.Equal(original.Blend.Palette.Count, reloaded.Blend.Palette.Count);
        Assert.Equal(original.Layout.Chunks.Count, reloaded.Layout.Chunks.Count);

        for (var index = 0; index < original.Layout.Chunks.Count; index++)
        {
            var originalChunk = original.Layout.Chunks[index];
            var reloadedChunk = reloaded.Layout.Chunks[index];

            if (originalChunk.Masks is null)
            {
                Assert.Null(reloadedChunk.Masks);
            }
            else
            {
                Assert.NotNull(reloadedChunk.Masks);
                Assert.True(originalChunk.Masks.Flags.SequenceEqual(reloadedChunk.Masks!.Flags));
            }

            if (originalChunk.Materials is null)
            {
                Assert.Null(reloadedChunk.Materials);
            }
            else
            {
                Assert.NotNull(reloadedChunk.Materials);
                Assert.True(originalChunk.Materials.Indices.SequenceEqual(reloadedChunk.Materials!.Indices));
            }
        }

        var originalBlend = original.RetainedSections.SingleOrDefault(section => section.SectionName == BlendTileData.AssetName);
        var reloadedBlend = reloaded.RetainedSections.SingleOrDefault(section => section.SectionName == BlendTileData.AssetName);

        if (originalBlend is not null)
        {
            Assert.NotNull(reloadedBlend);
            Assert.Equal(originalBlend.Sha256, reloadedBlend!.Sha256);
        }
    }

    private static Stream? OpenOrNull(FileSystemEntry entry)
    {
        try
        {
            return entry.Open();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
