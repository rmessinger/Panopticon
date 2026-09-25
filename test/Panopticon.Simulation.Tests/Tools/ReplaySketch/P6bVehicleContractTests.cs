using Panopticon.Logic.Object;
using Panopticon.Tools.ReplaySketch.Model;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

// WI-P6b-02: USA Crusader Tank (Vehicle) full six-point archetype contract.
// Runs as a real CI [Fact] (no game install).

public class P6bVehicleContractTests : P6bContractTestBase
{
    private const UnitArchetype Archetype = UnitArchetype.Vehicle;
    private const string ReferenceUnit = "AmericaTankCrusader";

    [Fact]
    public void Catalog_ResolvesVehicleReferenceUnit()
    {
        var unit = UnitArchetypeCatalog.Resolve(Archetype, DefaultFaction);

        Assert.NotNull(unit);
        Assert.Equal(ReferenceUnit, unit.DefinitionName);
        Assert.Contains(ObjectKinds.Vehicle, unit.KindOfTags);
        // A vehicle is not an aircraft and not a dozer.
        Assert.DoesNotContain(ObjectKinds.Aircraft, unit.KindOfTags);
        Assert.DoesNotContain(ObjectKinds.Dozer, unit.KindOfTags);
    }

    [Fact]
    public void Vehicle_Contract_HasAllSixPointsInOrder()
    {
        var scenario = BuildScenario(Archetype, DefaultFaction);

        Assert.Equal(UnitContractScenario.DefaultContractPoints, scenario.ContractPoints);
    }

    [Fact]
    public void Vehicle_Contract_AllPointsEvaluatedAndShapeValid()
    {
        var scenario = BuildScenario(Archetype, DefaultFaction);

        var report = RunContract(scenario, Generals);

        AssertFullContractShape(report, scenario);
    }

    [Fact]
    public void Vehicle_Contract_BasicContractHasNoGaps()
    {
        // The Crusader exposes no command-set special powers; weapon-mode upgrades are exercised by
        // the supplemental [GameFact] against real asset data.
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
