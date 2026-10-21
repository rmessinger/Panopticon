using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Panopticon.Data;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Tests.Data;
using Panopticon.Tools.TestHarness;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Tools.TestHarness;

// WI-M1-01: live run of the capacity probe on a scaled roster (38 ground units + 2 Aurora jets per
// side on the deepdivide deployment zones) proving the scripted general attack end to end: ground
// armies engage and take losses, every jet launches through the real taxi/takeoff flow, and a
// recovered jet launches again (the rearm observation). The full 1000 vs 1000 run is the
// TestHarness built-in scenario, not a test. Serial-game collection: real Game instances collide
// on static state with parallel Game-creating classes.

[Trait("Category", "Interactive")]
[Collection("SerialGameTests")]
public class MassBattleCapacityProbeScenarioTests
{
    private readonly ITestOutputHelper _output;

    public MassBattleCapacityProbeScenarioTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [GameFact(SageGame.CncGenerals)]
    public void AttackMove_HoldsInRangeAndResumesForBothSides_WhileMoveToKeepsAdvancing()
    {
        Platform.Start();
        try
        {
            using var game = new Game(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals), null,
                new Configuration { NoAudio = true, SkipStartingBuildings = true }, null);
            PlayerSetting[] players =
            [
                new(0, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.EasyAi, "Red"),
                new(1, "FactionAmerica", new ColorRgb(0, 0, 255), 1, PlayerOwner.EasyAi, "Blue"),
            ];
            game.StartSkirmishOrMultiPlayerGame(
                "maps/alpine assault/alpine assault.map", new EchoConnection(), players, 1, isMultiPlayer: false);
            game.StartRun();
            game.EnableFixedLogicTimestep();
            game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
            foreach (var player in game.Scene3D.Players)
            {
                player.DisableAI();
            }

            var scene = game.Scene3D;
            for (var index = 0; index < 4; index++)
            {
                var side = index % 2;
                var definition = game.AssetStore.ObjectDefinitions.GetByName(
                    index < 2 ? "AmericaInfantryRanger" : "AmericaTankPaladin");
                var unit = scene.GameObjects.CreateObject(definition, scene.Players[2 + side]);
                var enemy = scene.GameObjects.CreateObject(definition, scene.Players[3 - side]);
                var start = new Vector3(600, 600 + 500 * index, 0);
                unit.UpdateTransform(start, null);
                enemy.UpdateTransform(start + new Vector3(50, 0, 0), null);
                unit.UpdateColliders();
                enemy.UpdateColliders();
                var player = unit.Owner;
                player.SelectUnits([unit]);
                var attackMove = new Order(scene.GetPlayerIndex(player), OrderType.AttackMove);
                attackMove.AddPositionArgument(start + new Vector3(300, 0, 0));
                var processor = new OrderProcessor(game);
                processor.Process([attackMove]);
                Assert.NotEmpty(unit.AIUpdate!.TargetPoints);
                Assert.Contains(enemy.Owner, unit.Owner.Enemies);
                Assert.Contains(enemy, scene.Quadtree.FindNearby(unit, unit.Transform, unit.CurrentWeapon!.Template.AttackRange));
                Assert.True(unit.CurrentWeapon.IsTargetInRange(new WeaponTarget(scene.GameObjects, enemy.Id)));
                unit.AIUpdate.Update();

                Assert.True(unit.CurrentWeapon!.HasValidTarget);
                Assert.Equal(enemy.Id, unit.CurrentWeapon.CurrentTarget.GetTargetObject()!.Id);
                Assert.NotEmpty(unit.AIUpdate.TargetPoints);
                Assert.Equal(0f, unit.Speed);
                Assert.False(unit.ModelConditionFlags.Get(ModelConditionFlag.Moving));

                enemy.BodyModule.SetMaxHealth(10000f, MaxHealthChangeType.FullyHeal);
                var healthBefore = enemy.BodyModule.Health;
                var heldPosition = unit.Translation;
                for (var frame = 0; frame < 60; frame++)
                {
                    game.Update([]);
                }

                Assert.Equal(heldPosition.X, unit.Translation.X);
                Assert.Equal(heldPosition.Y, unit.Translation.Y);
                Assert.False(unit.ModelConditionFlags.Get(ModelConditionFlag.Moving));
                Assert.True(enemy.BodyModule.Health < healthBefore, "Attack-move must fire while holding position.");

                enemy.UpdateTransform(start + new Vector3(1000, 0, 0), null);
                enemy.UpdateColliders();
                unit.AIUpdate.Update();
                Assert.NotEmpty(unit.AIUpdate.TargetPoints);
                Assert.True(unit.ModelConditionFlags.Get(ModelConditionFlag.Moving));

                enemy.UpdateTransform(start + new Vector3(50, 0, 0), null);
                enemy.UpdateColliders();
                unit.AIUpdate.Update();
                Assert.False(unit.ModelConditionFlags.Get(ModelConditionFlag.Moving));

                var move = new Order(scene.GetPlayerIndex(player), OrderType.MoveTo);
                move.AddPositionArgument(start + new Vector3(300, 0, 0));
                processor.Process([move]);
                unit.AIUpdate.Update();
                Assert.NotEmpty(unit.AIUpdate.TargetPoints);
                Assert.True(unit.ModelConditionFlags.Get(ModelConditionFlag.Moving));

                processor.Process([attackMove]);
                unit.AIUpdate.Update();
                Assert.False(unit.ModelConditionFlags.Get(ModelConditionFlag.Moving));
                enemy.Kill();
                unit.AIUpdate.Update();
                Assert.NotEmpty(unit.AIUpdate.TargetPoints);
                Assert.True(unit.ModelConditionFlags.Get(ModelConditionFlag.Moving));

                unit.AIUpdate.Stop();
                unit.AIUpdate.Update();
                Assert.Empty(unit.AIUpdate.TargetPoints);
            }
        }
        finally
        {
            Platform.Stop();
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void ScaledProbe_RunsGeneralAttackWithSortiesAndLosses()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mass-battle-capacity-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var profilePath = Path.Combine(directory, "profile.json");
        var reportPath = Path.Combine(directory, "report.json");
        File.WriteAllText(profilePath, ScaledProfileJson);

        var config = new TestHarnessConfig
        {
            Headless = true,
            MassBattleCapacityProbeProfilePath = profilePath,
            MassBattleCapacityProbeReportPath = reportPath,
            MassBattleCapacityProbeFrameBudget = 5400,
        };

        int exitCode;
        try
        {
            exitCode = MassBattleCapacityProbeRunner.Run(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                config,
                new OutputReporter(_output));
        }
        finally
        {
            Directory.Delete(directory, true);
        }

        Assert.Equal(0, exitCode);
    }

