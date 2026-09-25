using Panopticon.Logic;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Logic;

public class PlayerManagerTests : MockedGameTest
{
    [Fact]
    public void OnNewGame_UsesLocalPlayerSettingWhenProvided()
    {
        var playerSettings = new[]
        {
            new PlayerSetting(1, "FactionAmerica", new ColorRgb(255, 255, 255), 1, PlayerOwner.Player, "first", isLocalForMultiplayer: true),
            new PlayerSetting(2, "FactionGLA", new ColorRgb(255, 255, 255), 2, PlayerOwner.EasyAi, "second"),
        };

        var manager = new PlayerManager(Generals);
        var mapPlayers = CreateMapPlayers();

        manager.OnNewGame(mapPlayers, GameType.Skirmish, playerSettings);

        Assert.Same(manager.Players[2], manager.LocalPlayer);
    }

    [Fact]
    public void OnNewGame_WiresSameTeamPlayersAsAlliesAndDifferentTeamsAsEnemies()
    {
        var playerSettings = new[]
        {
            new PlayerSetting(1, "FactionAmerica", new ColorRgb(255, 255, 255), 1, PlayerOwner.Player, "first"),
            new PlayerSetting(2, "FactionGLA", new ColorRgb(255, 255, 255), 1, PlayerOwner.EasyAi, "second"),
        };

        var manager = new PlayerManager(Generals);
        var mapPlayers = CreateMapPlayers();

        manager.OnNewGame(mapPlayers, GameType.Skirmish, playerSettings);

        var first = manager.Players[2];
        var second = manager.Players[3];

        Assert.Contains(second, first.Allies);
        Assert.Contains(first, second.Allies);
        Assert.DoesNotContain(second, first.Enemies);
        Assert.DoesNotContain(first, second.Enemies);
    }

    [Fact]
    public void OnNewGame_UnteamedPlayersAreMutualEnemies()
    {
        // -1 is the "no team" sentinel (matches SkirmishSlot's default and PlayerManager.OnNewGame's
        // ally-wiring check). Two players who never had a team assigned must fight each other, not
        // be wired as allies.
        var playerSettings = new[]
        {
            new PlayerSetting(1, "FactionAmerica", new ColorRgb(255, 255, 255), -1, PlayerOwner.EasyAi, "first"),
            new PlayerSetting(2, "FactionGLA", new ColorRgb(255, 255, 255), -1, PlayerOwner.EasyAi, "second"),
        };

        var manager = new PlayerManager(Generals);
        var mapPlayers = CreateMapPlayers();

        manager.OnNewGame(mapPlayers, GameType.Skirmish, playerSettings);

        var first = manager.Players[2];
        var second = manager.Players[3];

        Assert.Contains(second, first.Enemies);
        Assert.Contains(first, second.Enemies);
        Assert.DoesNotContain(second, first.Allies);
        Assert.DoesNotContain(first, second.Allies);
    }


    private static Panopticon.Data.Map.Player[] CreateMapPlayers() =>
    [
        Panopticon.Data.Map.Player.CreateNeutralPlayer(),
        Panopticon.Data.Map.Player.CreateCivilianPlayer(),
        new Panopticon.Data.Map.Player
        {
            Name = "first",
            Faction = "FactionAmerica",
            DisplayName = "First",
            IsHuman = true,
        },
        new Panopticon.Data.Map.Player
        {
            Name = "second",
            Faction = "FactionGLA",
            DisplayName = "Second",
            IsHuman = false,
        },
    ];
}
