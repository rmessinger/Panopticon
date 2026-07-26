using OpenSage.Logic.Object;
using OpenSage.Tools.ReplaySketch.Model;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

// WI-P6b-06: USA Particle Cannon (Superweapon) full six-point archetype contract.
// Runs as a real CI [Fact] (no game install). The ParticleUplinkCannon special power has no live
// handler (SpecialPowerAtLocationApplicator.ParticleUplinkCannon => throw
// NotImplementedException), so the abilities contract point surfaces an explicit P6b gap.

public class P6bSuperweaponContractTests : P6bContractTestBase
{
    private const UnitArchetype Archetype = UnitArchetype.Superweapon;
    private const string ReferenceUnit = "AmericaParticleCannon";
    private const string ParticlePower = "ParticleUplinkCannon";

    [Fact]
    public void Catalog_ResolvesSuperweaponReferenceUnit()
    {
        var unit = UnitArchetypeCatalog.Resolve(Archetype, DefaultFaction);

        Assert.NotNull(unit);
        Assert.Equal(ReferenceUnit, unit.DefinitionName);
        // A superweapon is a static structure.
        Assert.Contains(ObjectKinds.Structure, unit.KindOfTags);
        Assert.Contains(ObjectKinds.FSSuperweapon, unit.KindOfTags);
        Assert.Contains(ParticlePower, unit.SpecialPowerNames);
    }

    [Fact]
    public void Superweapon_Contract_HasAllSixPointsInOrder()
    {
        var scenario = BuildScenario(Archetype, DefaultFaction);

        Assert.Equal(UnitContractScenario.DefaultContractPoints, scenario.ContractPoints);
    }

    [Fact]
    public void Superweapon_Contract_AllPointsEvaluatedAndShapeValid()
    {
        var scenario = BuildScenario(Archetype, DefaultFaction);

        var report = RunContract(scenario, Generals);

        AssertFullContractShape(report, scenario);
    }

    [Fact]
    public void Superweapon_ParticleUplinkCannon_IsExplicitGapNotSilentPass()
    {
        // The particle cannon's fire ability is ParticleUplinkCannon. Its handler is unimplemented
        // (SpecialPowerAtLocationApplicator.ParticleUplinkCannon => throw
        // NotImplementedException), so the contract reports it as an explicit "P6b gap:".
        var scenario = BuildScenario(Archetype, DefaultFaction);

        var report = RunContract(scenario, Generals);

        Assert.True(report.HasGaps);
        Assert.False(report.AllContractPointsPassed);

        var abilityGap = AssertPointIsGap(report, ContractPoint.WeaponModesAndAbilities);
        Assert.Contains(ReferenceUnit, abilityGap.Detail);
        Assert.Contains(ParticlePower, abilityGap.Detail);

        // Produced/built, static positioning, charge action, attack/effect path, and sell/die
        // cleanup are unaffected; the static (non-moving) positioning is asserted as the move point.
        AssertPointPassed(report, ContractPoint.Produced);
        AssertPointPassed(report, ContractPoint.MoveOrStatic);
        AssertPointPassed(report, ContractPoint.ArchetypeAction);
        AssertPointPassed(report, ContractPoint.AttackOrDecline);
        AssertPointPassed(report, ContractPoint.DieWithCleanup);
    }

    [Fact]
    public void Superweapon_GapIsTheOnlyGap()
    {
        var scenario = BuildScenario(Archetype, DefaultFaction);

        var report = RunContract(scenario, Generals);

        var gap = Assert.Single(report.Gaps);
        Assert.Equal(ContractPoint.WeaponModesAndAbilities, gap.Point);
        Assert.Contains(ParticlePower, gap.Detail);
    }
}
