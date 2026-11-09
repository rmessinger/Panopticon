#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Generals;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Content.Importing.Units.Packages;
using Panopticon.Data;
using Panopticon.Data.Map;
using Panopticon.IO;
using Panopticon.Logic;
using Panopticon.Network;
using Panopticon.Tests.Data;
using Panopticon.Tests.InstalledAssets;
using Panopticon.Tools.H5Migrate;
using Panopticon.Tools.ReplaySketch.Assertions;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.TestHarness;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-H7-03: ReplaySketch/P0 new-format scenario proof.
/// Filter fragment: H7ScenarioProof.
///
/// Compares a legacy variant (legacy .map plus INI roster) and a converted variant (converted H3
/// map package directory loaded through Game.LoadMap package branch plus converted H4 unit package
/// published into a scoped store layer) of the same fight scenario run headlessly at the same seed
/// with the same P0 StateAssertion set. Proves equal end states, identical outcomes, and explicit
/// loaded-path proof without fallback substitution.
/// </summary>
[Collection("SerialGameTests")]
public sealed class H7ScenarioProofTests
{
    private readonly ITestOutputHelper _output;

    public H7ScenarioProofTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void SyntheticProof_P0StateAssertions_EvaluateIdenticallyOnExpectedPaths()
    {
        var expectedPaths = BehavioralParityRunner.DefaultAssertionPaths;
        Assert.NotEmpty(expectedPaths);
        Assert.Contains("outcome.status", expectedPaths);
        Assert.Contains("outcome.reason", expectedPaths);
        Assert.Contains("outcome.winningPlayer", expectedPaths);
        Assert.Contains("outcome.decidedFrame", expectedPaths);
        Assert.Contains("player[2].unitCount", expectedPaths);
        Assert.Contains("player[3].unitCount", expectedPaths);

        var assertionsA = expectedPaths
            .Select(path => new StateAssertion { Id = path, Path = path, Op = ">=", Value = 0 })
            .ToList();
        var assertionsB = expectedPaths
            .Select(path => new StateAssertion { Id = path, Path = path, Op = ">=", Value = 0 })
            .ToList();

        Assert.Equal(assertionsA.Count, assertionsB.Count);
        for (var i = 0; i < assertionsA.Count; i++)
        {
            Assert.Equal(assertionsA[i].Path, assertionsB[i].Path);
            Assert.Equal(assertionsA[i].Op, assertionsB[i].Op);
            Assert.Equal(assertionsA[i].Value, assertionsB[i].Value);
        }
    }

