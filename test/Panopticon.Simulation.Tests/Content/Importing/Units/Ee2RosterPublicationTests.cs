using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Units;
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

    public const string BlockedInfantryRowKey = "HeavyInfantry1UpgradeEpoch15";

    private static CaptureContentImport RosterImport(params string[] rowKeys)
    {
        return new CaptureContentImport
        {
            Source = "ee2",
            RowKeys = [.. rowKeys],
            Region = "we",
            GameUnitsPerSourceDistance = 12,
            ExcludedMechanics = [.. Ee2InventoryTests.ReferenceExclusions],
        };
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
    public void BlockedInfantryRowFailsExplicitlyAndPublishesNothing()
    {
        using var game = Boot();

        var nativeBefore = NativeIdentities(game);
        var reporter = new CollectingReporter();

        var published = UnitContentImportRunner.Import(game, RosterImport(BlockedInfantryRowKey), reporter);

        Assert.False(published);
        Assert.Contains(reporter.Errors, e => e.Contains("HeavyInfantry1_15"));

        // The failure path publishes nothing: no EE2 definition resolves afterwards.
        var ee2 = game.AssetStore.GetSourceContent(ContentSource.Ee2);
        Assert.All(PublishedRowKeys.Append(BlockedInfantryRowKey), rowKey => Assert.Null(ee2.ObjectDefinitions.GetByName(rowKey)));
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
