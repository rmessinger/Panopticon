#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources.Package;
using Xunit;

namespace Panopticon.Tests.Content.Importing.MapSources.Package;

/// <summary>
/// Symlink-rejection tests for the H3 map package chunk layer (WI-H3-02): a package
/// file or a traversed directory swapped for a symbolic link must be rejected on read,
/// both by the guard and by the reader, without following the link.
/// </summary>
public sealed class MapPackageChunkSymlinkTests
{
    [Fact]
    public void SymlinkedPackageFileIsRejectedByGuardAndReader()
    {
        var (root, target, entry) = WriteHeightsPackage();

        try
        {
            var outside = Path.Combine(root, "outside.bin");
            File.WriteAllText(outside, "not package data");

            var heightsPath = Path.Combine(target, "layers", "heights_0_0.bin");
            File.Delete(heightsPath);
            File.CreateSymbolicLink(heightsPath, outside);

            var resolved = MapPackageFileGuard.TryResolve(target, entry.Path, out var fullPath, out var guardError);

            Assert.False(resolved);
            Assert.Null(fullPath);
            Assert.NotNull(guardError);
            Assert.Equal(MapPackageChunkDiagnostic.SymlinkRejected, guardError.Code);

            var read = MapPackageChunkFileReader.ReadHeights(target, entry);

            Assert.False(read.Success);
            Assert.Null(read.Payload);
            Assert.Contains(read.Diagnostics, d => d.Code == MapPackageChunkDiagnostic.SymlinkRejected);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SymlinkedParentDirectoryIsRejectedByGuardAndReader()
    {
        var (root, target, entry) = WriteHeightsPackage();

        try
        {
            // The traversed directory itself becomes a link to a tree outside the package.
            var outsideDir = Path.Combine(root, "outside-layers");
            Directory.Move(Path.Combine(target, "layers"), outsideDir);
            Directory.CreateSymbolicLink(Path.Combine(target, "layers"), outsideDir);

            var resolved = MapPackageFileGuard.TryResolve(target, entry.Path, out var fullPath, out var guardError);

            Assert.False(resolved);
            Assert.Null(fullPath);
            Assert.NotNull(guardError);
            Assert.Equal(MapPackageChunkDiagnostic.SymlinkRejected, guardError.Code);

            var read = MapPackageChunkFileReader.ReadHeights(target, entry);

            Assert.False(read.Success);
            Assert.Null(read.Payload);
            Assert.Contains(read.Diagnostics, d => d.Code == MapPackageChunkDiagnostic.SymlinkRejected);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SymlinkedDeeperComponentIsRejected()
    {
        var (root, target, entry) = WriteHeightsPackage();

        try
        {
            // A nested-directory link in a deeper path component is rejected the same way.
            var elsewhere = Path.Combine(root, "elsewhere");
            Directory.CreateDirectory(elsewhere);
            var nested = Path.Combine(target, "layers", "nested");
            Directory.CreateDirectory(nested);
            Directory.Delete(nested);
            Directory.CreateSymbolicLink(nested, elsewhere);

            var deepEntry = entry with { Path = "layers/nested/heights_0_0.bin" };
            var resolved = MapPackageFileGuard.TryResolve(target, deepEntry.Path, out _, out var guardError);

            Assert.False(resolved);
            Assert.NotNull(guardError);
            Assert.Equal(MapPackageChunkDiagnostic.SymlinkRejected, guardError.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Writes one valid heights-only package under a fresh temporary root.</summary>
    private static (string Root, string Target, MapPackageLayerEntry HeightsEntry) WriteHeightsPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "panopticon-h3-02-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "package");

        var heights = new float[131 * 131];

        for (var i = 0; i < heights.Length; i++)
        {
            heights[i] = i * 0.5f;
        }

        var heightsEncoded = MapPackageChunkCodec.EncodeHeights(heights);
        var entry = new MapPackageLayerEntry(
            0,
            0,
            MapPackageFormat.LayerKindHeights,
            "layers/heights_0_0.bin",
            MapPackageFormat.EncodingFloat32Le,
            131,
            131,
            heightsEncoded.Length,
            Sha256Hex(heightsEncoded));
        var manifest = BuildManifest([entry]);
        var write = MapPackageDirectoryWriter.Write(target, manifest, new FixedPayloads(heights));

        Assert.True(write.Success, string.Join("; ", write.Diagnostics.Select(d => d.ToString())));

        return (root, target, entry);
    }

    private static MapPackageManifest BuildManifest(MapPackageLayerEntry[] layers)
    {
        return new MapPackageManifest(
            MapPackageFormat.PackageFormatName,
            MapPackageFormat.CurrentPackageVersion,
            MapInterchange.CurrentSchemaVersion,
            new MapPackageIdentity("TestMap", "LegacyMap", 8),
            new MapPackageTerrainLayout(
                new MapPackageQuantity(10.0, "GameUnit", "MapLocalXy"),
                129,
                129,
                1,
                1,
                0,
                null,
                "Fail",
                1,
                16641,
                []),
            new MapPackageBlendModel(
            [
                new MapPackageMaterialPaletteEntry(0, "grass", new MapPackageAssetReference("terrain", "grass")),
            ]),
            [],
            [],
            new MapPackageProvenance("1.0", [], []),
            layers,
            []);
    }

    private static string Sha256Hex(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    private sealed class FixedPayloads : IMapPackagePayloadProvider
    {
        private readonly float[] _heights;

        public FixedPayloads(float[] heights)
        {
            _heights = heights;
        }

        public float[] GetHeights(MapPackageLayerEntry entry)
        {
            return _heights;
        }

        public byte[] GetMaterials(MapPackageLayerEntry entry)
        {
            throw new InvalidOperationException("This package carries no materials.");
        }

        public byte[] GetMasks(MapPackageLayerEntry entry)
        {
            throw new InvalidOperationException("This package carries no masks.");
        }

        public byte[] GetImagery(MapPackageLayerEntry entry)
        {
            throw new InvalidOperationException("This package carries no imagery.");
        }

        public byte[] GetRetainedSection(MapPackageRetainedEntry entry)
        {
            throw new InvalidOperationException("This package carries no retained sections.");
        }
    }
}
