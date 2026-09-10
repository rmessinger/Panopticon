using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Recording;
using OpenSage.Simulation;
using Xunit;

namespace OpenSage.Tests.Simulation;

public sealed class SimulationDomainModelTests : MockedGameTest
{
    private static (MockedGameTest.TestGame Game, RecordingDomainFake Domain) CreateGameWithFakeDomain()
    {
        var game = new TestGame(SageGame.CncGenerals);
        return (game, new RecordingDomainFake());
    }

    [Fact]
    public void Host_RequiresDomainModel_ExposedAsProperty()
    {
        var (game, domain) = CreateGameWithFakeDomain();
        var host = new SimulationHost(game.GameEngine, domain);

        Assert.Same(domain, host.DomainModel);
    }

    [Fact]
    public void Host_InitializesDomain_AtConstruction()
    {
        var (game, domain) = CreateGameWithFakeDomain();
        var host = new SimulationHost(game.GameEngine, domain);

        Assert.Equal(["initialize:True"], domain.Calls);
    }

    [Fact]
    public void Host_LifecycleCallbacks_ExecuteInDocumentedOrder()
    {
        var (game, domain) = CreateGameWithFakeDomain();
        var host = new SimulationHost(game.GameEngine, domain);

        host.Run(2);
        host.Run(3, h => h.Frame >= 1);

        Assert.Equal(
        [
            "initialize:True",
            "run-starting",
            "run-complete:2",
            "run-starting",
            "run-complete:1",
        ], domain.Calls);
    }
}
