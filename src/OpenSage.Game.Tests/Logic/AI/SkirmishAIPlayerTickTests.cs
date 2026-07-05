using OpenSage.Logic;
using OpenSage.Logic.AI;
using OpenSage.Mathematics;
using Xunit;

namespace OpenSage.Tests.Logic.AI;

public sealed class SkirmishAIPlayerTickTests : MockedGameTest
{
    [Fact]
    public void LogicTick_TicksSkirmishAIAtDefaultCadence()
    {
        var manager = new PlayerManager(Generals);
        manager.OnNewGame(CreateMapPlayers(), GameType.Skirmish, CreatePlayerSettings(humanIsLocal: true));

        var aiPlayer = Assert.IsType<SkirmishAIPlayer>(manager.Players[3].AIPlayer);
        const int totalFrames = 180;

        for (var frame = 0; frame < totalFrames; frame++)
        {
            manager.Players[3].LogicTick();
        }

        Assert.Equal(totalFrames / (int)AIPlayer.DefaultDecisionCadenceFrames, aiPlayer.DecisionTickCount);
    }

    [Fact]
    public void LogicTick_DoesNotTickScaffoldForPlayersWithoutAI()
    {
        var manager = new PlayerManager(Generals);
        manager.OnNewGame(CreateMapPlayers(), GameType.Skirmish, CreatePlayerSettings(humanIsLocal: true));

        var aiPlayer = Assert.IsType<SkirmishAIPlayer>(manager.Players[3].AIPlayer);
        var humanPlayer = manager.Players[2];
        var neutralPlayer = manager.Players[0];

        Assert.Null(humanPlayer.AIPlayer);
        Assert.Null(neutralPlayer.AIPlayer);

        for (var frame = 0; frame < AIPlayer.DefaultDecisionCadenceFrames * 2; frame++)
        {
            humanPlayer.LogicTick();
            neutralPlayer.LogicTick();
        }

        Assert.Equal(0, aiPlayer.DecisionTickCount);
    }

    private static OpenSage.Data.Map.Player[] CreateMapPlayers() =>
    [
        OpenSage.Data.Map.Player.CreateNeutralPlayer(),
        OpenSage.Data.Map.Player.CreateCivilianPlayer(),
        new OpenSage.Data.Map.Player
        {
            Name = "human",
            Faction = "FactionAmerica",
            DisplayName = "Human",
            IsHuman = true,
        },
        new OpenSage.Data.Map.Player
        {
            Name = "ai",
            Faction = "FactionGLA",
            DisplayName = "AI",
            IsHuman = false,
        },
    ];

    private static PlayerSetting[] CreatePlayerSettings(bool humanIsLocal) =>
    [
        new PlayerSetting(1, "FactionAmerica", new ColorRgb(255, 255, 255), 1, PlayerOwner.Player, "human", humanIsLocal),
        new PlayerSetting(2, "FactionGLA", new ColorRgb(255, 128, 64), 2, PlayerOwner.EasyAi, "ai"),
    ];
}
