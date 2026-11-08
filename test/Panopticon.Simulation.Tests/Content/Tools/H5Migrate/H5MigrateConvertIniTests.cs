#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Generals;
using Panopticon.Content.Importing.Units.Packages;
using Panopticon.IO;
using Panopticon.Tests.Data;
using Panopticon.Tools.H5Migrate;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Tools.H5Migrate;

/// <summary>
/// CLI tests for WI-H5-02 `convert-ini`: converts bounded Generals INI rosters into verified H4 unit packages,
/// enforces retained INI block fidelity, and refuses error-carrying resolutions with a per-record report.
/// </summary>
[Collection("SerialGameTests")]
public sealed class H5MigrateConvertIniTests
{
    private readonly ITestOutputHelper _output;

    public H5MigrateConvertIniTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [GameFact(SageGame.CncGenerals)]
    public void ConvertIni_P6bRoster_SucceedsAndPassesAllGates()
    {
        var tempRoot = NewTempRoot();
        var packageDir = Path.Combine(tempRoot, "p6b-package");

        try
        {
            using var standardOut = new StringWriter();
            using var errorOut = new StringWriter();

            var exitCode = H5MigrateCli.Run(
                ["convert-ini", "--p6b", "--out", packageDir],
                standardOut,
                errorOut,
                CancellationToken.None);

            var errText = errorOut.ToString();
            Assert.True(exitCode == 0, $"convert-ini failed (exit {exitCode}): {errText}");
            Assert.Equal(string.Empty, errText);
            Assert.True(Directory.Exists(packageDir));
            Assert.True(File.Exists(Path.Combine(packageDir, UnitPackageFormat.ManifestFileName)));

            var outText = standardOut.ToString();
            Assert.Contains("converted: ", outText, StringComparison.Ordinal);
            Assert.Contains("records: ", outText, StringComparison.Ordinal);
            Assert.Contains("retained: ", outText, StringComparison.Ordinal);

            // Re-read through H4UnitPackageReader and verify equality gates
            var reader = new H4UnitPackageReader(packageDir);
            var pkgSet = reader.Read([]);
            Assert.DoesNotContain(pkgSet.Diagnostics, d => d.IsError);

            var pkgResolution = UnitImportResolution.Resolve(pkgSet, GeneralsUnitAdapter.CreateRegistry());
            Assert.True(pkgResolution.Succeeded, string.Join("; ", pkgResolution.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));

            // Roster closure count matches
            Assert.True(pkgResolution.Closure.Count > 0);

            // Retained block fidelity: every retained block SHA-256 matches the source INI slice
            var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
            using var fileSystem = new CompositeFileSystem(
                new DiskFileSystem(installation.Path),
                new BigFileSystem(installation.Path));
            var provider = new IniRetainedPayloadProvider(fileSystem);

            var retainedCount = 0;
            foreach (var record in pkgResolution.Closure)
            {
                if (!UnitPackageConversionVerifier.RecordNeedsRetention(record))
                {
                    continue;
                }

                retainedCount++;
                var block = provider.GetBlock(new UnitRetainedBlockRequest(record.Id));
                Assert.NotNull(block);

                var relativePath = Path.Combine("retained", UnitPackageConversionVerifier.KindFolder(record.Id.Kind), $"{record.Id.Name}.ini");
                var fullPath = Path.Combine(packageDir, relativePath);
                Assert.True(File.Exists(fullPath), $"Retained file '{relativePath}' missing from package.");

                var actualBytes = File.ReadAllBytes(fullPath);
                var expectedHash = Convert.ToHexString(SHA256.HashData(block!.Bytes)).ToLowerInvariant();
                var actualHash = Convert.ToHexString(SHA256.HashData(actualBytes)).ToLowerInvariant();
                Assert.Equal(expectedHash, actualHash);
                Assert.True(block.Bytes.SequenceEqual(actualBytes));
            }

            Assert.True(retainedCount > 0, "Expected at least one retained record in P6b roster closure.");
            _output.WriteLine($"P6b roster converted successfully: {pkgResolution.Closure.Count} records, {retainedCount} retained blocks verified.");
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void ConvertIni_ExplicitUnit_Succeeds()
    {
        var tempRoot = NewTempRoot();
        var packageDir = Path.Combine(tempRoot, "crusader-package");

        try
        {
            using var standardOut = new StringWriter();
            using var errorOut = new StringWriter();

            var exitCode = H5MigrateCli.Run(
                ["convert-ini", "AmericaTankCrusader", "--out", packageDir],
                standardOut,
                errorOut,
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, errorOut.ToString());
            Assert.True(Directory.Exists(packageDir));
            Assert.True(File.Exists(Path.Combine(packageDir, UnitPackageFormat.ManifestFileName)));
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void ConvertIni_UnresolvableRosterEntry_RefusesWithPerRecordReportAndWritesNothing()
    {
        var tempRoot = NewTempRoot();
        var packageDir = Path.Combine(tempRoot, "unresolvable-pkg");

        try
        {
            using var standardOut = new StringWriter();
            using var errorOut = new StringWriter();

            var exitCode = H5MigrateCli.Run(
                ["convert-ini", "NonexistentObjectXYZ", "--out", packageDir],
                standardOut,
                errorOut,
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.False(Directory.Exists(packageDir));

            var errorText = errorOut.ToString();
            Assert.Contains("record 'generals:Unit:NonexistentObjectXYZ'", errorText, StringComparison.Ordinal);
            Assert.Contains("no package was left at", errorText, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Fact]
    public void ConvertIni_RelativeOutPath_FailsUsage()
    {
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = H5MigrateCli.Run(
            ["convert-ini", "--p6b", "--out", "relative/package/dir"],
            standardOut,
            errorOut,
            CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.StartsWith("usage: h5migrate convert-ini", errorOut.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertIni_NoRosterSpecified_FailsUsage()
    {
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = H5MigrateCli.Run(
            ["convert-ini", "--out", "/tmp/unspecified-roster-package"],
            standardOut,
            errorOut,
            CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.StartsWith("usage: h5migrate convert-ini", errorOut.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertIni_MissingInstallPath_FailsOperational()
    {
        var tempRoot = NewTempRoot();
        var packageDir = Path.Combine(tempRoot, "missing-install-pkg");

        try
        {
            using var standardOut = new StringWriter();
            using var errorOut = new StringWriter();

            var exitCode = H5MigrateCli.Run(
                ["convert-ini", "--p6b", "--game-path", "/nonexistent/install/path/generals", "--out", packageDir],
                standardOut,
                errorOut,
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.False(Directory.Exists(packageDir));

            var errorText = errorOut.ToString();
            Assert.Contains(H5MigrateCliDiagnostics.InstallMissing, errorText, StringComparison.Ordinal);
            Assert.Contains("no package was left at", errorText, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void ConvertIni_Cancelled_LeavesNoPackage()
    {
        var tempRoot = NewTempRoot();
        var packageDir = Path.Combine(tempRoot, "cancelled-pkg");

        try
        {
            using var standardOut = new StringWriter();
            using var errorOut = new StringWriter();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var exitCode = H5MigrateCli.Run(
                ["convert-ini", "--p6b", "--out", packageDir],
                standardOut,
                errorOut,
                cts.Token);

            Assert.Equal(1, exitCode);
            Assert.False(Directory.Exists(packageDir));
            Assert.Contains("cancelled", errorOut.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static string NewTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "h5migrate-ini-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        if (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }
}
