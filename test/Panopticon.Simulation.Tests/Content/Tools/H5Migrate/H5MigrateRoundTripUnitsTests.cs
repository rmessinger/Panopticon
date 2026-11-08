#nullable enable

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Panopticon.IO;
using Panopticon.Tests.Data;
using Panopticon.Tools.H5Migrate;
using Xunit;

namespace Panopticon.Tests.Content.Tools.H5Migrate;

/// <summary>
/// Installed-corpus happy path for WI-H5-03 units mode: a P6b package converted by `convert-ini`
/// validates identical through `validate` with the same roster. Runs only with CNC_GENERALS_PATH set.
/// </summary>
[Collection("SerialGameTests")]
public sealed class H5MigrateRoundTripUnitsTests
{
    [GameFact(SageGame.CncGenerals)]
    public void ValidateUnits_P6bPackage_IsIdenticalToItsRoster()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var packageDir = Path.Combine(tempRoot, "p6b-package");

            using (var convertOut = new StringWriter())
            using (var convertErr = new StringWriter())
            {
                var convertExit = H5MigrateCli.Run(
                    ["convert-ini", "--p6b", "--out", packageDir],
                    convertOut,
                    convertErr,
                    CancellationToken.None);

                Assert.True(convertExit == 0, $"convert-ini failed (exit {convertExit}): {convertErr}");
            }

            using var standardOut = new StringWriter();
            using var errorOut = new StringWriter();

            var exitCode = H5MigrateCli.Run(
                ["validate", "--p6b", "--package", packageDir, "--json"],
                standardOut,
                errorOut,
                CancellationToken.None);

            Assert.True(exitCode == 0, $"validate failed (exit {exitCode}): {errorOut}\n{standardOut}");
            Assert.Equal(string.Empty, errorOut.ToString());

            using var document = JsonDocument.Parse(standardOut.ToString());
            var root = document.RootElement;

            Assert.Equal("units", root.GetProperty("mode").GetString());
            Assert.True(root.GetProperty("passed").GetBoolean());
            Assert.NotEmpty(root.GetProperty("records").EnumerateArray());
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
