using System.Linq;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

public class ValidationGroundBuilderTests
{
    private static SpawnPlan Build(ValidationGroundScenario? scenario = null) =>
        ValidationGroundBuilder.Build(scenario ?? new ValidationGroundScenario());

    [Fact, Trait("Category", "Roadmap")]
    public void Build_IsDeterministicAcrossInvocations()
    {
        var scenario = new ValidationGroundScenario { Seed = 7 };
        var a = Build(scenario);
        var b = Build(scenario);

        Assert.Equal(a.UnitPlacements.Count, b.UnitPlacements.Count);
        for (var i = 0; i < a.UnitPlacements.Count; i++)
        {
            Assert.Equal(a.UnitPlacements[i], b.UnitPlacements[i]);
        }

        Assert.Equal(a.TargetPlacements, b.TargetPlacements);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void EveryRosterUnit_AppearsExactlyOnceWithResolvedAction()
    {
        var plan = Build();

        var expected = ValidationGroundBuilderTests.TotalRosterCount;
        Assert.Equal(expected, plan.UnitPlacements.Count);

        var names = plan.UnitPlacements.Select(p => p.DefinitionName).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(plan.UnitPlacements, p => Assert.NotNull(p.Action));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void EveryAttacker_GetsADistinctTarget()
    {
        var plan = Build();

        // The cluster has one target per unit, and each unit's TargetIndex is its unique global
        // index, so no two attackers share a target. A shared target would complete every unit
        // assigned to it on the first hit, conflating their timings. See deep dive P9 §2.4, §2.5.
        Assert.Equal(plan.UnitPlacements.Count, plan.TargetPlacements.Count);
        var assigned = plan.UnitPlacements.Select(p => p.TargetIndex).ToList();
        Assert.Equal(assigned.Count, assigned.Distinct().Count());
        Assert.All(plan.UnitPlacements, p => Assert.InRange(p.TargetIndex, 0, plan.TargetPlacements.Count - 1));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void NoTwoPlacements_ShareAPosition()
    {
        var plan = Build();
        var positions = plan.UnitPlacements
            .Select(p => (p.Position.X, p.Position.Y))
            .ToList();

        Assert.Equal(positions.Count, positions.Distinct().Count());
    }

    private static int TotalRosterCount =>
        UnitCatalog.ForFaction(2).Count
        + UnitCatalog.ForFaction(3).Count
        + UnitCatalog.ForFaction(4).Count;
}
