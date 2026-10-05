using System.Linq;
using Panopticon.Content.Importing.Units;
using Panopticon.Logic.Object;
using Xunit;
using static Panopticon.Tests.Content.Importing.Units.MaterializationFixtures;
using static Panopticon.Tests.Content.Importing.Units.UnitRecordFixtures;

namespace Panopticon.Tests.Content.Importing.Units;

public class UnitAbilityMaterializerTests
{
    [Fact]
    public void AttackAbilityBindsWeaponIntoDefaultSet()
    {
        var spear = Weapon("Spear");
        var attack = new AbilityRecord(Id(ContentKind.Ability, "Melee"), At(1)) { Family = Authored.Of("Attack"), Weapon = Authored.Of(spear.Id) };
        var unit = Unit("Spearman") with
        {
            MaxHealth = Authored.Of(Hp(60)),
            Modules = [ModuleEdit.Add(new UnitModule("Melee", "Ability", ModuleKinds.None, Ability: attack.Id))],
        };

        var content = Materialize(Set(unit, spear, attack));
        var set = Assert.Single(content.Get<ObjectDefinition>(unit.Id).WeaponSets.Values);

        Assert.True(content.Succeeded);
        Assert.Same(content.Get<WeaponTemplate>(spear.Id), set.Slots[0].Weapon.Value);
    }

    [Fact]
    public void AttackAbilityWithoutWeaponFails()
    {
        var attack = new AbilityRecord(Id(ContentKind.Ability, "Melee"), At(1)) { Family = Authored.Of("Attack") };
        var unit = Unit("U") with
        {
            MaxHealth = Authored.Of(Hp(60)),
            Modules = [ModuleEdit.Add(new UnitModule("Melee", "Ability", ModuleKinds.None, Ability: attack.Id))],
        };

        Assert.False(Materialize(Set(unit, attack)).Succeeded);
    }

    [Fact]
    public void UnsupportedAbilityFailsBeforePublication()
    {
        var heal = new AbilityRecord(Id(ContentKind.Ability, "Heal"), At(1)) { Family = Authored.Of("Heal") };
        var unit = Unit("Medic") with
        {
            MaxHealth = Authored.Of(Hp(60)),
            Modules = [ModuleEdit.Add(new UnitModule("Heal", "Ability", ModuleKinds.None, Ability: heal.Id))],
        };

        var resolution = UnitImportResolution.Resolve(Set(unit, heal), Registry);

        Assert.Contains(resolution.Diagnostics, d => d.Code == DiagnosticCodes.UnsupportedAbility);
        Assert.False(resolution.Succeeded);
    }

    [Fact]
    public void LegacyModuleAttachesNativeDataUnchanged()
    {
        var native = new SquishCollideModuleData();
        var unit = Unit("U") with
        {
            MaxHealth = Authored.Of(Hp(10)),
            Modules = [ModuleEdit.Add(new UnitModule("ModuleTag_Squish", "SquishCollide", ModuleKinds.Collide, Legacy: new LegacyBinding(Source, 0)))],
        };

        var definition = Materialize(Set(unit), new LegacyTable(native)).Get<ObjectDefinition>(unit.Id);

        Assert.Same(native, definition.Behaviors["ModuleTag_Squish"].Data);
    }

    [Fact]
    public void LegacyBindingWithoutTableFails()
    {
        var unit = Unit("U") with
        {
            MaxHealth = Authored.Of(Hp(10)),
            Modules = [ModuleEdit.Add(new UnitModule("ModuleTag_Squish", "SquishCollide", ModuleKinds.Collide, Legacy: new LegacyBinding(Source, 3)))],
        };

        var content = Materialize(Set(unit), new LegacyTable());

        Assert.False(content.Succeeded);
        Assert.Contains("handle 3", content.Diagnostics.Single(d => d.IsError).Message);
    }

    [Fact]
    public void LegacyTemplateBindingReusesNativeAsset()
    {
        var native = new WeaponTemplate();
        var weapon = Weapon("Legacy") with { Legacy = new LegacyBinding(Source, 0) };
        var unit = Unit("U") with { MaxHealth = Authored.Of(Hp(10)), Weapons = ListOverride<ContentId>.Replace([weapon.Id]) };

        var content = Materialize(Set(unit, weapon), new LegacyTable(native));

        Assert.Same(native, content.Get<WeaponTemplate>(weapon.Id));
    }
}