    [InstalledAssetsFact(SageGame.CncGenerals)]
    [Trait("Category", InstalledAssetsEnvironment.CategoryName)]
    public void SameSeed_LegacyAndConvertedScenario_ParityProof()
    {
        var installation = InstalledAssetsEnvironment.RequireInstallation(SageGame.CncGenerals);
        Assert.NotNull(installation);

        var tempRoot = NewTempRoot();
        try
        {
            var scenario = BehavioralParityScenario.Load(
                Path.Combine(AppContext.BaseDirectory, "h5-parity-scenario.json"));

            var artifactsDirectory = Path.Combine(tempRoot, "artifacts");
            var mapPackageDirectory = Path.Combine(artifactsDirectory, "converted-map");
            var unitPackageDirectory = Path.Combine(artifactsDirectory, "converted-units");
            Directory.CreateDirectory(artifactsDirectory);
            Directory.CreateDirectory(mapPackageDirectory);
            Directory.CreateDirectory(unitPackageDirectory);

            // ── Extract and convert the legacy map to H3 map package directory ──
            var sourceMapDiskPath = Path.Combine(artifactsDirectory, "source.map");
            using (var fileSystem = installation.CreateFileSystem())
            {
                var entry = fileSystem.GetFile(scenario.SourceMapPath);
                Assert.NotNull(entry);
                using var sourceStream = entry.Open();
                using var targetStream = File.Create(sourceMapDiskPath);
                sourceStream.CopyTo(targetStream);
            }

            using (var mapOut = new StringWriter())
            using (var mapErr = new StringWriter())
            {
                var mapExit = H5MigrateCli.Run(
                    ["convert-map", "--input", sourceMapDiskPath, "--out", mapPackageDirectory],
                    mapOut,
                    mapErr,
                    CancellationToken.None);
                Assert.True(mapExit == 0, $"convert-map failed (exit {mapExit}): {mapErr}{mapOut}");
            }

            // ── Convert the P6b reference roster to H4 unit package directory ──
            using (var iniOut = new StringWriter())
            using (var iniErr = new StringWriter())
            {
                var iniExit = H5MigrateCli.Run(
                    ["convert-ini", "--p6b", "--game-path", installation.Path, "--out", unitPackageDirectory],
                    iniOut,
                    iniErr,
                    CancellationToken.None);
                Assert.True(iniExit == 0, $"convert-ini failed (exit {iniExit}): {iniErr}{iniOut}");
            }

            // ── Predetermined legitimate provenance allowlist validation ──
            using (var sourceMapStream = File.OpenRead(sourceMapDiskPath))
            {
                var sourceMap = MapFile.FromStream(sourceMapStream);
                var mapReport = RoundTripValidator.ValidateMap(sourceMap, sourceMapDiskPath, mapPackageDirectory);
                Assert.True(
                    mapReport.Passed,
                    $"Map round-trip validator failed under provenance allowlist: {string.Join("; ", mapReport.Sections.SelectMany(s => s.Findings).Select(f => $"{f.Code} {f.Message}"))}");
            }

            using (var installFileSystem = new CompositeFileSystem(new DiskFileSystem(installation.Path), new BigFileSystem(installation.Path)))
            {
                var expected = LoadExpectedResolution(installFileSystem, BehavioralParityRunner.P6bRosterNames);
                var provider = new IniRetainedPayloadProvider(installFileSystem);
                var unitReport = RoundTripValidator.ValidateUnits(expected.Resolution, expected.Adapter.Natives, provider, unitPackageDirectory);
                Assert.True(
                    unitReport.Passed,
                    $"Unit round-trip validator failed under provenance allowlist: {string.Join("; ", unitReport.Sections.SelectMany(s => s.Findings).Select(f => $"{f.Code} {f.Message}"))}");
            }

            // ── Execute both variants through the behavioral equivalence harness ──
            var reportPath = Path.Combine(tempRoot, "h7-parity-report.json");
            var options = new BehavioralParityOptions(artifactsDirectory, reportPath);
            var runnerExit = BehavioralParityRunner.Run(installation, scenario, options, new RecordingReporter(_output));
            Assert.True(runnerExit == 0, "Behavioral parity execution failed with non-zero exit code.");

            Assert.True(File.Exists(reportPath), "Parity report JSON was not written.");
            using var reportDoc = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = reportDoc.RootElement;
            Assert.True(root.GetProperty("allPassed").GetBoolean(), "Parity report did not pass all assertions.");

            var parity = root.GetProperty("parity");
            Assert.True(parity.GetProperty("allPassed").GetBoolean(), "Parity comparisons between legacy and converted diverged.");

            var comparisons = parity.GetProperty("comparisons");
            Assert.True(comparisons.GetArrayLength() >= 10, $"Expected >= 10 parity comparisons, got {comparisons.GetArrayLength()}.");
            foreach (var comp in comparisons.EnumerateArray())
            {
                Assert.True(
                    comp.GetProperty("passed").GetBoolean(),
                    $"Divergence in comparison '{comp.GetProperty("name").GetString()}': expected={comp.GetProperty("expected").GetString()}, actual={comp.GetProperty("actual").GetString()}");
            }

            // ── Explicit loaded-path proof assertions ──
            var playerSettings = new[]
            {
                new PlayerSetting(1, "America", MassBattlePlacement.ParseColor("1", 0), 0, PlayerOwner.Player, "ProofPlayerA"),
                new PlayerSetting(2, "America", MassBattlePlacement.ParseColor("2", 1), 1, PlayerOwner.Player, "ProofPlayerB"),
            };

            // Legacy side loaded-path verification
            using (var gameLegacy = new Game(installation, null, new Configuration { NoAudio = true, SkipStartingBuildings = true }, null))
            {
                var initialDepth = gameLegacy.AssetStore.ScopeDepth;
                gameLegacy.StartSkirmishOrMultiPlayerGame(scenario.SourceMapPath, new EchoConnection(), playerSettings, 1, isMultiPlayer: false);
                Assert.Equal(initialDepth, gameLegacy.AssetStore.ScopeDepth);
                Assert.True(
                    gameLegacy.CurrentMapPath.EndsWith(".map", StringComparison.OrdinalIgnoreCase),
                    $"Expected legacy map path ending in .map, got: {gameLegacy.CurrentMapPath}");
            }

            // Converted side loaded-path verification
            string loadedPathProof;
            using (var gameConverted = new Game(installation, null, new Configuration { NoAudio = true, SkipStartingBuildings = true }, null))
            {
                var baseDepth = gameConverted.AssetStore.ScopeDepth;
                var publishError = PublishUnitPackage(gameConverted, unitPackageDirectory, out var packageRecordCount);
                Assert.Null(publishError);
                Assert.Equal(baseDepth + 1, gameConverted.AssetStore.ScopeDepth);

                gameConverted.StartSkirmishOrMultiPlayerGame(mapPackageDirectory, new EchoConnection(), playerSettings, 1, isMultiPlayer: false);
                Assert.Equal(
                    Path.GetFullPath(mapPackageDirectory),
                    Path.GetFullPath(gameConverted.CurrentMapPath));

                Assert.NotNull(gameConverted.AssetStore.ObjectDefinitions.GetByName("AmericaTankCrusader"));
                Assert.NotNull(gameConverted.AssetStore.ObjectDefinitions.GetByName("AmericaInfantryRanger"));

                loadedPathProof = $"Loaded-path proof: map package '{mapPackageDirectory}' served map via Game.LoadMap (package branch); H4 unit package '{unitPackageDirectory}' served {packageRecordCount} units via adapter 'H4UnitPackage' (publication depth {gameConverted.AssetStore.ScopeDepth})";
                _output.WriteLine(loadedPathProof);
            }

            InstalledAssetsEnvironment.RecordCoverage(
                _output,
                "H7-03 legacy and converted scenario parity proof",
                executedCount: 1,
                skippedCount: 0,
                loadedPathProof);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string? PublishUnitPackage(Game game, string unitPackageDirectory, out int recordCount)
    {
        recordCount = 0;
        var reader = new H4UnitPackageReader(unitPackageDirectory);
        var roots = new List<string> { "DefaultThingTemplate" };
        roots.AddRange(BehavioralParityRunner.P6bRosterNames);
        var set = reader.Read(roots);
        var readError = set.Diagnostics.FirstOrDefault(d => d.IsError);
        if (readError is not null)
        {
            return $"package read failed: {readError.Code}: {readError.Message}";
        }

        var resolution = UnitImportResolution.Resolve(set, GeneralsUnitAdapter.CreateRegistry());
        if (!resolution.Succeeded)
        {
            return "package resolution failed: "
                + string.Join("; ", resolution.Diagnostics.Where(d => d.IsError).Select(d => d.Message));
        }

        var publisher = new UnitContentPublisher(game.AssetStore);
        var context = new UnitMaterializationContext(
            new UnitConversionPolicy(SageGame.CncGenerals),
            publisher.Lookup,
            reader.Natives,
            game.AssetStore);
        var materialized = UnitRuntimeMaterializer.Materialize(resolution, context);
        if (!materialized.Succeeded)
        {
            return "package materialization failed: "
                + string.Join("; ", materialized.Diagnostics.Where(d => d.IsError).Select(d => d.Message));
        }

        var publication = publisher.Publish(materialized, null, materialized.Assets.Keys.ToList());
        if (!publication.Succeeded)
        {
            return "package publication failed: "
                + string.Join("; ", publication.Diagnostics.Where(d => d.IsError).Select(d => d.Message));
        }

        recordCount = materialized.Assets.Count;
        return null;
    }

    private static LoadedRoster LoadExpectedResolution(FileSystem installFileSystem, IReadOnlyList<string> rosterRoots)
    {
        var staging = GeneralsIniLoader.Load(installFileSystem);
        var adapter = new GeneralsUnitAdapter(staging, SageGame.CncGenerals);
        var roots = new List<string> { "DefaultThingTemplate" };
        roots.AddRange(rosterRoots);
        var set = adapter.Read(roots);
        var resolution = UnitImportResolution.Resolve(set, GeneralsUnitAdapter.CreateRegistry());
        Assert.True(
            resolution.Succeeded,
            string.Join("; ", resolution.Diagnostics.Where(diagnostic => diagnostic.IsError).Select(diagnostic => diagnostic.Message)));
        return new LoadedRoster(adapter, resolution);
    }

    private sealed class RecordingReporter(ITestOutputHelper output) : ITestHarnessReporter
    {
        public void Info(string message) => output.WriteLine(message);

        public void Error(string message) => output.WriteLine(message);

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

    private sealed record LoadedRoster(GeneralsUnitAdapter Adapter, UnitImportResolution Resolution);

    private static string NewTempRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonH7Tests", "proof_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }
}
