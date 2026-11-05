#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Tests.Pipeline;
using Panopticon.Tools.GeoImport;
using Xunit;

namespace Panopticon.Geospatial.Tests.Cli;

/// <summary>
/// In-process CLI tests over the real importer and reader, reusing the synthetic
/// real-file KML and GeoTIFF project fixture of the importer tests.
/// </summary>
public class GeoImportCliTests
{
    [Fact]
    public void Import_SyntheticManifest_PublishesThePayloadAndExitsZero()
    {
        using var project = ImportProjectFixture.Create();
        var output = project.OutputPath();
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(["import", "--manifest", project.ManifestPath, "--out", output], standardOut, errorOut, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(output, "payload.json")));
        Assert.Contains(Lines(standardOut), line => line.StartsWith("published: ", StringComparison.Ordinal));
        Assert.Equal(string.Empty, errorOut.ToString());
    }

    [Fact]
    public void Import_MissingRequiredArgument_IsAUsageFailure()
    {
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(["import", "--manifest", "manifest.json"], standardOut, errorOut, CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, standardOut.ToString());
        Assert.StartsWith("usage: geoimport", errorOut.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, CountLines(errorOut));
    }

    [Fact]
    public void Import_MalformedManifest_IsAnOperationalFailureWithSingleLineDiagnostics()
    {
        using var project = ImportProjectFixture.Create(manifest: false);
        var output = project.OutputPath();
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(
            ["import", "--manifest", Path.Combine(project.Root, "missing.json"), "--out", output],
            standardOut,
            errorOut,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.False(Directory.Exists(output));
        Assert.True(errorOut.ToString().Length > 0);

        // Every diagnostic is exactly one `severity code stage message` line.
        foreach (var line in Lines(errorOut))
        {
            var tokens = line.Split(' ');
            Assert.True(tokens.Length >= 4, $"the diagnostic line must carry severity, code, stage, and message: '{line}'");
            Assert.True(tokens[0] is "Error" or "Warning" or "Info", $"unexpected severity token '{tokens[0]}' in '{line}'");
        }

        Assert.All(Lines(errorOut), line => Assert.NotEqual(' ', line[0]));
    }

    [Fact]
    public void Import_ExistingOutputWithoutForce_IsAnOperationalFailure()
    {
        using var project = ImportProjectFixture.Create();
        var output = project.OutputPath();
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "operator.txt"), "operator");
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(
            ["import", "--manifest", project.ManifestPath, "--out", output], standardOut, errorOut, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal("operator", File.ReadAllText(Path.Combine(output, "operator.txt")));
        Assert.Contains(Lines(errorOut), line => line.StartsWith("Error GEO-STO-005", StringComparison.Ordinal));
    }

    [Fact]
    public void Import_WithForce_PublishesAndReportsTheRetainedBackup()
    {
        using var project = ImportProjectFixture.Create();
        var output = project.OutputPath();
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "old.bin"), "old");
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(
            ["import", "--manifest", project.ManifestPath, "--out", output, "--force"],
            standardOut,
            errorOut,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(output, "payload.json")));
        Assert.Contains(Lines(standardOut), line => line.StartsWith("retained backup: ", StringComparison.Ordinal));
    }

    [Fact]
    public void Import_CancelledToken_ExitsOneWithoutPublishingAnything()
    {
        using var project = ImportProjectFixture.Create();
        var output = project.OutputPath();
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();
        using var source = new CancellationTokenSource();
        source.Cancel();

        var exitCode = GeoImportCli.Run(
            ["import", "--manifest", project.ManifestPath, "--out", output],
            standardOut,
            errorOut,
            source.Token);

        Assert.Equal(1, exitCode);
        Assert.False(Directory.Exists(output));
        Assert.Equal("cancelled: the import was cancelled before the payload was published", Assert.Single(Lines(errorOut)));
        Assert.Empty(Directory.GetDirectories(project.Root, "*.tmp-*"));
    }

    [Fact]
    public void Report_ValidPayload_PrintsTheSummaryAndExitsZero()
    {
        using var project = ImportProjectFixture.Create();
        var output = project.OutputPath();
        Assert.Equal(0, GeoImportCli.Run(["import", "--manifest", project.ManifestPath, "--out", output], new StringWriter(), new StringWriter(), CancellationToken.None));

        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(["report", "--payload", output], standardOut, errorOut, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, errorOut.ToString());

        var lines = Lines(standardOut);

        Assert.Contains(lines, line => line.StartsWith("payloadSha256: ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line == "chunks: 1 (1 x 1)");
        Assert.Contains(lines, line => line == "imagery: 1 pixels per sample");
        Assert.Contains(lines, line => line.StartsWith("features: ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("palette: ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("checks: ", StringComparison.Ordinal));

        using var reader = GeneratedMapPayloadReader.Open(output, new GeoDiagnosticBag());
        Assert.NotNull(reader);
        var zeroCell = reader!.Report.Masks.ZeroCellMappedPolygonFeatureCount;
        Assert.Contains(lines, line => line == $"masks: {zeroCell} zero-cell mapped polygon features");
    }

    [Fact]
    public void Report_MissingRequiredArgument_IsAUsageFailure()
    {
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(["report"], standardOut, errorOut, CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, standardOut.ToString());
        Assert.StartsWith("usage: geoimport", errorOut.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Report_InvalidPayload_IsAnOperationalFailure()
    {
        using var project = ImportProjectFixture.Create(manifest: false);
        var payload = Path.Combine(project.Root, "not-a-payload");
        Directory.CreateDirectory(payload);
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(["report", "--payload", payload], standardOut, errorOut, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.True(errorOut.ToString().Length > 0);
        Assert.All(Lines(errorOut), line => Assert.True(line.Split(' ').Length >= 4));
    }

    [Fact]
    public void Report_CancelledToken_ExitsOne()
    {
        using var project = ImportProjectFixture.Create();
        var output = project.OutputPath();
        Assert.Equal(0, GeoImportCli.Run(["import", "--manifest", project.ManifestPath, "--out", output], new StringWriter(), new StringWriter(), CancellationToken.None));

        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();
        using var source = new CancellationTokenSource();
        source.Cancel();

        var exitCode = GeoImportCli.Run(["report", "--payload", output], standardOut, errorOut, source.Token);

        Assert.Equal(1, exitCode);
        Assert.Equal("cancelled: the report was cancelled before it was read", Assert.Single(Lines(errorOut)));
    }

    [Fact]
    public void DiagnosticFormatting_NormalizesWhitespaceAndKeepsTheTokenOrder()
    {
        var diagnostic = new GeoDiagnostic(
            "GEO-TEST-001",
            GeoStage.Parse,
            GeoSeverity.Error,
            "line one\r\nline two\twith   runs\u00A0inside",
            "sources/x.kml");

        var line = GeoImportCli.FormatDiagnostic(diagnostic);

        Assert.Equal("Error GEO-TEST-001 Parse line one line two with runs inside", line);
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.DoesNotContain('\t', line);
    }

    [Fact]
    public void NormalizeWhitespace_CollapsesAllRunsAndTrimsEdges()
    {
        Assert.Equal(string.Empty, GeoImportCli.NormalizeWhitespace(null));
        Assert.Equal(string.Empty, GeoImportCli.NormalizeWhitespace(" \r\n\t "));
        Assert.Equal("a b", GeoImportCli.NormalizeWhitespace(" a\tb "));
        Assert.Equal("a b c", GeoImportCli.NormalizeWhitespace("a\r\n\r\n b\u000Bc"));
    }

    private static string[] Lines(StringWriter writer)
    {
        return writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private static int CountLines(StringWriter writer)
    {
        return Lines(writer).Length;
    }
}
