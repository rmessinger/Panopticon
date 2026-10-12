using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Data;
using Panopticon.Logic;
using Panopticon.Network;
using Panopticon.Tests.Data;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Model.Capture;
using Panopticon.Tools.TestHarness;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.Units;

/// <summary>
/// Card WI-M2-04: the EE2 reference contracts run to completion in a real game session beside
/// native Generals content, and the machine-readable report names every result. Requires
/// CNC_GENERALS_PATH and EMPIRE_EARTH_II_PATH; no EE2 asset is committed.
/// </summary>
[Trait("Category", "Interactive")]
[Collection("SerialGameTests")]
public sealed class Ee2ContractValidationTests(ITestOutputHelper output)
{
    private static Ee2ValidationProfile Profile()
    {
        return new Ee2ValidationProfile
        {
            Name = "ee2-reference-validation-tests",
            ContentImport = new CaptureContentImport
            {
                Source = "ee2",
                RowKeys = [.. Ee2RosterPublicationTests.PublishedRowKeys],
                Region = "we",
                GameUnitsPerSourceDistance = Ee2ReferenceScale.GameUnitsPerSourceDistance,
                GameUnitsPerSourceModelUnit = Ee2ReferenceScale.GameUnitsPerSourceModelUnit,
                ExcludedMechanics = [.. Ee2InventoryTests.ReferenceExclusions],
            },
            Contracts =
            [
                new Ee2ContractEntry { Definition = "ee2:LightMounted1UpgradeEpoch15", Kind = Ee2ContractKind.Ground },
                new Ee2ContractEntry { Definition = "ee2:HeavyMounted1UpgradeEpoch15", Kind = Ee2ContractKind.Ground },
                new Ee2ContractEntry { Definition = "ee2:HeavyArtilleryUpgradeEpoch14", Kind = Ee2ContractKind.Ground },
                new Ee2ContractEntry { Definition = "ee2:FighterUpgradeEpoch15", Kind = Ee2ContractKind.FixedWing },
                new Ee2ContractEntry { Definition = "ee2:BomberUpgradeEpoch15", Kind = Ee2ContractKind.FixedWing },
                new Ee2ContractEntry { Definition = "ee2:LightMounted2UpgradeEpoch13", Kind = Ee2ContractKind.Helicopter },
            ],
        };
    }

    [Fact]
    public void ReferenceContractsPassAndTheReportNamesEveryResult()
    {
        var ee2Path = Environment.GetEnvironmentVariable(UnitContentImportRunner.Ee2PathVariable);
        Assert.False(
            string.IsNullOrEmpty(ee2Path),
            $"Unmet prerequisite: {UnitContentImportRunner.Ee2PathVariable} must point at an Empire Earth II install.");

        using var game = new Game(
            InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
            preferredBackend: null,
            config: new Configuration { NoAudio = true, SkipStartingBuildings = true },
            window: null);
        game.StartSkirmishOrMultiPlayerGame(
            "maps/Dark Mountain/Dark Mountain.map",
            new EchoConnection(),
            [new(0, "FactionAmerica", new Panopticon.Mathematics.ColorRgb(255, 0, 0), 0, PlayerOwner.EasyAi, "USA"),
             new(1, "FactionChina", new Panopticon.Mathematics.ColorRgb(0, 255, 0), 1, PlayerOwner.EasyAi, "China")],
            1,
            isMultiPlayer: false);
        game.StartRun();
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        foreach (var player in game.Scene3D.Players)
        {
            player.DisableAI();
        }

        game.EnableFixedLogicTimestep();

        var reporter = new CollectingReporter();
        Assert.True(UnitContentImportRunner.Import(game, Profile().ContentImport, reporter), string.Join("\n", reporter.Errors));

        var report = Ee2ContractPhase.RunContracts(game, Profile(), reporter);

        foreach (var line in reporter.Lines.Where(line => line.StartsWith("[ee2]") || line.StartsWith("EE2")))
        {
            output.WriteLine(line);
        }

        // The deterministic contracts pass outright: ground displacement, weapon-caused damage,
        // and death for every reference entry, plus the helicopter's return and second sortie.
        // The fixed-wing rearm cycle (return, refill, second damaging sortie) is asserted green
        // since WI-M2-06: airborne 2D friction and the dedicated taxi locomotor removed the
        // divergence and the parking-flow deadlocks that made it report-only.
        foreach (var result in report.Contracts)
        {
            Assert.True(result.Moved != false, $"{result.Definition}: displacement not observed");
            Assert.True(result.DamagedTarget == true, $"{result.Definition}: no weapon-caused damage");
            Assert.True(result.Died == true, $"{result.Definition}: death not observed");
        }

        var helicopter = report.Contracts.Single(c => c.Definition.Contains("LightMounted2"));
        Assert.True(helicopter.Passed);
        Assert.True(helicopter.ReturnedToBase == true);
        Assert.True(helicopter.DamagedSecondSortie == true);
        Assert.True(helicopter.AmmunitionAbsentInSource == true);

        foreach (var fixedWing in report.Contracts.Where(c => c.Definition.Contains("Fighter") || c.Definition.Contains("Bomber")))
        {
            Assert.True(fixedWing.ConsumedAmmunition == true, $"{fixedWing.Definition}: ammunition never consumed");
            Assert.True(fixedWing.ReturnedToBase == true, $"{fixedWing.Definition}: return to base not observed");
            Assert.True(fixedWing.ClipRefilled == true, $"{fixedWing.Definition}: clip did not refill");
            Assert.True(fixedWing.DamagedSecondSortie == true, $"{fixedWing.Definition}: no damage on the second sortie");
        }

    }

    [Fact]
    public void FailureReasonsAndSourceAbsencesAreReportedVerbatim()
    {
        // Synthetic checks of the report shape: no install needed, no game boot.
        var failed = new Ee2ContractResult("ee2:Missing", "ammunition never consumed (clip 32); no damage on the first sortie");
        var absentAmmo = new Ee2ContractResult("ee2:LightMounted2UpgradeEpoch13") { AmmunitionAbsentInSource = true };

        Assert.False(failed.Passed);
        Assert.Equal(2, failed.FailureReason.Split("; ").Length);
        Assert.True(absentAmmo.Passed);
        Assert.True(absentAmmo.AmmunitionAbsentInSource);
    }

    private sealed class CollectingReporter : ITestHarnessReporter
    {
        public List<string> Lines { get; } = [];

        public List<string> Errors { get; } = [];

        public void Info(string message)
        {
            Lines.Add(message);
        }

        public void Error(string message)
        {
            Lines.Add(message);
            Errors.Add(message);
        }

        public void ActionDispatched(ActionType actionType, string? playerName, uint frame)
        {
        }

        public void ActionCompleted(ActionType actionType, string? playerName, uint frame)
        {
        }

        public void MatchOutcomeDecided(OutcomeStatus status, OutcomeReason reason, string? winnerName, uint frame)
        {
        }
    }
}
