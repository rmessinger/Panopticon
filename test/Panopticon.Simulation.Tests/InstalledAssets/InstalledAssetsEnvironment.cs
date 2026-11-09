#nullable enable

using System;
using System.IO;
using System.Linq;
using Panopticon.Data;
using Panopticon.Mods.BuiltIn;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.InstalledAssets;

/// <summary>
/// Phase-wide environment and execution policy for tests requiring real game installations.
/// Supports explicit skip reporting when installations are missing and strict enforcement mode
/// under which missing or vacuous execution fails the test run.
/// </summary>
public static class InstalledAssetsEnvironment
{
    /// <summary>
    /// Environment variable enabling strict installed-assets validation mode.
    /// When set to "1" or "true", missing or vacuous executions fail instead of skipping.
    /// </summary>
    public const string StrictInstalledEnvironmentVariable = "PANOPTICON_STRICT_INSTALLED";

    /// <summary>
    /// The xUnit trait category for tests that exercise installed game assets.
    /// </summary>
    public const string CategoryName = "InstalledAssets";

    /// <summary>
    /// Determines whether strict installed validation mode is enabled.
    /// </summary>
    public static bool IsStrict(out string? rawValue)
    {
        rawValue = Environment.GetEnvironmentVariable(StrictInstalledEnvironmentVariable);
        return string.Equals(rawValue, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Probes for the specified game installation through environment variables and registered locators.
    /// </summary>
    public static bool TryGetInstallation(SageGame game, out GameInstallation? installation, out string skipReason)
    {
        var definition = GameDefinition.FromGame(game);
        installation = InstallationLocators.FindAllInstallations(definition).FirstOrDefault();

        if (installation is not null && Directory.Exists(installation.Path))
        {
            skipReason = string.Empty;
            return true;
        }

        installation = null;
        var envVar = $"{definition.Identifier.ToUpperInvariant()}_PATH";
        skipReason = $"Game installation '{definition.DisplayName}' was not found. Set {envVar} pointing to the installation root directory.";
        return false;
    }

    /// <summary>
    /// Resolves the required game installation. If missing in strict mode, fails the test run
    /// with the required failure message; otherwise throws an <see cref="InvalidOperationException"/>.
    /// </summary>
    public static GameInstallation RequireInstallation(SageGame game)
    {
        if (TryGetInstallation(game, out var installation, out var skipReason))
        {
            return installation!;
        }

        if (IsStrict(out _))
        {
            Assert.Fail($"Strict mode '{StrictInstalledEnvironmentVariable}=1' requires game installation '{game}' to be present, but it was not found: {skipReason}");
        }

        throw new InvalidOperationException(skipReason);
    }

    /// <summary>
    /// Emits executed versus skipped coverage counts through the test output helper.
    /// </summary>
    public static void RecordCoverage(ITestOutputHelper output, string suiteName, int executedCount, int skippedCount, string details = "")
    {
        ArgumentNullException.ThrowIfNull(output);
        var detailSuffix = string.IsNullOrWhiteSpace(details) ? string.Empty : $" {details}";
        output.WriteLine($"[{CategoryName}] {suiteName}: Executed={executedCount}, Skipped={skippedCount}.{detailSuffix}");
    }
}
