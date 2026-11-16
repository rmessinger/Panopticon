using System;
using System.IO;
using Xunit;
using Xunit.Sdk;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// Game-fact flavor of the Sedan install-backed proof: identical discovery wiring to
/// <see cref="GameFactAttribute"/> (its base is sealed, so the fact metadata is replicated),
/// plus an explicit discovery skip when the operator fixture payload is not mounted, so the
/// MCP container reports a skip instead of a failure. The proof itself runs host-local.
/// </summary>
[XunitTestCaseDiscoverer("Panopticon.Tests.GameTestDiscoverer", "Panopticon.Simulation.Tests")]
[AttributeUsage(AttributeTargets.Method)]
internal sealed class SedanOperatorFixtureFactAttribute : FactAttribute
{
    internal const string OperatorFixturePath = "/mnt/pit2/panopticon/geo-proof/r2-03-footprints-payload-v2";

    public readonly SageGame[] Games;

    public SedanOperatorFixtureFactAttribute(SageGame game, params SageGame[] otherGames)
    {
        Games = [game, .. otherGames];

        if (!Directory.Exists(OperatorFixturePath))
        {
            Skip = $"Operator fixture not mounted: {OperatorFixturePath} (Sedan install-backed proof runs host-local; MCP container does not mount operator fixtures)";
        }
    }
}
