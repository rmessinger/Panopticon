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
    public void TargetAssignment_IsByIndexAndStable()
    {
        var plan = Build();
        var targetCount = plan.TargetPlacements.Count;

        for (var i = 0; i < plan.UnitPlacements.Count; i++)
        {
            Assert.Equal(i % targetCount, plan.UnitPlacements[i].TargetIndex);
            Assert.InRange(plan.UnitPlacements[i].TargetIndex, 0, targetCount - 1);
        }
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
