using Panopticon.Content.Importing.Units;
using Panopticon.Logic.Object;
using Xunit;
using static Panopticon.Tests.Content.Importing.Units.MaterializationFixtures;
using static Panopticon.Tests.Content.Importing.Units.UnitRecordFixtures;

namespace Panopticon.Tests.Content.Importing.Units;

public class UnitProductionMaterializerTests
{
    [Fact]
    public void CostTimeAndPrerequisitesMaterialize()
    {
        var barracks = Unit("Barracks") with { MaxHealth = Authored.Of(Hp(1000)) };
        var unit = Unit("Soldier") with
        {
            MaxHealth = Authored.Of(Hp(100)),
            BuildCost = Authored.Of(Money(225)),
            BuildTime = Authored.Of(Seconds(5)),
            Prerequisites = ListOverride<ContentId>.Replace([barracks.Id]),
        };

        var content = Materialize(Set(unit, barracks));
        var definition = content.Get<ObjectDefinition>(unit.Id);

        Assert.Equal(225f, definition.BuildCost);
        Assert.Equal(150u, definition.BuildTime.Value);
        Assert.Same(content.Get<ObjectDefinition>(barracks.Id), Assert.Single(Assert.Single(definition.Prerequisites.Objects)).Value);
    }

    [Fact]
    public void UpgradeMaterializesWithAuthoredCost()
    {
        var upgrade = new UpgradeRecord(Id(ContentKind.Upgrade, "Armor"), At(1))
        {
            DisplayName = Authored.Of("Armor"),
            BuildCost = Authored.Of(Money(800)),
            BuildTime = Authored.Of(Seconds(30)),
            Scope = Authored.Of("Object"),
        };
        var unit = Unit("U") with { MaxHealth = Authored.Of(Hp(10)), Prerequisites = ListOverride<ContentId>.Replace([upgrade.Id]) };

        var content = Materialize(Set(unit, upgrade));
        var native = content.Get<UpgradeTemplate>(upgrade.Id);

        Assert.Equal(800f, native.BuildCost);
        Assert.Equal(900u, native.BuildTime.Value);
        Assert.Equal(UpgradeType.Object, native.Type);
        Assert.Contains(content.Diagnostics, d => d.IsError && d.Message.Contains("not an object"));
    }

    [Fact]
    public void ProductionLinksFailInsteadOfDropping()
    {
        var factory = Unit("Factory") with { MaxHealth = Authored.Of(Hp(10)), Produces = ListOverride<ContentId>.Replace([Id(ContentKind.Unit, "Tank")]) };
        var tank = Unit("Tank") with { MaxHealth = Authored.Of(Hp(10)) };

        var content = Materialize(Set(factory, tank));

        Assert.Contains(content.Diagnostics, d => d.IsError && d.Message.Contains("command-set"));
    }

    [Fact]
    public void NonCurrencyCostIsRejectedBeforeMaterialization()
    {
        var unit = Unit("U") with { MaxHealth = Authored.Of(Hp(10)), BuildCost = Authored.Of(new Quantity(5, QuantityUnit.Scalar)) };

        Assert.False(UnitImportResolution.Resolve(Set(unit), Registry).Succeeded);
    }
}
