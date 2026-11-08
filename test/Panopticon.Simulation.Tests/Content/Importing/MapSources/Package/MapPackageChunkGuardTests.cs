#nullable enable

using System;
using System.IO;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources.Package;
using Xunit;

namespace Panopticon.Tests.Content.Importing.MapSources.Package;

/// <summary>
/// Path-containment tests for the H3 map package chunk layer (WI-H3-02): entry paths
/// that escape the package root are rejected by <see cref="MapPackageFileGuard"/> and by
/// <see cref="MapPackageChunkFileReader"/> even when the entry object was constructed
/// directly and never passed the schema validator.
/// </summary>
public sealed class MapPackageChunkGuardTests
{
    private const string ValidSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void ParentTraversalEntryPathIsRejectedByGuard()
    {
        var root = NewTempRoot();

        try
        {
            var packageRoot = Path.Combine(root, "package");
            Directory.CreateDirectory(packageRoot);

            // Constructed directly, bypassing the schema validator on purpose.
            var entry = new MapPackageLayerEntry(
                0,
                0,
                MapPackageFormat.LayerKindHeights,
                "../outside.bin",
                MapPackageFormat.EncodingFloat32Le,
                131,
                131,
                131 * 131 * 4,
                ValidSha256);

            var resolved = MapPackageFileGuard.TryResolve(packageRoot, entry.Path, out var fullPath, out var error);

            Assert.False(resolved);
            Assert.Null(fullPath);
            Assert.NotNull(error);
            Assert.Equal(MapPackageChunkDiagnostic.PathEscape, error.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DeepTraversalEntryPathIsRejectedByReader()
    {
        var root = NewTempRoot();

        try
        {
            var packageRoot = Path.Combine(root, "package");
            Directory.CreateDirectory(packageRoot);

            var entry = new MapPackageLayerEntry(
                0,
                0,
                MapPackageFormat.LayerKindHeights,
                "layers/../../escaped.bin",
                MapPackageFormat.EncodingFloat32Le,
                131,
                131,
                131 * 131 * 4,
                ValidSha256);

            var read = MapPackageChunkFileReader.ReadHeights(packageRoot, entry);

            Assert.False(read.Success);
            Assert.Null(read.Payload);
            Assert.Contains(read.Diagnostics, d => d.Code == MapPackageChunkDiagnostic.PathEscape);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AbsoluteEntryPathIsRejected()
    {
        var root = NewTempRoot();

        try
        {
            var packageRoot = Path.Combine(root, "package");
            Directory.CreateDirectory(packageRoot);

            var resolved = MapPackageFileGuard.TryResolve(packageRoot, "/etc/passwd", out var fullPath, out var error);

            Assert.False(resolved);
            Assert.Null(fullPath);
            Assert.NotNull(error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SchemaInvalidPathIsRejectedWithSchemaDiagnostic()
    {
        var root = NewTempRoot();

        try
        {
            var packageRoot = Path.Combine(root, "package");
            Directory.CreateDirectory(packageRoot);

            // Contained but schema-invalid: empty path segment.
            var resolved = MapPackageFileGuard.TryResolve(packageRoot, "layers//h.bin", out var fullPath, out var error);

            Assert.False(resolved);
            Assert.Null(fullPath);
            Assert.NotNull(error);
            Assert.Equal(MapPackageDiagnostic.PathInvalid, error.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingFileResolvesThroughGuardAndReportsFileMissingOnRead()
    {
        var root = NewTempRoot();

        try
        {
            var packageRoot = Path.Combine(root, "package");
            Directory.CreateDirectory(packageRoot);

            var resolved = MapPackageFileGuard.TryResolve(packageRoot, "layers/heights_0_0.bin", out var fullPath, out var error);

            Assert.True(resolved);
            Assert.Null(error);
            Assert.NotNull(fullPath);
            Assert.EndsWith(Path.Combine("layers", "heights_0_0.bin"), fullPath);

            var entry = new MapPackageLayerEntry(
                0,
                0,
                MapPackageFormat.LayerKindHeights,
                "layers/heights_0_0.bin",
                MapPackageFormat.EncodingFloat32Le,
                131,
                131,
                131 * 131 * 4,
                ValidSha256);
            var read = MapPackageChunkFileReader.ReadHeights(packageRoot, entry);

            Assert.False(read.Success);
            Assert.Null(read.Payload);
            Assert.Contains(read.Diagnostics, d => d.Code == MapPackageChunkDiagnostic.FileMissing);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ValidContainedPathResolvesInsideRoot()
    {
        var root = NewTempRoot();

        try
        {
            var packageRoot = Path.Combine(root, "package");
            Directory.CreateDirectory(Path.Combine(packageRoot, "layers"));

            var resolved = MapPackageFileGuard.TryResolve(packageRoot, "layers/heights_0_0.bin", out var fullPath, out var error);

            Assert.True(resolved);
            Assert.Null(error);
            Assert.NotNull(fullPath);
            Assert.StartsWith(Path.GetFullPath(packageRoot) + Path.DirectorySeparatorChar, fullPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "panopticon-h3-02-" + Guid.NewGuid().ToString("N"));
    }
}
