using System;
using System.Numerics;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Tests;
using Panopticon.Tests.Data;
using Xunit;

namespace Panopticon.Tests.Graphics.Cameras;

// Contract: the zoom settle step toward desiredZoom is clamped at the remaining distance.
// The step scales with frames elapsed, so on long frames (low fps) the raw step overshoots
// the target and, past a gain of 2, oscillates with growing amplitude (runaway trackpad zoom).

[Collection("SerialGameTests")]
public class TacticalViewZoomSettleTests : IDisposable
{
    private Game? _game;

    private Game CreateGame()
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);

        Platform.Start();
        var game = new Game(installation, null, new Configuration { NoAudio = true, SkipStartingBuildings = true }, null);

        var playerSettings = new PlayerSetting[]
        {
            new(0, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.EasyAi, "USA"),
            new(1, "FactionChina", new ColorRgb(0, 255, 0), 0, PlayerOwner.EasyAi, "China"),
            new(2, "FactionGLA", new ColorRgb(0, 0, 255), 0, PlayerOwner.EasyAi, "GLA"),
        };
        game.StartSkirmishOrMultiPlayerGame("maps/alpine assault/alpine assault.map", new EchoConnection(), playerSettings, 1, isMultiPlayer: false);
        game.StartRun();
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        foreach (var player in game.Scene3D.Players)
        {
            player.DisableAI();
        }

        game.EnableFixedLogicTimestep();
        return game;
    }

    private static TimeInterval Frame(TimeSpan total, double frameMs)
    {
        return new TimeInterval(total, TimeSpan.FromMilliseconds(frameMs));
    }

    [GameFact(SageGame.CncGenerals)]
    public void ZoomSettle_LongFrame_ClampsAtDesiredZoom()
    {
        using var game = _game = CreateGame();
        var view = game.Scene3D.TacticalView;

        // Let one ZoomOut settle; the frozen zoom is the observable desiredZoom.
        view.ZoomOut();
        var total = TimeSpan.Zero;
        for (var i = 0; i < 300; i++)
        {
            total += TimeSpan.FromMilliseconds(30);
            view.Update(Frame(total, 30));
        }

        var desiredZoom = view.Zoom;

        // Recreate the gap without changing the height target, then settle it in one
        // long frame whose gain (CameraAdjustSpeed * frames elapsed) is 4: unclamped,
        // the step overshoots desiredZoom by three times the gap.
        var adjustSpeed = game.AssetStore.GameData.Current.CameraAdjustSpeed;
        view.Zoom = desiredZoom - 0.1f;
        var longFrameMs = 4 / adjustSpeed * SageGame.CncGenerals.MsPerLogicFrame();
        total += TimeSpan.FromMilliseconds(longFrameMs);
        view.Update(Frame(total, longFrameMs));

        Assert.Equal(desiredZoom, view.Zoom, 3);

        // A second long frame must hold the target instead of oscillating away from it.
        total += TimeSpan.FromMilliseconds(longFrameMs);
        view.Update(Frame(total, longFrameMs));

        Assert.Equal(desiredZoom, view.Zoom, 3);
    }

    public void Dispose()
    {
        _game?.Dispose();
        Platform.Stop();
    }
}
