using System.Linq;
using OpenSage.Logic.Object;
using OpenSage.Tools.ReplaySketch.Model;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

// WI-P6b-05: USA Ambulance (Support) full six-point archetype contract.
// Runs as a real CI [Fact] (no game install). The Ambulance's CleanupArea special power has no
// live handler (SpecialPowerAtLocationApplicator throws NotImplementedException), so the
// WeaponModesAndAbilities contract point surfaces an explicit P6b gap rather than a silent pass.

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
    public void Support_CleanupArea_IsExplicitGapNotSilentPass()
    {
        // The Ambulance's primary support ability is CleanupArea (detox/cleanup-area). Its handler
        // is unimplemented (SpecialPowerAtLocationApplicator.CleanupArea => throw
        // NotImplementedException), so the contract must report it as an explicit "P6b gap:" and
        // must NOT pass the abilities point silently.
        var scenario = BuildScenario(Archetype, DefaultFaction);

        var report = RunContract(scenario, Generals);

        Assert.True(report.HasGaps);
        Assert.False(report.AllContractPointsPassed);

        var abilityGap = AssertPointIsGap(report, ContractPoint.WeaponModesAndAbilities);
        Assert.Contains(ReferenceUnit, abilityGap.Detail);
        Assert.Contains(CleanupAreaPower, abilityGap.Detail);

        // The other five contract points are unaffected.
        AssertPointPassed(report, ContractPoint.Produced);
        AssertPointPassed(report, ContractPoint.MoveOrStatic);
        AssertPointPassed(report, ContractPoint.ArchetypeAction);
        AssertPointPassed(report, ContractPoint.AttackOrDecline);
        AssertPointPassed(report, ContractPoint.DieWithCleanup);
    }

    [Fact]
    public void Support_GapIsTheOnlyGap()
    {
        // Lock that exactly one gap is surfaced and it names the unimplemented special power.
        var scenario = BuildScenario(Archetype, DefaultFaction);

        var report = RunContract(scenario, Generals);

        var gap = Assert.Single(report.Gaps);
        Assert.Equal(ContractPoint.WeaponModesAndAbilities, gap.Point);
        Assert.Contains(CleanupAreaPower, gap.Detail);
    }
}
