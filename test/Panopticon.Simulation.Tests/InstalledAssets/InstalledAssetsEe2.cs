#nullable enable

using System;
using System.IO;
using Xunit;

namespace Panopticon.Tests.InstalledAssets;

/// <summary>
/// Environment policy for the Empire Earth II install used by H7 InstalledAssets tests. EE2 is
/// not a SageGame in <see cref="Panopticon.Mods.BuiltIn.GameDefinition"/>, so it cannot ride the
/// SageGame-keyed <see cref="InstalledAssetsFactAttribute"/>; the skip and strict rules are the
/// same. Content is located exactly the way the existing EE2 tests locate it: the
/// EMPIRE_EARTH_II_PATH environment variable pointing at an install whose
/// <c>zips/db.zip</c> and <c>zips/graphics.zip</c> archives hold the DDF database.
/// </summary>
public static class InstalledAssetsEe2Environment
{
    public const string PathEnvironmentVariable = "EMPIRE_EARTH_II_PATH";

    public static bool TryGetInstall(out string installRoot, out string skipReason)
    {
        installRoot = Environment.GetEnvironmentVariable(PathEnvironmentVariable) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
        {
            skipReason = $"Empire Earth II installation was not found. Set {PathEnvironmentVariable} pointing at the installation root directory.";
            return false;
        }

        skipReason = string.Empty;
        return true;
    }

    /// <summary>Resolves the required EE2 install; strict mode fails with a named message, otherwise throws.</summary>
    public static string RequireInstall()
    {
        if (TryGetInstall(out var installRoot, out var skipReason))
        {
            return installRoot;
        }

        if (InstalledAssetsEnvironment.IsStrict(out _))
        {
            Assert.Fail($"Strict mode '{InstalledAssetsEnvironment.StrictInstalledEnvironmentVariable}=1' requires an Empire Earth II installation, but it was not found: {skipReason}");
        }

        throw new InvalidOperationException(skipReason);
    }
}

/// <summary>
/// Applied to tests requiring the installed Empire Earth II assets. Missing install skips with a
/// recorded reason under normal mode; strict mode leaves the test active so missing or vacuous
/// execution fails the run.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class InstalledAssetsEe2FactAttribute : FactAttribute
{
    public InstalledAssetsEe2FactAttribute()
    {
        if (!InstalledAssetsEnvironment.IsStrict(out _) &&
            !InstalledAssetsEe2Environment.TryGetInstall(out _, out var skipReason))
        {
            Skip = skipReason;
        }
    }
}
