#nullable enable

using System;
using Panopticon.Data;
using Xunit;

namespace Panopticon.Tests.InstalledAssets;

/// <summary>
/// Applied to tests requiring installed game assets.
/// When the game installation is missing under normal mode, the test explicitly skips with a recorded reason.
/// Under strict mode (<c>PANOPTICON_STRICT_INSTALLED=1</c>), the test remains active so that missing or
/// vacuous execution fails the test run.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class InstalledAssetsFactAttribute : FactAttribute
{
    public SageGame Game { get; }

    public InstalledAssetsFactAttribute(SageGame game = SageGame.CncGenerals)
    {
        Game = game;

        if (!InstalledAssetsEnvironment.IsStrict(out _) &&
            !InstalledAssetsEnvironment.TryGetInstallation(game, out _, out var skipReason))
        {
            Skip = skipReason;
        }
    }
}
