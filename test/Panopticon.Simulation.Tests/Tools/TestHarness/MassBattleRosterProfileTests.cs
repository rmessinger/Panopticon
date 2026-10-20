using System;
using System.IO;
using System.Linq;
using Panopticon.Data;
using Panopticon.Logic.Object;
using Panopticon.Tests.Data;
using Panopticon.Tools.TestHarness;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Tools.TestHarness;

// WI-M5-02: roster-mode deployment profiles. The install-free facts pin the validation rules,
// the ee2: definition resolution, and the ground/aircraft partition of a manifest side; the
// Interactive fact spawns a scaled roster on the real deepdivide zones through the shared H2a
// import and parking wiring (needs CNC_GENERALS_PATH and EMPIRE_EARTH_II_PATH).

public sealed class MassBattleRosterProfileTests
{
    [Fact]
    public void Validate_CapacityModeNeedsNoRosters()
    {
        var profile = new MassBattleDeploymentProfile();
        var manifest = new RosterManifest { Sides = [new RosterSide { Id = "a" }] };
        Assert.Null(MassBattleRosterSpawner.Validate(profile, manifest));
    }

    [Fact]
    public void Validate_RosterModeNeedsEverySideAndNoCapacityBlocks()
    {
        var manifest = new RosterManifest
        {
            Sides =
            [
                new RosterSide { Id = "generals-led" },
                new RosterSide { Id = "ee2-led" },
            ],
        };

        var profile = new MassBattleDeploymentProfile
        {
            Sides =
            {
                new MassBattleSideProfile { Id = "sideA", Roster = "generals-led" },
                new MassBattleSideProfile { Id = "sideB", Roster = "ee2-led" },
            },
        };
        Assert.Null(MassBattleRosterSpawner.Validate(profile, manifest));

        var mixed = profile with
        {
            Sides =
            [
                new MassBattleSideProfile { Id = "sideA", Roster = "generals-led" },
                new MassBattleSideProfile { Id = "sideB" },
            ],
        };
        Assert.Contains("every side", MassBattleRosterSpawner.Validate(mixed, manifest));

        var withBlocks = profile with
        {
            Capacity = new MassBattleCapacityProfile
            {
                Blocks = [new MassBattleUnitBlock { Definition = "X", Count = 1 }],
            },
        };
        Assert.Contains("capacity", MassBattleRosterSpawner.Validate(withBlocks, manifest));

        var unknownId = profile with
        {
            Sides =
            [
                new MassBattleSideProfile { Id = "sideA", Roster = "generals-led" },
                new MassBattleSideProfile { Id = "sideB", Roster = "does-not-exist" },
            ],
        };
        Assert.Contains("does-not-exist", MassBattleRosterSpawner.Validate(unknownId, manifest));
    }

    [Fact]
    public void DefinitionName_ResolvesEe2RowsThroughThePublishPrefix()
    {
        Assert.Equal(
            "ee2:HeavyInfantry1UpgradeEpoch15",
            MassBattleRosterSpawner.DefinitionName(new RosterEntry { Definition = "HeavyInfantry1UpgradeEpoch15", Source = "ee2" }));
        Assert.Equal(
            "AmericaTankCrusader",
            MassBattleRosterSpawner.DefinitionName(new RosterEntry { Definition = "AmericaTankCrusader", Source = "generals" }));
    }

    [Fact]
    public void GroundBlocks_AndAircraftEntries_PartitionTheManifestInOrder()
    {
        var side = new RosterSide
        {
            Id = "ee2-led",
            Entries =
            {
                new RosterEntry { Definition = "HeavyInfantry1UpgradeEpoch15", Source = "ee2", Count = 300 },
                new RosterEntry { Definition = "AmericaJetRaptor", Source = "generals", Count = 100, Aircraft = true },
                new RosterEntry { Definition = "FighterUpgradeEpoch15", Source = "ee2", Count = 80, Aircraft = true },
                new RosterEntry { Definition = "GLAVehicleQuadCannon", Source = "generals", Count = 120 },
            },
        };

        var ground = MassBattleRosterSpawner.GroundBlocks(side);
        Assert.Equal(
            [("ee2:HeavyInfantry1UpgradeEpoch15", 300), ("GLAVehicleQuadCannon", 120)],
            ground.Select(block => (block.Definition, block.Count)));

        var air = MassBattleRosterSpawner.AircraftEntries(side);
        Assert.Equal([("AmericaJetRaptor", 100), ("ee2:FighterUpgradeEpoch15", 80)], air);

        Assert.Equal(side.Entries.Count, ground.Count + air.Count);
    }

