using System.Collections.Generic;
using Panopticon.Content;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.FX;
using Panopticon.Logic.Object;
using Xunit;
using static Panopticon.Tests.Content.Importing.Units.MaterializationFixtures;
using static Panopticon.Tests.Content.Importing.Units.UnitRecordFixtures;

namespace Panopticon.Tests.Content.Importing.Units;

public class UnitDamageEffectPolicyTests
{
    internal static UnitContentSet TankContent()
    {
        return Set(Unit("Tank") with
        {
            MaxHealth = Authored.Of(Hp(100)),
            KindOf = ListOverride<string>.Replace(["VEHICLE"]),
            Armor = Authored.Of(Id(ContentKind.Armor, "Plate")),
        }, Armor("Plate"));
    }

    private static MaterializedUnitContent Prepare(Dictionary<ContentId, string>? mappings, DamageFX? effect)
    {
        var resolution = UnitImportResolution.Resolve(TankContent(), Registry);
        Assert.True(resolution.Succeeded);
        return UnitRuntimeMaterializer.Materialize(resolution, new UnitMaterializationContext(
            Policy, damageEffects: mappings, damageEffectLookup: _ => effect));
    }

    [Fact]
    public void OptInBindsExactEffectWithoutChangingArmor()
    {
        var effect = new DamageFX();
        var content = Prepare(new() { [Id(ContentKind.Unit, "Tank")] = "TankDamageFX" }, effect);
        var armor = Assert.Single(content.Get<ObjectDefinition>(Id(ContentKind.Unit, "Tank")).ArmorSets);

        Assert.True(content.Succeeded);
        Assert.Same(effect, armor.DamageFX.Value);
        Assert.Equal("TankDamageFX", armor.DamageFX.Name);
        Assert.Equal(5f, armor.Armor.Value.AdjustDamage(DamageType.SmallArms, 10f));
    }

    [Fact]
    public void AbsenceDoesNotBindAnyEffect()
    {
        var content = Prepare(null, new DamageFX());

        Assert.True(content.Succeeded);
        Assert.Null(Assert.Single(content.Get<ObjectDefinition>(Id(ContentKind.Unit, "Tank")).ArmorSets).DamageFX);
    }

    [Theory]
    [InlineData("Tank", "MissingDamageFX")]
    [InlineData("UnknownTank", "TankDamageFX")]
    public void MissingRequestedReferencePreventsStaging(string subject, string name)
    {
        var content = Prepare(new() { [Id(ContentKind.Unit, subject)] = name }, null);

        Assert.False(content.Succeeded);
        Assert.Empty(content.Assets);
        Assert.Contains(content.Diagnostics, d => d.IsError && d.Message.Contains(subject) && d.Message.Contains(name));
    }

    [Fact]
    public void MissingFxListPreventsStaging()
    {
        var effect = new DamageFX();
        effect.Groups[(int)DamageType.SmallArms, (int)VeterancyLevel.Regular].MinorFX =
            new LazyAssetReference<FXList>("MissingImpact", () => null!);
        var content = Prepare(new() { [Id(ContentKind.Unit, "Tank")] = "TankDamageFX" }, effect);

        Assert.False(content.Succeeded);
        Assert.Empty(content.Assets);
        Assert.Contains(content.Diagnostics, d => d.Message.Contains("Tank") && d.Message.Contains("MissingImpact"));
    }

    [Fact]
    public void NonVehicleCannotOptIn()
    {
        var resolution = UnitImportResolution.Resolve(ValidSoldier(), Registry);
        var content = UnitRuntimeMaterializer.Materialize(resolution, new UnitMaterializationContext(
            Policy, damageEffects: new Dictionary<ContentId, string> { [Id(ContentKind.Unit, "Soldier")] = "TankDamageFX" },
            damageEffectLookup: _ => new DamageFX()));

        Assert.False(content.Succeeded);
        Assert.Empty(content.Assets);
        Assert.Contains(content.Diagnostics, d => d.Message.Contains("Soldier") && d.Message.Contains("typed vehicle"));
    }

    [Fact]
    public void MappingIsScopedToOneUnitNotItsSharedArmor()
    {
        var first = (UnitRecord)TankContent().Records[0];
        var second = Unit("OtherTank", "Tank");
        var set = new UnitContentSet(Source, [first, second, Armor("Plate")], [first.Id, second.Id]);
        var resolution = UnitImportResolution.Resolve(set, Registry);
        var effect = new DamageFX();
        var content = UnitRuntimeMaterializer.Materialize(resolution, new UnitMaterializationContext(
            Policy, damageEffects: new Dictionary<ContentId, string> { [first.Id] = "TankDamageFX" }, damageEffectLookup: _ => effect));

        Assert.True(content.Succeeded);
        var mapped = Assert.Single(content.Get<ObjectDefinition>(first.Id).ArmorSets);
        var unmapped = Assert.Single(content.Get<ObjectDefinition>(second.Id).ArmorSets);
        Assert.Same(mapped.Armor.Value, unmapped.Armor.Value);
        Assert.Same(effect, mapped.DamageFX.Value);
        Assert.Null(unmapped.DamageFX);
    }
}
