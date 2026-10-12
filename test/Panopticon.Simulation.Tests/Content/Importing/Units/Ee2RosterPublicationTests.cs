using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Ee2;
using Panopticon.Tests.Data;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Model.Capture;
using Panopticon.Tools.TestHarness;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.Units;

/// <summary>
/// Card WI-M2-03: the authored EE2 reference roster publishes beside native Generals content
/// through the scoped publisher, and the blocked infantry entry fails without leaving partial
/// registration or mutating native definitions. Requires CNC_GENERALS_PATH and
/// EMPIRE_EARTH_II_PATH; no EE2 asset is committed.
/// </summary>
[Trait("Category", "Interactive")]
[Collection("SerialGameTests")]
public sealed class Ee2RosterPublicationTests(ITestOutputHelper output)
{
    public static readonly string[] PublishedRowKeys =
    [
        "LightMounted1UpgradeEpoch15",
        "HeavyMounted1UpgradeEpoch15",
        "HeavyArtilleryUpgradeEpoch14",
        "FighterUpgradeEpoch15",
        "BomberUpgradeEpoch15",
        "LightMounted2UpgradeEpoch13",
    ];

    public const string InfantryRowKey = "HeavyInfantry1UpgradeEpoch15";

    public static readonly string[] AllRowKeys = [.. PublishedRowKeys, InfantryRowKey];

    private static CaptureContentImport RosterImport(params string[] rowKeys)
    {
        return new CaptureContentImport
        {
            Source = "ee2",
            RowKeys = [.. rowKeys],
            Region = "we",
            GameUnitsPerSourceDistance = Ee2ReferenceScale.GameUnitsPerSourceDistance,
            GameUnitsPerSourceModelUnit = Ee2ReferenceScale.GameUnitsPerSourceModelUnit,
            ExcludedMechanics = [.. Ee2InventoryTests.ReferenceExclusions],
        };
    }

    /// <summary>
    /// Card WI-M3-01: materializes the reference roster from the install without a Game boot, so
    /// staged module data (for example the draw's model scale) can be asserted directly.
    /// </summary>
    public static Panopticon.Content.Importing.Units.Materialization.MaterializedUnitContent MaterializeReferenceRoster(
        double gameUnitsPerSourceDistance,
        double? gameUnitsPerSourceModelUnit)
    {
        var set = new Ee2UnitAdapter(Ee2InventoryTests.OpenInstall(), new Ee2ImportPolicy(15, "we", Ee2InventoryTests.ReferenceExclusions))
            .ReadRows(AllRowKeys);
        var resolution = UnitImportResolution.Resolve(set, UnitCapabilityRegistry.CreateDefault());
        Assert.True(resolution.Succeeded, string.Join("\n", resolution.Diagnostics.Where(d => d.IsError)));

        var content = Panopticon.Content.Importing.Units.Materialization.UnitRuntimeMaterializer.Materialize(
            resolution,
            new Panopticon.Content.Importing.Units.Materialization.UnitMaterializationContext(
                new Panopticon.Content.Importing.Units.Materialization.UnitConversionPolicy(
                    SageGame.CncGenerals,
                    gameUnitsPerSourceDistance: gameUnitsPerSourceDistance),
                gameUnitsPerSourceModelUnit: gameUnitsPerSourceModelUnit));
        Assert.True(content.Succeeded, string.Join("\n", content.Diagnostics));
        return content;
    }

    private static Game Boot()
    {
        var ee2Path = Environment.GetEnvironmentVariable(UnitContentImportRunner.Ee2PathVariable);
        Assert.False(
            string.IsNullOrEmpty(ee2Path),
            $"Unmet prerequisite: {UnitContentImportRunner.Ee2PathVariable} must point at an Empire Earth II install.");
        Assert.True(System.IO.Directory.Exists(System.IO.Path.Combine(ee2Path!, "zips")), "Unmet prerequisite: the EE2 zips folder was not found.");

        return new Game(InstalledFilesTestData.GetInstallation(SageGame.CncGenerals));
    }

    [Fact]
    public void RosterPublishesBesideGeneralsWithoutTouchingNatives()
    {
        using var game = Boot();

        var nativeBefore = NativeIdentities(game);
        var reporter = new CollectingReporter();

        var published = UnitContentImportRunner.Import(game, RosterImport(PublishedRowKeys), reporter);

        foreach (var line in reporter.Lines)
        {
            output.WriteLine(line);
        }

        Assert.True(published, string.Join("\n", reporter.Errors));

        var ee2 = game.AssetStore.GetSourceContent(ContentSource.Ee2);
        foreach (var rowKey in PublishedRowKeys)
        {
            Assert.NotNull(ee2.ObjectDefinitions.GetByName(rowKey));
            Assert.Null(game.AssetStore.ObjectDefinitions.GetByName(rowKey));
        }

        // Native Generals definitions are the same assets afterwards: identities unchanged.
        Assert.Equal(nativeBefore, NativeIdentities(game));
    }

    [Fact]
    public void InfantryRowPublishesWithEpoch15Clips()
    {
        // WI-M2-05: the epoch-15 infantry clips import (flags 0x0025 accepted;
        // skeleton-superset targets skipped), so the infantry row publishes beside
        // Generals instead of failing explicitly.
        using var game = Boot();

        var nativeBefore = NativeIdentities(game);
        var reporter = new CollectingReporter();

        var published = UnitContentImportRunner.Import(game, RosterImport(InfantryRowKey), reporter);

        foreach (var line in reporter.Lines)
        {
            output.WriteLine(line);
        }

        Assert.True(published, string.Join("\n", reporter.Errors));

        var ee2 = game.AssetStore.GetSourceContent(ContentSource.Ee2);
        Assert.NotNull(ee2.ObjectDefinitions.GetByName(InfantryRowKey));
        Assert.Null(game.AssetStore.ObjectDefinitions.GetByName(InfantryRowKey));
        Assert.Equal(nativeBefore, NativeIdentities(game));
    }

    [Fact]
    public void MissingInstallIsAnExplicitFailure()
    {
        using var game = Boot();
        var root = Environment.GetEnvironmentVariable(UnitContentImportRunner.Ee2PathVariable)!;
        Environment.SetEnvironmentVariable(UnitContentImportRunner.Ee2PathVariable, null);
        try
        {
            var reporter = new CollectingReporter();
            Assert.False(UnitContentImportRunner.Import(game, RosterImport(PublishedRowKeys[0]), reporter));
            Assert.Contains(reporter.Errors, e => e.Contains(UnitContentImportRunner.Ee2PathVariable));
        }
        finally
        {
            Environment.SetEnvironmentVariable(UnitContentImportRunner.Ee2PathVariable, root);
        }
    }

    private static IReadOnlyDictionary<string, uint> NativeIdentities(Game game)
    {
        var names = new[] { "GLAInfantryRebel", "AmericaTankCrusader", "AmericaJetRanger" };
        return names.ToDictionary(
            name => name,
            name => game.AssetStore.ObjectDefinitions.GetByName(name)?.InstanceId ?? 0);
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

        public void MatchOutcomeDecided(Panopticon.Logic.OutcomeStatus status, Panopticon.Logic.OutcomeReason reason, string? winnerName, uint frame)
        {
        }
    }
}
