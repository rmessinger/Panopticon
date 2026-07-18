// P1: LocalSkirmishManager offline boot — a single-player skirmish starts without
// networking and reaches the first logic tick. See implementation-checklist.md (P1).
//
// Run with: dotnet test src --filter "FullyQualifiedName~LocalSkirmishManagerBoot"

using System;
using System.Threading.Tasks;
using OpenSage.Data;
using OpenSage.Input;
using OpenSage.Network;
using OpenSage.Tools.ReplaySketch.Services;
using Xunit;

namespace OpenSage.Tests.Network;

// Always-on (CI) checks of the start transition that do not require game files.
public class LocalSkirmishManagerStartTests : MockedGameTest
{
    [Fact, Trait("Category", "Roadmap")]
    public async Task HandleStartButton_SetsEchoConnectionAndReadyToStart()
    {
        var skirmish = new LocalSkirmishManager(Generals);
        Generals.SkirmishManager = skirmish;

        Assert.Equal(SkirmishGameStatus.Configuring, skirmish.Settings.Status);

        await skirmish.HandleStartButtonClickAsync();

        // The local path must not open a network connection: it uses EchoConnection.
        Assert.Equal(SkirmishGameStatus.ReadyToStart, skirmish.Settings.Status);
        Assert.NotNull(skirmish.Connection);
        Assert.IsType<EchoConnection>(skirmish.Connection);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void Constructor_LocalSlotIsHumanAndMarkedLocal()
    {
        var skirmish = new LocalSkirmishManager(Generals);

        Assert.Equal(0, skirmish.Settings.LocalSlotIndex);
        Assert.Equal(SkirmishSlotState.Human, skirmish.Settings.LocalSlot.State);
    }
}

// Full headless boot through the LocalSkirmishManager -> StartGame -> first logic tick path.
// Requires an installed game (map + INI assets); skips cleanly on CI without one, mirroring
// the P0 harness interactive tests.
public class LocalSkirmishManagerBootTests
{
    private const string TestMap = "maps/alpine assault/alpine assault.map";

    [Fact, Trait("Category", "Roadmap"), Trait("Category", "Interactive")]
    public async Task LocalSkirmishManager_BootsHeadlessly_ToStartedAndFirstLogicTick()
    {
        var installationService = new GeneralsInstallationService();
        if (!installationService.TryGetInstallation(out var installation) || installation is null)
        {
            return; // No game installation available on this machine/CI — skip.
        }

        Platform.Start();
        try
        {
            using var game = new Game(
                installation,
                preferredBackend: null,
                new Configuration { NoAudio = true },
                window: null);

            var skirmish = new LocalSkirmishManager(game);
            game.SkirmishManager = skirmish;

            // One human (slot 0, set by the constructor) + one AI slot.
            skirmish.Settings.MapName = TestMap;
            skirmish.Settings.Slots[1].State = SkirmishSlotState.MediumArmy;

            await skirmish.HandleStartButtonClickAsync();
            Assert.Equal(SkirmishGameStatus.ReadyToStart, skirmish.Settings.Status);
            Assert.IsType<EchoConnection>(skirmish.Connection);

            game.StartRun();

            // Driving Update() pumps LocalLogicTick -> SkirmishManager.Update(), which calls
            // StartGame() (status -> Started) and then runs the first logic tick in the same call.
            var started = false;
            for (var i = 0; i < 20 && game.IsRunning; i++)
            {
                game.Update(Array.Empty<InputMessage>());
                if (!started && skirmish.Settings.Status == SkirmishGameStatus.Started)
                {
                    started = true;
                }
            }

            Assert.True(started, "Skirmish never reached Started status.");
            Assert.Equal(SkirmishGameStatus.Started, skirmish.Settings.Status);

            // Boot produced a scene + order stream with the EchoConnection (no network).
            Assert.True(game.InGame);
            Assert.NotNull(game.Scene3D);
            Assert.NotNull(game.NetworkMessageBuffer);

            // First logic tick actually executed: the logic frame counter advanced past zero.
            var logicFrame = (uint)(game.MapTime.TotalTime.TotalMilliseconds /
                                    game.GameEngine.MsPerLogicFrame);
            Assert.True(logicFrame >= 1, $"First logic tick did not run (logicFrame={logicFrame}).");

            // Players spawned: neutral + civilian + human + AI, and LocalPlayer is the human.
            var players = game.Scene3D.Players;
            Assert.True(players.Count >= 4, $"Expected >=4 players after boot, got {players.Count}.");
            Assert.NotNull(game.Scene3D.LocalPlayer);
        }
        finally
        {
            Platform.Stop();
        }
    }
}