    [Fact]
    public void CommittedBattleProfile_ValidatesAgainstTheShippedManifest()
    {
        var profilePath = Path.Combine(AppContext.BaseDirectory, MassBattleRosterSpawner.DefaultBattleProfileFileName);
        Assert.True(File.Exists(profilePath), $"battle profile '{profilePath}' should ship beside the test assembly");
        var profile = MassBattleDeploymentProfile.Load(profilePath);

        var manifestPath = Path.Combine(AppContext.BaseDirectory, RosterBudgetEvaluator.DefaultManifestFileName);
        var manifest = RosterBudgetEvaluator.Load(manifestPath);

        Assert.Null(MassBattleRosterSpawner.Validate(profile, manifest));

        // Anchor arithmetic (decision 4): each AmericaAirfield parks 4 aircraft, so every side
        // carries at least ceil(aircraft / 4) anchors.
        foreach (var side in profile.Sides)
        {
            var manifestSide = manifest.Sides.Single(s => s.Id == side.Roster);
            var aircraft = MassBattleRosterSpawner.AircraftEntries(manifestSide).Sum(entry => entry.Count);
            Assert.True(
                side.AirfieldAnchors.Count * 4 >= aircraft,
                $"{side.Id}: {side.AirfieldAnchors.Count} anchors park at most {side.AirfieldAnchors.Count * 4} of {aircraft} aircraft");
        }
    }
}

[Trait("Category", "Interactive")]
public sealed class MassBattleRosterSpawnTests
{
    private readonly ITestOutputHelper _output;

    public MassBattleRosterSpawnTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [GameFact(SageGame.CncGenerals)]
    public void ScaledRosters_SpawnGroundAndParkAircraft_ThroughTheSharedImport()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mass-battle-roster-spawn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var manifestPath = Path.Combine(directory, "manifest.json");

        var profile = MassBattleDeploymentProfile.Load(
            Path.Combine(AppContext.BaseDirectory, MassBattleRosterSpawner.DefaultBattleProfileFileName));
        var manifest = RosterBudgetEvaluator.Load(
            Path.Combine(AppContext.BaseDirectory, RosterBudgetEvaluator.DefaultManifestFileName));

        // Scale every entry to a test-sized roster; ids stay so the profile's roster links hold.
        foreach (var side in manifest.Sides)
        {
            foreach (var entry in side.Entries)
            {
                entry.Count = entry.Aircraft ? 1 : 2;
            }
        }

        // One anchor parks the scaled aircraft (4 slots per airfield).
        profile = profile with
        {
            RosterManifestPath = manifestPath,
            Sides = profile.Sides
                .Select(side => side with { AirfieldAnchors = [side.AirfieldAnchors[0]] })
                .ToList(),
        };
        RosterBudgetEvaluatorSaveManifest(manifest, manifestPath);

        using var game = new Game(InstalledFilesTestData.GetInstallation(SageGame.CncGenerals));
        var playerSettings = profile.Sides.Select((side, index) => new Panopticon.Logic.PlayerSetting(
            index,
            side.Side,
            MassBattlePlacement.ParseColor(side.Color, index),
            index,
            Panopticon.Logic.PlayerOwner.EasyAi,
            side.Id)).ToArray();
        game.StartSkirmishOrMultiPlayerGame(profile.Map.Path, new Panopticon.Network.EchoConnection(), playerSettings, 1, isMultiPlayer: false);
        game.StartRun();
        game.VictorySystem.ConditionRule = new Panopticon.Logic.NeverDefeatedVictoryConditionRule();
        foreach (var player in game.Scene3D.Players)
        {
            player.DisableAI();
        }

        var reporter = new OutputReporter(_output);
        Assert.True(
            MassBattleRosterSpawner.ImportEe2Content(game, profile, manifest, reporter),
            "the manifest's ee2 rows should import through the shared H2a layer");

        var sidePlayers = Panopticon.Logic.AI.Hierarchy.ArmyAiSides.SidePlayers(game.Scene3D.Players, profile.Sides.Count);
        var navigation = game.Scene3D.Navigation;
        for (var i = 0; i < profile.Sides.Count; i++)
        {
            var manifestSide = manifest.Sides.Single(s => s.Id == profile.Sides[i].Roster);
            var result = MassBattleRosterSpawner.SpawnSide(game, profile, i, sidePlayers[i], manifestSide, navigation, reporter);

            Assert.True(result.AllGroundSpawned, $"{result.SideId} ground shortfall: {string.Join("; ", result.GroundEntries.Where(e => e.Spawned != e.Requested).Select(e => $"{e.Definition} {e.Spawned}/{e.Requested}"))}");
            Assert.True(result.AllAircraftParked, $"{result.SideId} parking shortfall: {string.Join("; ", result.AircraftEntries.Where(e => e.Spawned != e.Requested).Select(e => $"{e.Definition} {e.Spawned}/{e.Requested}"))}");
            Assert.Empty(result.FailureReasons);

            foreach (var jet in result.Units.Where(unit => unit.AIUpdate is JetAIUpdate))
            {
                var jetAI = (JetAIUpdate)jet.AIUpdate;
                Assert.NotNull(jetAI.Base);
                Assert.Equal(JetAIUpdate.JetAIState.Parked, jetAI.CurrentJetAIState);
            }
        }

        Directory.Delete(directory, true);
    }

    private static void RosterBudgetEvaluatorSaveManifest(RosterManifest manifest, string path)
    {
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(manifest, RosterBudgetEvaluator.Options));
    }

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
}
