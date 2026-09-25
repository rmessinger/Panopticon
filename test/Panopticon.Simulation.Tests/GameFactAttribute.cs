using System;
using System.Linq;
using Xunit;
using Xunit.Sdk;

namespace Panopticon.Tests;

[XunitTestCaseDiscoverer("Panopticon.Tests.GameTestDiscoverer", "Panopticon.Simulation.Tests")]
[AttributeUsage(AttributeTargets.Method)]
public sealed class GameFactAttribute : FactAttribute
{
    public readonly SageGame[] Games;

    public GameFactAttribute(SageGame game, params SageGame[] otherGames)
    {
        Games = new[] { game }.Union(otherGames).ToArray();
    }
}