    [GameFact(SageGame.CncGenerals)]
    public void ScaledProbe_AsyncAnchorRecordsLatencies()
    {
        // WI-B4-06: the async-path scale anchor wiring, proven at scaled size. The full
        // M1-scale anchor run is the TestHarness scenario, not a test.
        var directory = Path.Combine(Path.GetTempPath(), "mass-battle-capacity-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var profilePath = Path.Combine(directory, "profile.json");
        var reportPath = Path.Combine(directory, "report.json");
        File.WriteAllText(profilePath, ScaledProfileJson);

        var config = new TestHarnessConfig
        {
            Headless = true,
            MassBattleCapacityProbeProfilePath = profilePath,
            MassBattleCapacityProbeReportPath = reportPath,
            MassBattleCapacityProbeFrameBudget = 5400,
            MassBattleCapacityProbeAsyncAnchorBurstEveryFrames = 300,
            MassBattleCapacityProbeAsyncAnchorRequestsPerBurst = 4,
        };

        int exitCode;
        JsonDocument report;
        try
        {
            exitCode = MassBattleCapacityProbeRunner.Run(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                config,
                new OutputReporter(_output));
            report = JsonDocument.Parse(File.ReadAllText(reportPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }

        Assert.Equal(0, exitCode);

        var anchor = report.RootElement.GetProperty("asyncPathAnchor");
        Assert.Equal(68, anchor.GetProperty("submitted").GetInt32());
        Assert.True(anchor.GetProperty("applied").GetInt32() > 0, "at least one anchor request should apply");
        Assert.True(anchor.GetProperty("medianWallMs").GetDouble() > 0);
    }

    [GameFact(SageGame.CncGenerals)]
    public void ScaledProbe_ArmyAiRunsHierarchyWithoutScriptedGroundOrders()
    {
        // WI-M4-03: both armies under the hierarchical policy with the scripted ground attack
        // disabled (jets keep the M1 sortie loop). The squad invariant must hold for the whole
        // engagement and the armies must still close and take losses through the AI's own
        // orders, which the report's allPassed exit checks (contact, losses, sorties).
        var directory = Path.Combine(Path.GetTempPath(), "mass-battle-capacity-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var profilePath = Path.Combine(directory, "profile.json");
        var reportPath = Path.Combine(directory, "report.json");
        File.WriteAllText(profilePath, ScaledProfileJson);

        var config = new TestHarnessConfig
        {
            Headless = true,
            MassBattleCapacityProbeProfilePath = profilePath,
            MassBattleCapacityProbeReportPath = reportPath,
            MassBattleCapacityProbeFrameBudget = 5400,
            MassBattleCapacityProbeArmyAi = true,
        };

        int exitCode;
        JsonDocument report;
        try
        {
            exitCode = MassBattleCapacityProbeRunner.Run(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                config,
                new OutputReporter(_output));
            report = JsonDocument.Parse(File.ReadAllText(reportPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }

        Assert.Equal(0, exitCode);

        var squadInvariant = report.RootElement.GetProperty("squadInvariant");
        var sides = squadInvariant.GetProperty("sides");
        Assert.Equal(2, sides.GetArrayLength());
        foreach (var side in sides.EnumerateArray())
        {
            Assert.True(side.GetProperty("groundSquads").GetInt32() > 0, "each side should field ground squads");
            Assert.Equal(0, side.GetProperty("unassignedEligibleGround").GetInt32());
            Assert.Equal(0, side.GetProperty("maxUnassignedEligibleGround").GetInt32());
            Assert.Equal(0, side.GetProperty("commanderlessSquads").GetInt32());
            Assert.Equal(0, side.GetProperty("maxCommanderlessSquads").GetInt32());
        }

        // WI-M4-04: path requests per decision tick are bounded by squads plus blocked followers
        // (WI-B4-04), not by units.
        Assert.True(
            squadInvariant.GetProperty("decisionTicksObserved").GetInt32() > 0,
            "army-AI mode should observe decision ticks");
        var maxRequests = squadInvariant.GetProperty("maxPathRequestsPerDecisionTick").GetInt64();
        var squadsAtWorst = squadInvariant.GetProperty("squadsAtWorstDecisionTick").GetInt32();
        var membersAtWorst = squadInvariant.GetProperty("membersAtWorstDecisionTick").GetInt32();
        var blockedAtWorst = squadInvariant.GetProperty("maxBlockedFollowersPerDecisionTick").GetInt64();
        Assert.True(membersAtWorst > squadsAtWorst, "the armies should outnumber their squads");
        Assert.True(
            maxRequests <= squadsAtWorst + blockedAtWorst,
            $"path requests per decision tick ({maxRequests}) should be bounded by squads plus blocked followers ({squadsAtWorst} squads, {blockedAtWorst} blocked)");

        // WI-M4-05: the air commander owns the jets (the M1 scripted sortie loop is off in
        // army-AI mode). All three missions recur across the run, and at least one jet
        // accepted a mission again after recovering, which under AI means it rearmed and
        // re-sortied.
        var airMissions = report.RootElement.GetProperty("airMissions");
        Assert.Equal(2, airMissions.GetArrayLength());
        var strike = 0;
        var patrol = 0;
        var escort = 0;
        foreach (var side in airMissions.EnumerateArray())
        {
            strike += side.GetProperty("strike").GetInt32();
            patrol += side.GetProperty("patrol").GetInt32();
            escort += side.GetProperty("escort").GetInt32();
        }

        Assert.True(strike > 0, "the air commander should assign strike missions");
        Assert.True(patrol > 0, "the air commander should assign patrol missions");
        Assert.True(escort > 0, "the air commander should assign escort missions");
        var multiMissionJets = report.RootElement.GetProperty("airMissions").EnumerateArray()
            .Sum(side => side.GetProperty("jetsWithMultipleMissions").GetInt32());
        Assert.True(multiMissionJets > 0, "at least one jet should accept a second mission after recovery");

        // WI-M4-06: stall detection runs in army-AI mode; a scaled run with contact has no unit
        // idle beyond the authored threshold while enemies remain.
        var stallThreshold = squadInvariant.GetProperty("stallThresholdFrames").GetInt32();
        Assert.True(stallThreshold > 0, "the stall threshold should be authored and reported");
        foreach (var side in squadInvariant.GetProperty("sides").EnumerateArray())
        {
            Assert.Equal(0, side.GetProperty("stalledUnitsAtEnd").GetInt32());
        }
    }

    /// <summary>Forwards runner messages to the test output so failures show their reasons.</summary>
    private sealed class OutputReporter(ITestOutputHelper output) : ITestHarnessReporter
    {
        public void Info(string message) => output.WriteLine(message);

        public void Error(string message) => output.WriteLine($"ERROR: {message}");

        public void ActionDispatched(Panopticon.Tools.ReplaySketch.Model.ActionType actionType, string? playerName, uint frame) =>
            output.WriteLine($"  [dispatched] {actionType} player: {playerName ?? "?"} frame: {frame}");

        public void ActionCompleted(Panopticon.Tools.ReplaySketch.Model.ActionType actionType, string? playerName, uint frame) =>
            output.WriteLine($"  [completed] {actionType} player: {playerName ?? "?"} frame: {frame}");

        public void MatchOutcomeDecided(Panopticon.Logic.OutcomeStatus status, Panopticon.Logic.OutcomeReason reason, string? winnerName, uint frame) =>
            output.WriteLine($"  [match-decided] status: {status} reason: {reason} winner: {winnerName ?? "(shared)"} frame: {frame}");
    }

    // The embedded 1000-per-side profile scaled to a test-sized roster: same map, zones, and
    // airfield anchors; 38 ground units plus 2 parked jets per side.
    private const string ScaledProfileJson = """
        {
          "map": {
            "name": "deepdivide",
            "path": "maps/deepdivide/deepdivide.map",
            "provenance": "scaled test profile (WI-M1-01 scenario test)"
          },
          "sides": [
            {
              "id": "sideA",
              "side": "FactionAmerica",
              "color": "409FFF",
              "zone": { "x": 1900, "y": 370, "width": 2200, "height": 1400 },
              "facingDegrees": 90,
              "airfieldDefinition": "AmericaAirfield",
              "airfieldAnchors": [ { "x": 2450, "y": 150 }, { "x": 3450, "y": 150 } ]
            },
            {
              "id": "sideB",
              "side": "FactionAmerica",
              "color": "FF5040",
              "zone": { "x": 1900, "y": 2250, "width": 2200, "height": 1400 },
              "facingDegrees": -90,
              "airfieldDefinition": "AmericaAirfield",
              "airfieldAnchors": [ { "x": 2450, "y": 3830 }, { "x": 3450, "y": 3830 } ]
            }
          ],
          "capacity": {
            "unitsPerSide": 40,
            "spacingFactor": 2.0,
            "minPitch": 25,
            "blocks": [
              { "definition": "AmericaInfantryRanger", "count": 20 },
              { "definition": "AmericaTankPaladin", "count": 10 },
              { "definition": "AmericaVehicleTomahawk", "count": 8 }
            ],
            "aircraft": { "definition": "AmericaJetAurora", "count": 2 }
          },
          "reachability": {
            "arrivalRadius": 60,
            "maxSpeedSafetyFactor": 2.0,
            "minTimeoutFrames": 900,
            "maxFrameBudget": 30000
          }
        }
        """;
}
