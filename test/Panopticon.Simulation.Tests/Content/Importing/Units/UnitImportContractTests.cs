using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Units;
using Xunit;
using static Panopticon.Tests.Content.Importing.Units.UnitRecordFixtures;

namespace Panopticon.Tests.Content.Importing.Units;

public class UnitImportContractTests
{
    [Fact]
    public void ContentId_FollowsSourceNameRule()
    {
        var generalsUpper = new ContentId(ContentSource.Generals, ContentKind.Unit, "AmericaInfantryRanger");
        var generalsLower = new ContentId(ContentSource.Generals, ContentKind.Unit, "americainfantryranger");
        var ee2Upper = new ContentId(ContentSource.Ee2, ContentKind.Unit, "Priest");
        var ee2Lower = new ContentId(ContentSource.Ee2, ContentKind.Unit, "priest");

        Assert.Equal(generalsUpper, generalsLower);
        Assert.Equal(generalsUpper.GetHashCode(), generalsLower.GetHashCode());
        Assert.Equal("AmericaInfantryRanger", generalsUpper.Name);
        Assert.NotEqual(ee2Upper, ee2Lower);
    }

    [Fact]
    public void ContentId_DistinguishesSourceAndKind()
    {
        var unit = new ContentId(ContentSource.Generals, ContentKind.Unit, "X");

        Assert.NotEqual(unit, new ContentId(ContentSource.Ee2, ContentKind.Unit, "X"));
        Assert.NotEqual(unit, new ContentId(ContentSource.Generals, ContentKind.Weapon, "X"));
    }

    [Fact]
    public void ContentId_RejectsEmptyName()
    {
        Assert.Throws<ArgumentException>(() => new ContentId(ContentSource.Generals, ContentKind.Unit, " "));
    }

    [Fact]
    public void Authored_KeepsOmittedAndZeroDistinct()
    {
        var omitted = Authored<Quantity>.Omitted;
        var zero = Authored.Of(Hp(0));

        Assert.False(omitted.IsPresent);
        Assert.True(zero.IsPresent);
        Assert.Equal(0, zero.Value.Value);
        Assert.Throws<InvalidOperationException>(() => omitted.Value);
    }

    [Fact]
    public void ListOverride_CopiesInput()
    {
        var items = new List<string> { "INFANTRY" };
        var list = ListOverride<string>.Replace(items);

        items.Add("VEHICLE");

        Assert.Equal(["INFANTRY"], list.Items);
    }

    [Fact]
    public void ListOverride_ExplicitEmptyReplacesParent()
    {
        var parent = ListOverride<string>.Replace(["A"]);

        Assert.Empty(ListOverride<string>.Replace([]).ApplyOver(parent).Resolved);
        Assert.Equal(["A"], ListOverride<string>.Inherit.ApplyOver(parent).Resolved);
        Assert.Equal(["B"], ListOverride<string>.Edit(["B"], ["A"]).ApplyOver(parent).Resolved);
    }

    [Fact]
    public void Records_CopyCollections()
    {
        var multipliers = new Dictionary<string, Authored<double>> { ["SMALL_ARMS"] = Authored.Of(0.5) };
        var edits = new List<ModuleEdit> { ModuleEdit.Add(Body()) };
        var armor = new ArmorRecord(Id(ContentKind.Armor, "A"), At(1)) { Multipliers = multipliers };
        var unit = Unit("U") with { Modules = edits };

        multipliers["FLAME"] = Authored.Of(2.0);
        edits.Clear();

        Assert.Single(armor.Multipliers);
        Assert.Single(unit.Modules);
    }

    [Fact]
    public void Record_RejectsMismatchedKind()
    {
        Assert.Throws<ArgumentException>(() => new UnitRecord(Id(ContentKind.Weapon, "W"), At(1)));
    }

    [Fact]
    public void ContentSet_CopiesRecords()
    {
        var records = new List<SourceRecord> { Unit("U") };
        var set = new UnitContentSet(Source, records, []);

        records.Clear();

        Assert.Single(set.Records);
    }

    [Fact]
    public void Contracts_HoldNoParserOrRuntimeTypes()
    {
        var forbiddenNamespaces = new[] { "Panopticon.Data.Ini", "Panopticon.Logic.Object.GameObject" };
        var contractTypes = typeof(SourceRecord).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(SourceRecord).Namespace);

        foreach (var type in contractTypes)
        {
            foreach (var property in type.GetProperties())
            {
                var name = property.PropertyType.FullName ?? string.Empty;
                Assert.DoesNotContain(forbiddenNamespaces, forbidden => name.StartsWith(forbidden, StringComparison.Ordinal));
                Assert.False(typeof(BaseAsset).IsAssignableFrom(property.PropertyType), $"{type.Name}.{property.Name} holds a runtime asset.");
            }
        }
    }

    [Fact]
    public void EveryContentFamily_HasARecordType()
    {
        var recordKinds = new SourceRecord[]
        {
            Unit("U"),
            Weapon("W"),
            Armor("A"),
            Locomotor("L"),
            new ProjectileRecord(Id(ContentKind.Projectile, "P"), At(1)),
            new AbilityRecord(Id(ContentKind.Ability, "Ab"), At(1)),
            new UpgradeRecord(Id(ContentKind.Upgrade, "Up"), At(1)),
            new VisualStateRecord(Id(ContentKind.VisualState, "V"), At(1)),
        }.Select(record => record.Id.Kind);

        Assert.Equal(Enum.GetValues<ContentKind>(), recordKinds);
    }
}
