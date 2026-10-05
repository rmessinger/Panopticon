using System.Linq;
using Panopticon.Content.Importing.Units;
using Xunit;
using static Panopticon.Tests.Content.Importing.Units.UnitRecordFixtures;

namespace Panopticon.Tests.Content.Importing.Units;

public class UnitDependencyResolverTests
{
    private sealed class PublishedSet(params ContentId[] ids) : IPublishedContentLookup
    {
        public bool Contains(ContentId id) => ids.Contains(id);
    }

    private static DependencyResult Resolve(UnitContentSet set, IPublishedContentLookup? published = null)
    {
        var inheritance = new UnitInheritanceResolver().Resolve(set);
        return new UnitDependencyResolver().Resolve(inheritance, set.Roots, published);
    }

    [Fact]
    public void ClosureFollowsReferencesInDiscoveryOrder()
    {
        var unused = Weapon("Unused");
        var result = Resolve(ValidSoldier(unused));

        Assert.Empty(result.Diagnostics);
        Assert.Equal(["Soldier", "Feet", "Rifle", "Flesh"], result.Closure.Select(r => r.Id.Name));
    }

    [Fact]
    public void ForwardReferenceInLaterRecordResolves()
    {
        var unit = Unit("U") with { Weapons = ListOverride<ContentId>.Replace([Id(ContentKind.Weapon, "Later")]) };
        var set = new UnitContentSet(Source, [unit, Weapon("Later")], [unit.Id]);

        Assert.Empty(Resolve(set).Diagnostics);
    }

    [Fact]
    public void MissingRequiredDependencyIsError()
    {
        var unit = Unit("U") with { Armor = Authored.Of(Id(ContentKind.Armor, "Ghost")) };
        var result = Resolve(new UnitContentSet(Source, [unit], [unit.Id]));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.MissingDependency, diagnostic.Code);
        Assert.True(diagnostic.IsError);
    }

    [Fact]
    public void MissingPresentationIsWarning()
    {
        var unit = Unit("U") with { Presentation = ListOverride<ContentId>.Replace([Id(ContentKind.Unit, "SoundOnly")]) };
        var diagnostic = Assert.Single(Resolve(new UnitContentSet(Source, [unit], [unit.Id])).Diagnostics);

        Assert.Equal(DiagnosticCodes.MissingOptionalDependency, diagnostic.Code);
        Assert.False(diagnostic.IsError);
    }

    [Fact]
    public void WrongKindIsError()
    {
        var unit = Unit("U") with { Weapons = ListOverride<ContentId>.Replace([Id(ContentKind.Armor, "Flesh")]) };
        var result = Resolve(new UnitContentSet(Source, [unit, Armor("Flesh")], [unit.Id]));

        Assert.Equal(DiagnosticCodes.WrongDependencyKind, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void PublishedContentSatisfiesReferenceWithoutJoiningClosure()
    {
        var builtIn = new ContentId(ContentSource.Generals, ContentKind.Weapon, "RangerAdvancedCombatRifle");
        var unit = Unit("U") with { Weapons = ListOverride<ContentId>.Replace([builtIn]) };
        var result = Resolve(new UnitContentSet(Source, [unit], [unit.Id]), new PublishedSet(builtIn));

        Assert.Empty(result.Diagnostics);
        Assert.Single(result.Closure);
    }

    [Fact]
    public void ProductionCycleIsLegal()
    {
        var factory = Unit("Factory") with { Produces = ListOverride<ContentId>.Replace([Id(ContentKind.Unit, "Dozer")]) };
        var dozer = Unit("Dozer") with { Produces = ListOverride<ContentId>.Replace([factory.Id]) };

        var result = Resolve(new UnitContentSet(Source, [factory, dozer], [factory.Id]));

        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, result.Closure.Count);
    }

    [Fact]
    public void NonProductionCycleIsForbidden()
    {
        var a = Unit("A") with { Presentation = ListOverride<ContentId>.Replace([Id(ContentKind.Unit, "B")]) };
        var b = Unit("B") with { Presentation = ListOverride<ContentId>.Replace([a.Id]) };

        var result = Resolve(new UnitContentSet(Source, [a, b], [a.Id]));

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.DependencyCycle);
    }

    [Fact]
    public void MissingRootIsError()
    {
        var result = Resolve(new UnitContentSet(Source, [], [Id(ContentKind.Unit, "Nobody")]));

        Assert.Equal(DiagnosticCodes.MissingRoot, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void CrossSourceReferenceRequiresExplicitPublishedTarget()
    {
        var foreign = new ContentId(ContentSource.Ee2, ContentKind.Weapon, "Rifle");
        var unit = Unit("U") with { Weapons = ListOverride<ContentId>.Replace([foreign]) };

        var result = Resolve(new UnitContentSet(Source, [unit, Weapon("Rifle")], [unit.Id]));

        Assert.Equal(DiagnosticCodes.MissingDependency, Assert.Single(result.Diagnostics).Code);
    }
}
