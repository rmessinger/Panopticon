#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Geospatial.Tests.Pipeline;
using Panopticon.Tools.GeoImport;
using Xunit;

namespace Panopticon.Geospatial.Tests.Cli;

/// <summary>
/// In-process `package` verb tests: a payload imported from the synthetic project fixture is
/// converted, and the resulting package is reloaded through the public package reader.
/// </summary>
public class GeoImportPackageCliTests
{
    [Fact]
    public void Package_ImportedPayload_WritesThePackageNamedAfterThePayloadAndExitsZero()
    {
        using var project = ImportProjectFixture.Create();
        var payload = ImportPayload(project);
        var package = project.OutputPath("package");
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(["package", "--payload", payload, "--out", package], standardOut, errorOut, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, errorOut.ToString());
        Assert.True(File.Exists(Path.Combine(package, "manifest.json")));

        var lines = Lines(standardOut);

        Assert.Contains(lines, line => line.StartsWith("package: ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line == "chunks: 1");
        Assert.Equal(4, lines.Count(line => line.StartsWith("limitation: ", StringComparison.Ordinal)));

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
            ["package", "--payload", payload, "--out", package, "--name", "Custom Map"],
            new StringWriter(),
            new StringWriter(),
            CancellationToken.None);

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
            CancellationToken.None);

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

        var exitCode = GeoImportCli.Run(["package", "--payload", payload, "--out", package], standardOut, errorOut, CancellationToken.None);

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
            ["package", "--payload", payload, "--out", package, "--force"],
            standardOut,
            errorOut,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, errorOut.ToString());
        Assert.False(File.Exists(Path.Combine(package, "old.bin")));
        Assert.Single(new PackageReader(package).Read().Layout.Chunks);
    }

    private static string ImportPayload(ImportProjectFixture project)
    {
        var payload = project.OutputPath("payload");
        var exitCode = GeoImportCli.Run(
            ["import", "--manifest", project.ManifestPath, "--out", payload],
            new StringWriter(),
            new StringWriter(),
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        return payload;
    }

    private static string[] Lines(StringWriter writer)
    {
        return writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }
}
