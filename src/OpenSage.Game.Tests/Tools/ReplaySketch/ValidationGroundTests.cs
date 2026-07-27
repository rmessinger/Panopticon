using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenSage.Data;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using OpenSage.Mods.Generals;
using OpenSage.Tests.Data;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;
using Xunit;
using Xunit.Abstractions;

namespace OpenSage.Tests.Tools.ReplaySketch;

// P9-8: validation-ground test coverage. Install-free tests cover the engine-free logic (layout
// determinism, roster completeness, action-mapping totality, baseline comparison live in the
// sibling P9-1/2/4/7 test classes). The [GameFact] test is the live authority: it verifies the
// roster and baseline are valid against a real install, and the full end-to-end spawn+measure run
// is owned by TestHarness (BuiltInScenario.UnitValidationGround), documented in its AGENTS.md.
public class ValidationGroundTests
{
    private readonly ITestOutputHelper _output;

    public ValidationGroundTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static readonly string BaselinePath = FindBaselineFile();

    private static string FindBaselineFile()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "OpenSage.Tools.ReplaySketch.Core",
                "Profiles", "unit-validation-ground-baseline.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return "unit-validation-ground-baseline.json";
    }

    [Fact, Trait("Category", "Roadmap")]
    public void ReportWriter_EmitsTableAndDistinctSections()
    {
        var report = new ValidationGroundReport();
        report.Timings.Add(new UnitTiming("UnitA", 2, PrimaryActionKind.Attack, 40));
        report.Timeouts.Add(new UnitTimeout("UnitB", 3, PrimaryActionKind.Move, 120));
        report.Gaps.Add(new UnitGap("UnitC", 4, PrimaryActionKind.SpecialPower, "unimplemented"));

        var text = ValidationGroundReportWriter.WriteTextReport(report, baseline: null);

        Assert.Contains("UnitA", text);
        Assert.Contains("TIMEOUT", text);
        Assert.Contains("GAP", text);
        Assert.Contains("unimplemented", text);
        Assert.Contains("Summary:", text);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CommittedBaseline_CoversEveryCatalogUnit()
    {
        var path = BaselinePath;
        Assert.True(File.Exists(path), $"Baseline file not found at {path}.");

        var baseline = UnitTimingBaseline.Load(path);

        foreach (var (unit, _) in PrimaryActionCatalog.AllEntries())
        {
            var factionIndex = unit.DefinitionName.StartsWith("America") ? 2
                : unit.DefinitionName.StartsWith("China") ? 3 : 4;
            Assert.True(baseline.Find(unit.DefinitionName, factionIndex) != null,
                $"Baseline missing entry for {unit.DefinitionName} (faction {factionIndex}).");
        }
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CommittedBaseline_HasPositiveGoldenFramesAndDeliberateTolerance()
    {
        var baseline = UnitTimingBaseline.Load(BaselinePath);
        Assert.NotEmpty(baseline.Entries);
        Assert.All(baseline.Entries, e =>
        {
            Assert.True(e.GoldenFrames > 0);
            Assert.True(e.TolerancePercent > 0);
            Assert.True(e.MinimumFrameFloor > 0);
        });
    }

    // The live authority. Requires a real Generals install; skipped in CI without one. Verifies
    // every UnitCatalog definition name resolves in the real asset store, so an invalid name fails
    // here rather than silently dropping the unit from the ground. The full spawn+measure run is
    // driven by TestHarness (BuiltInScenario.UnitValidationGround).
    [GameFact(SageGame.CncGenerals)]
    public void EveryCatalogUnit_ResolvesAgainstRealInstall()
    {
        var rootFolder = InstalledFilesTestData.GetInstallationDirectory(SageGame.CncGenerals);
        var installation = new GameInstallation(new GeneralsDefinition(), rootFolder);

        Platform.Start();
        try
        {
            using var game = new Game(installation);
            foreach (var (unit, _) in PrimaryActionCatalog.AllEntries())
            {
                var resolved = game.AssetStore.ObjectDefinitions.GetByName(unit.DefinitionName);
                Assert.True(resolved != null,
                    $"UnitCatalog definition '{unit.DefinitionName}' does not resolve in the real install.");
            }
        }
        finally
        {
            Platform.Stop();
        }
    }

    // Data-grounded accuracy gate for the baseline: every Attack entry's golden frame count must be
    // consistent with the unit's primary WeaponTemplate as authored in the install's INI. A unit
    // cannot reduce target health before its weapon's PreAttackDelay, so a golden value below that
    // delay is impossible and means the capture is wrong (no matter how reproducible). Requires a
    // real install; skipped in CI without one. See TestHarness/AGENTS.md "Verifying accuracy".
    [GameFact(SageGame.CncGenerals)]
    public void BaselineGoldenFrames_AreConsistentWithWeaponIniData()
    {
        var baseline = UnitTimingBaseline.Load(BaselinePath);
        var rootFolder = InstalledFilesTestData.GetInstallationDirectory(SageGame.CncGenerals);
        var installation = new GameInstallation(new GeneralsDefinition(), rootFolder);

        Platform.Start();
        var impossible = new List<string>();
        try
        {
            using var game = new Game(installation);
            foreach (var entry in baseline.Entries.Where(e => e.ActionKind == "Attack"))
            {
                var definition = game.AssetStore.ObjectDefinitions.GetByName(entry.DefinitionName);
                if (definition == null)
                {
                    impossible.Add($"{entry.DefinitionName}: definition does not resolve in the install.");
                    continue;
                }

                var weapon = ResolvePrimaryWeapon(definition);
                if (weapon == null)
                {
                    impossible.Add($"{entry.DefinitionName}: no primary WeaponTemplate in the install.");
                    continue;
                }

                var preAttack = weapon.PreAttackDelay.Min.Value;
                var delayBetweenShots = weapon.CoolDownDelayBetweenShots.Min.Value;
                _output.WriteLine(
                    $"{entry.DefinitionName,-32} golden={entry.GoldenFrames,-4} preAttack={preAttack,-4} " +
                    $"delayBetweenShots={delayBetweenShots,-4} range={weapon.AttackRange}");

                if (entry.GoldenFrames < preAttack)
                {
                    impossible.Add(
                        $"{entry.DefinitionName}: golden {entry.GoldenFrames} < weapon PreAttackDelay {preAttack}; " +
                        "the unit cannot damage a target that fast, so the baseline value is wrong.");
                }
            }
        }
        finally
        {
            Platform.Stop();
        }

        Assert.Empty(impossible);
    }

    private static WeaponTemplate? ResolvePrimaryWeapon(ObjectDefinition definition)
    {
        if (!definition.WeaponSets.TryGetValue(new BitArray<WeaponSetConditions>(), out var set))
        {
            return null;
        }

        return set.Slots[(int)WeaponSlot.Primary]?.Weapon?.Value;
    }
}
