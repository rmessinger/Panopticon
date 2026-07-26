using OpenSage.Logic.Object;
using OpenSage.Tools.ReplaySketch.Model;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

// WI-P6b-03: USA Comanche (Aircraft) full six-point archetype contract.
// Runs as a real CI [Fact] (no game install). The land/takeoff cycle and airborne movement are
// exercised by the supplemental [GameFact] against a real install.

public class P6bAircraftContractTests : P6bContractTestBase
{
    private const UnitArchetype Archetype = UnitArchetype.Aircraft;
    private const string ReferenceUnit = "AmericaVehicleComanche";

    [Fact]
    public void Catalog_ResolvesAircraftReferenceUnit()
    {
        var unit = UnitArchetypeCatalog.Resolve(Archetype, DefaultFaction);

        Assert.NotNull(unit);
        Assert.Equal(ReferenceUnit, unit.DefinitionName);
        Assert.Contains(ObjectKinds.Aircraft, unit.KindOfTags);
    }

    [Fact]
    public void Aircraft_Contract_HasAllSixPointsInOrder()
    {
        var scenario = BuildScenario(Archetype, DefaultFaction);

        Assert.Equal(UnitContractScenario.DefaultContractPoints, scenario.ContractPoints);
    }

    [Fact]
    public void Aircraft_Contract_AllPointsEvaluatedAndShapeValid()
    {
        var scenario = BuildScenario(Archetype, DefaultFaction);

        var report = RunContract(scenario, Generals);

        AssertFullContractShape(report, scenario);
    }

    [Fact]
    public void Aircraft_Contract_BasicContractHasNoGaps()
    {
        // The Comanche exposes no command-set special powers (rocket pods are a weapon slot, not a
        // SpecialPowerType); the land/takeoff cycle and airborne move are live [GameFact] concerns.
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
