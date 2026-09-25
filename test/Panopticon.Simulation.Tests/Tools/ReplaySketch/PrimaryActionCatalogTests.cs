using System.Linq;
using Panopticon.Logic.Orders;
using Panopticon.Tools.ReplaySketch.Model;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

public class PrimaryActionCatalogTests
{
    [Fact, Trait("Category", "Roadmap")]
    public void EveryCatalogEntry_ResolvesToAnAction()
    {
        var entries = PrimaryActionCatalog.AllEntries().ToList();

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.NotNull(e.Action));
        // Completeness guard: the test set must cover the whole roster, so adding a unit to
        // UnitCatalog without a mapping fails here instead of silently dropping it from the ground.
        Assert.Equal(
            TotalCatalogCount,
            entries.Select(e => e.Unit.DefinitionName).Distinct().Count());
    }

    [Fact, Trait("Category", "Roadmap")]
    public void EveryBuildAction_HasATargetDefinitionName()
    {
        var builds = PrimaryActionCatalog.AllEntries()
            .Where(e => e.Action.Kind == PrimaryActionKind.Build)
            .ToList();

        Assert.All(builds, e => Assert.False(string.IsNullOrEmpty(e.Action.TargetDefinitionName)));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void EveryAction_HasPositiveFrameBudget()
    {
        Assert.All(PrimaryActionCatalog.AllEntries(), e => Assert.True(e.Action.FrameBudget > 0));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void SupportUnits_ResolveToSpecialPower()
    {
        Assert.Equal(PrimaryActionKind.SpecialPower,
            PrimaryActionCatalog.Resolve("AmericaVehicleMedic").Kind);
        Assert.Equal(PrimaryActionKind.SpecialPower,
            PrimaryActionCatalog.Resolve("ChinaInfantryHacker").Kind);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CombatUnits_ResolveToAttack()
    {
        var attack = PrimaryActionCatalog.Resolve("AmericaInfantryRanger");
        Assert.Equal(PrimaryActionKind.Attack, attack.Kind);
        Assert.Equal(OrderType.AttackObject, attack.OrderType);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void Move_IsReachableAsExplicitFallback()
    {
        var move = PrimaryAction.Move(PrimaryActionCatalog.MoveFrameBudget);
        Assert.Equal(PrimaryActionKind.Move, move.Kind);
        Assert.Equal(OrderType.MoveTo, move.OrderType);
        Assert.Null(move.TargetDefinitionName);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void BuildAndGatherBudgets_AreLargerThanAttack()
    {
        Assert.True(PrimaryActionCatalog.BuildFrameBudget > PrimaryActionCatalog.AttackFrameBudget);
        Assert.True(PrimaryActionCatalog.GatherFrameBudget > PrimaryActionCatalog.AttackFrameBudget);
    }

    private static int TotalCatalogCount =>
        UnitCatalog.ForFaction(2).Count
        + UnitCatalog.ForFaction(3).Count
        + UnitCatalog.ForFaction(4).Count;
}
