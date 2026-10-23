using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.IO;
using Panopticon.Mathematics;
using Panopticon.Terrain;
using Xunit;

namespace Panopticon.Tests;

/// <summary>
/// A1-3 logic-tick extraction: the stepper advances the logic frame counter with no
/// <c>Game.Render</c> call and no presentation systems (WI-A1-05).
/// </summary>
public class LogicStepperTests : MockedGameTest
{
    private static (MockedGameTest.TestGame Game, LogicStepper Stepper) CreateStepperGame()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var stepper = new LogicStepper(game);
        return (game, stepper);
    }

    [Fact]
    public void AdvanceLogic_AdvancesFrameCount_ByNumberOfSteps()
    {
        var (game, stepper) = CreateStepperGame();

        var startFrame = game.GameLogic.CurrentFrame.Value;
        const int steps = 10;

        // Fixed-timestep map time: one MsPerLogicFrame per step, matching
        // Game.EnableFixedLogicTimestep.
        var interval = TimeSpan.FromMilliseconds(game.GameEngine.MsPerLogicFrame);
        for (var i = 1; i <= steps; i++)
        {
            stepper.AdvanceLogic(interval * i);
        }

        Assert.Equal(startFrame + steps, game.GameLogic.CurrentFrame.Value);
    }

    [Fact]
    public void AdvanceLogic_DoesNotTick_WhenPaused()
    {
        var (game, stepper) = CreateStepperGame();

        var startFrame = game.GameLogic.CurrentFrame.Value;
        game.IsLogicRunning = false;

        var interval = TimeSpan.FromMilliseconds(game.GameEngine.MsPerLogicFrame);
        for (var i = 1; i <= 5; i++)
        {
            stepper.AdvanceLogic(interval * i);
        }

        Assert.Equal(startFrame, game.GameLogic.CurrentFrame.Value);
    }

    [Fact]
    public void Reset_RestoresInitialSchedule()
    {
        var (game, stepper) = CreateStepperGame();

        var interval = TimeSpan.FromMilliseconds(game.GameEngine.MsPerLogicFrame);
        stepper.AdvanceLogic(interval);

        stepper.Reset();

        // After Reset, time zero is due immediately: one tick fires for map time zero.
        var startFrame = game.GameLogic.CurrentFrame.Value;
        stepper.AdvanceLogic(TimeSpan.Zero);
        Assert.Equal(startFrame + 1, game.GameLogic.CurrentFrame.Value);
    }

    // WI-N0-01: logic-thread path search time is emitted as a nested span after ai and resets
    // per tick, mirroring the ai decision span.
    [Fact]
    public void LogicTick_EmitsPathSpanAndResetsPerTick()
    {
        var navigation = CreateFixtureNavigation(out var start, out var end);
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var stepper = new LogicStepper(game);
        var spans = new List<KeyValuePair<string, TimeSpan>>();
        stepper.SystemSpan += (system, span) => spans.Add(new KeyValuePair<string, TimeSpan>(system, span));

        var interval = TimeSpan.FromMilliseconds(game.GameEngine.MsPerLogicFrame);
        navigation.CalculatePath(start, end, out _);
        stepper.AdvanceLogic(interval);

        // The tick that consumed the accumulated search time reports it under path, after ai.
        Assert.Contains(spans, s => s.Key == "path" && s.Value > TimeSpan.Zero);
        Assert.True(IndexOf(spans, "ai") < IndexOf(spans, "path"), "path span must be emitted after ai");

        // A tick with no path work resets the accumulator: the emitted span is zero, not the
        // previous tick's value.
        spans.Clear();
        stepper.AdvanceLogic(interval * 2);
        var pathSpan = spans[IndexOf(spans, "path")].Value;
        Assert.Equal(TimeSpan.Zero, pathSpan);
    }

    // WI-N0-01: without a span sink the accumulator is still consumed, never leaked.
    [Fact]
    public void LogicTick_ConsumesPathSpanWithoutSink()
    {
        var navigation = CreateFixtureNavigation(out var start, out var end);
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var stepper = new LogicStepper(game);

        var interval = TimeSpan.FromMilliseconds(game.GameEngine.MsPerLogicFrame);
        navigation.CalculatePath(start, end, out _);
        stepper.AdvanceLogic(interval);
        stepper.AdvanceLogic(interval * 2);

        Assert.Equal(TimeSpan.Zero, navigation.ConsumePathSpan());
    }

    private static int IndexOf(List<KeyValuePair<string, TimeSpan>> spans, string system)
    {
        for (var i = 0; i < spans.Count; i++)
        {
            if (spans[i].Key == system)
            {
                return i;
            }
        }

        return -1;
    }

    private static Panopticon.Navigation.Navigation CreateFixtureNavigation(out System.Numerics.Vector3 start, out System.Numerics.Vector3 end)
    {
        using var fileSystem = new DiskFileSystem(
            Path.Combine(System.Environment.CurrentDirectory, "Data", "Map", "Assets"));
        var mapFile = MapFile.FromFileSystemEntry(fileSystem.GetFile("BlendTileData_CliffTextures.map"));
        Assert.NotNull(mapFile.BlendTileData);
        Assert.NotNull(mapFile.HeightMapData);
        var heightMap = new HeightMap(mapFile.HeightMapData);
        start = heightMap.GetPosition(1, 1);
        end = heightMap.GetPosition(heightMap.Width - 2, heightMap.Height - 2);
        return new Panopticon.Navigation.Navigation(mapFile.BlendTileData, heightMap);
    }
}
