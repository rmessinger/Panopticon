using Panopticon.Logic;
using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Mathematics;
using Panopticon.Tests;
using Xunit;

namespace Panopticon.Tests.Logic;

/// <summary>
/// WI-M5-09: a spectator start makes the replay observer the local player without touching the
/// default fallback, and the army sides keep their AI under a spectated start.
/// </summary>
public sealed class SpectatorStartTests : MockedGameTest
{
    [Fact]
    public void DefaultStart_KeepsFirstHumanSlotAsLocalPlayer()
    {
        var players = ConfigurePlayers(spectator: false);
        Assert.Same(Generals.PlayerManager.Players[2], Generals.PlayerManager.LocalPlayer);
    }

    [Fact]
    public void SpectatorStart_MakesObserverTheLocalPlayer()
    {
        var players = ConfigurePlayers(spectator: true);
        var observer = Assert.Single(Generals.PlayerManager.Players, player => player.Side == "FactionObserver");
        Assert.Same(observer, Generals.PlayerManager.LocalPlayer);
        Assert.Same(players[0], Generals.PlayerManager.Players[2]);
    }

    [Fact]
    public void SpectatorWithoutObserver_KeepsDefaultFallback()
    {
        Generals.Configuration.Spectator = true;
        Generals.PlayerManager.OnNewGame(
            [
                Panopticon.Data.Map.Player.CreateNeutralPlayer(),
                Panopticon.Data.Map.Player.CreateCivilianPlayer(),
                new Panopticon.Data.Map.Player
                {
                    Name = "armyA",
                    Faction = "FactionAmerica",
                    DisplayName = "Army A",
                    IsHuman = true,
                },
                new Panopticon.Data.Map.Player
                {
                    Name = "armyB",
                    Faction = "FactionGLA",
                    DisplayName = "Army B",
                    IsHuman = false,
                },
            ],
            GameType.Skirmish,
            [
                new PlayerSetting(1, "FactionAmerica", new ColorRgb(255, 255, 255), -1, PlayerOwner.Player, "armyA", true),
                new PlayerSetting(2, "FactionGLA", new ColorRgb(255, 128, 64), -1, PlayerOwner.EasyAi, "armyB"),
            ]);

        Assert.Same(Generals.PlayerManager.Players[2], Generals.PlayerManager.LocalPlayer);
    }

    [Fact]
    public void SpectatedArmies_KeepTheirAi()
    {
        var players = ConfigurePlayers(spectator: true);
        var settings = new PlayerSetting[]
        {
            new(1, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.EasyAi, "armyA"),
            new(2, "FactionGLA", new ColorRgb(0, 0, 255), 1, PlayerOwner.EasyAi, "armyB"),
        };
        ArmyAiSides.ApplyPolicies(settings);

        var game = new TestGame(SageGame.CncGenerals);
        game.Configuration.Spectator = true;
        // FromMapData only wires a SkirmishAIPlayer when the faction template resolves.
        AddFactionTemplate(game, "FactionAmerica");
        AddFactionTemplate(game, "FactionGLA");
        game.PlayerManager.OnNewGame(
            [
                Panopticon.Data.Map.Player.CreateNeutralPlayer(),
                Panopticon.Data.Map.Player.CreateCivilianPlayer(),
                new Panopticon.Data.Map.Player { Name = "armyA", Faction = "FactionAmerica", DisplayName = "Army A" },
                new Panopticon.Data.Map.Player { Name = "armyB", Faction = "FactionGLA", DisplayName = "Army B" },
                Observer(),
            ],
            GameType.Skirmish,
            settings);

        var sides = ArmyAiSides.Resolve(game.PlayerManager.Players, ["armyA", "armyB"]);
        Assert.Equal(2, sides.Count);
        Assert.All(sides, side => Assert.NotNull(side.Ai));
        Assert.All(sides, side => Assert.IsType<HierarchicalAIDecisionPolicy>(side.Policy));
        Assert.Same(game.PlayerManager.Players[4], game.PlayerManager.LocalPlayer);
    }

    private Player[] ConfigurePlayers(bool spectator)
    {
        Generals.Configuration.Spectator = spectator;
        Generals.PlayerManager.OnNewGame(
            [
                Panopticon.Data.Map.Player.CreateNeutralPlayer(),
                Panopticon.Data.Map.Player.CreateCivilianPlayer(),
                new Panopticon.Data.Map.Player
                {
                    Name = "armyA",
                    Faction = "FactionAmerica",
                    DisplayName = "Army A",
                    IsHuman = true,
                },
                new Panopticon.Data.Map.Player
                {
                    Name = "armyB",
                    Faction = "FactionGLA",
                    DisplayName = "Army B",
                    IsHuman = false,
                },
                Observer(),
            ],
            GameType.Skirmish,
            [
                new PlayerSetting(1, "FactionAmerica", new ColorRgb(255, 255, 255), -1, PlayerOwner.Player, "armyA", true),
                new PlayerSetting(2, "FactionGLA", new ColorRgb(255, 128, 64), -1, PlayerOwner.EasyAi, "armyB"),
            ]);

        return [Generals.PlayerManager.Players[2], Generals.PlayerManager.Players[3]];
    }

    private static void AddFactionTemplate(TestGame game, string faction)
    {
        var template = new PlayerTemplate();
        typeof(PlayerTemplate)
            .GetMethod("SetNameAndInstanceId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(template, ["PlayerTemplate", faction]);
        game.AssetStore.PlayerTemplates.Add(template);
    }

    private static Panopticon.Data.Map.Player Observer() => new()
    {
        Name = "ReplayObserver",
        Faction = "FactionObserver",
        DisplayName = "Observer",
        IsHuman = false,
    };
}
