using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Units;
using Panopticon.Logic.Object;
using Xunit;
using static Panopticon.Tests.Content.Importing.Units.UnitRecordFixtures;

namespace Panopticon.Tests.Content.Importing.Units;

public class UnitImportValidatorTests
{
    private static readonly UnitCapabilityRegistry Registry = new(
        UnitCapabilityRegistry.DefaultTypedModuleFamilies,
        ["SquishCollide"],
        UnitCapabilityRegistry.DefaultAbilityFamilies,
        Source);

    private static UnitImportResolution Resolve(UnitContentSet set) => UnitImportResolution.Resolve(set, Registry);

    private static UnitContentSet Single(SourceRecord record) => new(Source, [record], [record.Id]);

    private static string[] ErrorCodes(UnitImportResolution resolution)
    {
        return resolution.Diagnostics.Where(d => d.IsError).Select(d => d.Code).ToArray();
    }

    [Fact]
    public void ValidSoldierPasses()
    {
        var resolution = Resolve(ValidSoldier());

        Assert.True(resolution.Succeeded, string.Join("\n", resolution.Diagnostics));
        Assert.Equal(4, resolution.Closure.Count);
        Assert.Contains(resolution.Diagnostics, d => d.Code == DiagnosticCodes.NoVisualBinding && !d.IsError);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-5)]
    public void InvalidHealthIsRejected(double health)
    {
        var unit = Unit("U") with { MaxHealth = Authored.Of(Hp(health)) };

        Assert.Equal([DiagnosticCodes.InvalidNumber], ErrorCodes(Resolve(Single(unit))));
    }

    [Fact]
    public void MissingHealthIsRejected()
    {
        Assert.Equal([DiagnosticCodes.MissingRequiredValue], ErrorCodes(Resolve(Single(Unit("U")))));
    }

    [Fact]
    public void WrongUnitIsRejected()
    {
        var unit = Unit("U") with { MaxHealth = Authored.Of(Hp(10)), BuildTime = Authored.Of(Money(5)) };

        Assert.Equal([DiagnosticCodes.UnitMismatch], ErrorCodes(Resolve(Single(unit))));
    }

    [Fact]
    public void InvalidWeaponValuesAreRejected()
    {
        var weapon = Weapon("W") with
        {
            AttackRange = Authored.Of(Distance(0)),
            ReloadTime = Authored.Of(Seconds(double.NaN)),
            MinimumAttackRange = Authored.Of(Distance(50)),
            ClipSize = Authored.Of(-1),
        };

        var codes = ErrorCodes(Resolve(Single(weapon)));

        Assert.Equal(4, codes.Length);
        Assert.All(codes, code => Assert.Equal(DiagnosticCodes.InvalidNumber, code));
    }

    [Fact]
    public void UnsupportedAbilityIsRejected()
    {
        var ability = new AbilityRecord(Id(ContentKind.Ability, "Convert"), At(1)) { Family = Authored.Of("ConvertAttack") };
        var unit = Unit("Priest") with
        {
            MaxHealth = Authored.Of(Hp(50)),
            Modules = [ModuleEdit.Add(new UnitModule("Convert", "Ability", ModuleKinds.SpecialPower, Ability: ability.Id))],
        };

        var resolution = Resolve(new UnitContentSet(Source, [unit, ability], [unit.Id]));

        Assert.Equal([DiagnosticCodes.UnsupportedAbility], ErrorCodes(resolution));
        Assert.False(resolution.Succeeded);
    }

    [Fact]
    public void UnsupportedModuleFamilyIsRejected()
    {
        var unit = Unit("U") with
        {
            MaxHealth = Authored.Of(Hp(10)),
            Modules = [ModuleEdit.Add(new UnitModule("ModuleTag_Era", "EpochAdvance", ModuleKinds.Update))],
        };

        Assert.Equal([DiagnosticCodes.UnsupportedModule], ErrorCodes(Resolve(Single(unit))));
    }

    [Fact]
    public void LegacyBindingAcceptedOnlyFromLegacySource()
    {
        var module = new UnitModule("ModuleTag_Squish", "SquishCollide", ModuleKinds.Collide, Legacy: new LegacyBinding(Source, 0));
        var legacyUnit = Unit("U") with { MaxHealth = Authored.Of(Hp(10)), Modules = [ModuleEdit.Add(module)] };
        var foreignUnit = new UnitRecord(new ContentId(ContentSource.Ee2, ContentKind.Unit, "U"), At(1))
        {
            MaxHealth = Authored.Of(Hp(10)),
            Modules = [ModuleEdit.Add(module)],
        };
        var unbound = Unit("V") with { MaxHealth = Authored.Of(Hp(10)), Modules = [ModuleEdit.Add(module with { Legacy = null })] };

        Assert.Empty(ErrorCodes(Resolve(Single(legacyUnit))));
        Assert.Equal([DiagnosticCodes.LegacyBindingNotAllowed], ErrorCodes(Resolve(new UnitContentSet(ContentSource.Ee2, [foreignUnit], [foreignUnit.Id]))));
        Assert.Equal([DiagnosticCodes.UnsupportedModule], ErrorCodes(Resolve(Single(unbound))));
    }

    [Fact]
    public void UnclassifiedGameplayBlocksAndMetadataWarns()
    {
        var unit = Unit("U") with
        {
            MaxHealth = Authored.Of(Hp(10)),
            Unclassified = [new UnclassifiedField("popCount", AffectsGameplay: true), new UnclassifiedField("VerboseTooltip", AffectsGameplay: false)],
        };

        var resolution = Resolve(Single(unit));

        Assert.Equal([DiagnosticCodes.UnsupportedMechanic], ErrorCodes(resolution));
        Assert.Contains(resolution.Diagnostics, d => d.Code == DiagnosticCodes.NonGameplayMetadata && d.Severity == ImportDiagnosticSeverity.Warning);
    }

    [Fact]
    public void VisualRequiresFourStates()
    {
        var visual = new VisualStateRecord(Id(ContentKind.VisualState, "V"), At(1))
        {
            ModelKey = Authored.Of("model.nif"),
            StateAnimations = new Dictionary<string, Authored<string>> { ["Idle"] = Authored.Of("idle.kf"), ["move"] = Authored.Of("walk.kf") },
        };

        var diagnostics = Resolve(Single(visual)).Diagnostics;

        // Idle, Move, Death, Attack are all validated. A missing Attack clip is a reported
        // warning (WI-M2-02: sources may author none; the draw module binds no condition for
        // it); the other states stay errors.
        Assert.Equal(1, diagnostics.Count(d => d.IsError && d.Code == DiagnosticCodes.MissingVisualState && d.Message.Contains("Death")));
        Assert.Equal(1, diagnostics.Count(d => !d.IsError && d.Code == DiagnosticCodes.MissingVisualState && d.Message.Contains("Attack")));
    }

    [Fact]
    public void UpgradeScopeMustBeKnown()
    {
        var upgrade = new UpgradeRecord(Id(ContentKind.Upgrade, "Era"), At(1)) { Scope = Authored.Of("Epoch") };

        Assert.Equal([DiagnosticCodes.InvalidEnumValue], ErrorCodes(Resolve(Single(upgrade))));
    }

    [Fact]
    public void NegativeArmorMultiplierIsRejected()
    {
        var armor = Armor("A") with { Multipliers = new Dictionary<string, Authored<double>> { ["FLAME"] = Authored.Of(-1.0) } };

        Assert.Equal([DiagnosticCodes.InvalidNumber], ErrorCodes(Resolve(Single(armor))));
    }
}
