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

    // Catalog entries that are valid only in Zero Hour. They do not resolve against a vanilla
    // Generals install, so the validation ground reports them as gaps at run time and never
    // measures them (see ValidationGroundHost.SpawnUnits). Every other catalog entry must resolve,
    // so a typo or stale name still fails the gate rather than being silently dropped.
    // Single-sourced from the catalog's ZeroHourOnly flags: entries valid only in Zero Hour
    // (absent from a vanilla Generals install).
    private static readonly HashSet<string> ZeroHourOnlyDefinitions = new(
        PrimaryActionCatalog.AllEntries()
            .Where(e => e.Unit.ZeroHourOnly)
            .Select(e => e.Unit.DefinitionName));

    // Catalog entries permitted to lack a baseline entry. ZeroHourOnlyDefinitions are valid only in
    // Zero Hour (absent from a vanilla Generals install); UnmeasurableAttackReasons are units whose
    // attack the validation-ground sandbox cannot measure (recorded as gaps at run time). Every other
    // catalog unit must have a golden entry, so a stale/missing name still fails the gate. The
    // SpecialPower units are measured since P6c implemented their ability handlers, so they require
    // baseline entries like every other unit.
    private static bool IsExcludedFromBaseline(string definitionName)
    {
        if (ZeroHourOnlyDefinitions.Contains(definitionName))
        {
            return true;
        }

        // Units whose attack the sandbox cannot measure are recorded as gaps and have no entry.
        return PrimaryActionCatalog.UnmeasurableAttackReasons.ContainsKey(definitionName);
    }

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
            if (IsExcludedFromBaseline(unit.DefinitionName))
            {
                continue;
            }

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
    // here rather than silently dropping the unit from the ground. Catalog entries that are valid
    // only in Zero Hour (ZeroHourOnlyDefinitions) are permitted to be absent against vanilla
    // Generals; the validation ground reports those as gaps at run time. The full spawn+measure run
    // is driven by TestHarness (BuiltInScenario.UnitValidationGround).
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
                if (ZeroHourOnlyDefinitions.Contains(unit.DefinitionName))
                {
                    continue;
                }

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
                    // Some units (e.g. GLAVehicleTechnical) only gain a weapon through an upgrade the
                    // static definition does not expose, yet fire in the simulation. The golden was
                    // captured from a real fire, so report and skip rather than treating the missing
                    // template as a baseline error.
                    _output.WriteLine($"{entry.DefinitionName,-32} golden={entry.GoldenFrames,-4} (no resolvable WeaponTemplate; skipped)");
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
        // Prefer the default (empty-condition) weapon set, then fall back to any set's primary
        // slot. Some units (e.g. GLAVehicleTechnical) only expose a weapon under a non-default
        // condition, but still fire one in the simulation, so a single-condition lookup would
        // wrongly report them as having no weapon.
        if (definition.WeaponSets.TryGetValue(new BitArray<WeaponSetConditions>(), out var defaultSet)
            && defaultSet.Slots[(int)WeaponSlot.Primary]?.Weapon?.Value is { } defaultWeapon)
        {
            return defaultWeapon;
        }

        foreach (var set in definition.WeaponSets.Values)
        {
            foreach (var slot in set.Slots)
            {
                if (slot?.Weapon?.Value is { } weapon)
                {
                    return weapon;
                }
            }
        }

        return null;
    }

    // WI-VG-04: every per-unit TargetOverride must be a target the unit's primary weapon can
    // actually damage, verified from the install's INI (weapon damage type versus the override
    // target's armor). If a weapon, armor, or override changes, this fails so the override is
    // re-chosen rather than silently producing a unit that can never destroy its target.
    [GameFact(SageGame.CncGenerals)]
    public void TargetOverrides_AreDamageableByPrimaryWeapon()
    {
        var rootFolder = InstalledFilesTestData.GetInstallationDirectory(SageGame.CncGenerals);
        var installation = new GameInstallation(new GeneralsDefinition(), rootFolder);

        var overrides = new List<(string Unit, string Target)>();
        foreach (var faction in new[] { 2, 3, 4 })
        {
            foreach (var option in UnitCatalog.ForFaction(faction).Where(u => u.TargetOverride != null))
            {
                overrides.Add((option.DefinitionName, option.TargetOverride!));
            }
        }

        Assert.NotEmpty(overrides);

        Platform.Start();
        try
        {
            using var game = new Game(installation);
            foreach (var (unitName, targetName) in overrides)
            {
                var unit = game.AssetStore.ObjectDefinitions.GetByName(unitName);
                Assert.NotNull(unit);

                var target = game.AssetStore.ObjectDefinitions.GetByName(targetName);
                Assert.NotNull(target);

                var weapon = ResolvePrimaryWeapon(unit);
                Assert.NotNull(weapon);

                var damageNugget = weapon.Nuggets.OfType<DamageNugget>().FirstOrDefault();
                Assert.NotNull(damageNugget);

                var armor = target.ArmorSets.FirstOrDefault()?.Armor?.Value;
                Assert.NotNull(armor);

                Assert.False(armor.Values[(int)damageNugget.DamageType].IsZero,
                    $"{unitName}'s primary weapon deals {damageNugget.DamageType} damage, which the " +
                    $"override target {targetName}'s {armor.Name} resists completely; " +
                    "the override cannot be destroyed by this unit.");
            }
        }
        finally
        {
            Platform.Stop();
        }
    }
}
