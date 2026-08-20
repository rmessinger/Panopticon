using System.Linq;
using OpenSage.Logic.Object;
using OpenSage.Tools.ReplaySketch.Model;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

// WI-P6b-05: USA Ambulance (Support) full six-point archetype contract.
// Runs as a real CI [Fact] (no game install). The Ambulance's CleanupArea special power gained a
// live handler in P6c (heal friendlies in the target radius), so the WeaponModesAndAbilities
// contract point passes and the full six-point contract holds; the live observable effect is
// asserted by the install-gated P6cSpecialPowerTests.

public class P6bSupportContractTests : P6bContractTestBase
{
    private const UnitArchetype Archetype = UnitArchetype.Support;
    private const string ReferenceUnit = "AmericaVehicleMedic";
    private const string CleanupAreaPower = "CleanupArea";

    [Fact]
    public void Catalog_ResolvesSupportReferenceUnit()
    {
        var unit = UnitArchetypeCatalog.Resolve(Archetype, DefaultFaction);

        Assert.NotNull(unit);
        Assert.Equal(ReferenceUnit, unit.DefinitionName);
        Assert.Equal(UnitArchetypeCatalog.FactionUSA, unit.FactionName);
        Assert.Contains(CleanupAreaPower, unit.SpecialPowerNames);
    }

    [Fact]
    public void Support_Contract_HasAllSixPointsInOrder()
    {
        var scenario = BuildScenario(Archetype, DefaultFaction);

        Assert.Equal(UnitContractScenario.DefaultContractPoints, scenario.ContractPoints);
    }

    [Fact]
    public void Support_Contract_AllPointsEvaluatedAndShapeValid()
    {
        var scenario = BuildScenario(Archetype, DefaultFaction);

        var report = RunContract(scenario, Generals);

        AssertFullContractShape(report, scenario);
    }

    [Fact]
    public void Support_CleanupArea_IsImplemented_PassesAllSixPoints()
    {
        // P6c implemented the Ambulance's CleanupArea handler (SpecialPowerAtLocationApplicator
        // heals friendlies in the power radius), so the abilities point passes and no gap is
        // reported anywhere in the contract.
        var scenario = BuildScenario(Archetype, DefaultFaction);

        var report = RunContract(scenario, Generals);

        Assert.False(report.HasGaps);
        Assert.True(report.AllContractPointsPassed);

        AssertPointPassed(report, ContractPoint.Produced);
        AssertPointPassed(report, ContractPoint.MoveOrStatic);
        AssertPointPassed(report, ContractPoint.ArchetypeAction);
        AssertPointPassed(report, ContractPoint.AttackOrDecline);
        AssertPointPassed(report, ContractPoint.WeaponModesAndAbilities);
        AssertPointPassed(report, ContractPoint.DieWithCleanup);
    }

    [Fact]
    public void Support_NoGapsAfterP6c()
    {
        // Lock that the P6c implementation removed the last support-contract gap.
        var scenario = BuildScenario(Archetype, DefaultFaction);

        var report = RunContract(scenario, Generals);

        Assert.Empty(report.Gaps);
    }
}
