using Panopticon.Logic.Object;
using Panopticon.Tools.ReplaySketch.Model;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

// WI-P6b-04: USA Dozer (Builder) full six-point archetype contract.
// Runs as a real CI [Fact] (no game install). Build/repair orders (OrderType.BuildObject /
// OrderType.RepairStructure) have real OrderProcessor handlers; the live construct-to-completion
// and repair-progress effects are exercised by the supplemental [GameFact].

public class P6bBuilderContractTests : P6bContractTestBase
{
    private const UnitArchetype Archetype = UnitArchetype.Builder;
    private const string ReferenceUnit = "AmericaVehicleDozer";

    [Fact]
    public void Catalog_ResolvesBuilderReferenceUnit()
    {
        var unit = UnitArchetypeCatalog.Resolve(Archetype, DefaultFaction);

        Assert.NotNull(unit);
        Assert.Equal(ReferenceUnit, unit.DefinitionName);
        Assert.Contains(ObjectKinds.Dozer, unit.KindOfTags);
    }

    [Fact]
    public void Builder_Contract_HasAllSixPointsInOrder()
    {
        var scenario = BuildScenario(Archetype, DefaultFaction);

        Assert.Equal(UnitContractScenario.DefaultContractPoints, scenario.ContractPoints);
    }

    [Fact]
    public void Builder_Contract_AllPointsEvaluatedAndShapeValid()
    {
        var scenario = BuildScenario(Archetype, DefaultFaction);

        var report = RunContract(scenario, Generals);

        AssertFullContractShape(report, scenario);
    }

    [Fact]
    public void Builder_Contract_BasicContractHasNoGaps()
    {
        // The Dozer exposes no command-set SpecialPowerType; its archetype actions (BuildObject and
        // RepairStructure) map to implemented OrderProcessor handlers, so no gap is surfaced here.
        var scenario = BuildScenario(Archetype, DefaultFaction);

        var report = RunContract(scenario, Generals);

        Assert.False(report.HasGaps,
            report.HasGaps ? string.Join("\n", report.GapMessages) : null);
        Assert.True(report.AllContractPointsPassed);
        AssertPointPassed(report, ContractPoint.Produced);
        AssertPointPassed(report, ContractPoint.MoveOrStatic);
        AssertPointPassed(report, ContractPoint.ArchetypeAction);
        AssertPointPassed(report, ContractPoint.AttackOrDecline);
        AssertPointPassed(report, ContractPoint.WeaponModesAndAbilities);
        AssertPointPassed(report, ContractPoint.DieWithCleanup);
    }
}
