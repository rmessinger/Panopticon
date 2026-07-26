using OpenSage.Logic.Object;
using OpenSage.Tools.ReplaySketch.Model;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

// WI-P6b-01: USA Ranger (Infantry) full six-point archetype contract.
// Runs as a real CI [Fact] (no game install). The supplemental [GameFact] exercises the live
// contract against a real install.

public class P6bInfantryContractTests : P6bContractTestBase
{
    private const UnitArchetype Archetype = UnitArchetype.Infantry;
    private const string ReferenceUnit = "AmericaInfantryRanger";

    [Fact]
    public void Catalog_ResolvesInfantryReferenceUnit()
    {
        var unit = UnitArchetypeCatalog.Resolve(Archetype, DefaultFaction);

        Assert.NotNull(unit);
        Assert.Equal(ReferenceUnit, unit.DefinitionName);
        Assert.Contains(ObjectKinds.Infantry, unit.KindOfTags);
    }

    [Fact]
    public void Infantry_Contract_HasAllSixPointsInOrder()
    {
        var scenario = BuildScenario(Archetype, DefaultFaction);

        Assert.Equal(UnitContractScenario.DefaultContractPoints, scenario.ContractPoints);
    }

    [Fact]
    public void Infantry_Contract_AllPointsEvaluatedAndShapeValid()
    {
        var scenario = BuildScenario(Archetype, DefaultFaction);

        var report = RunContract(scenario, Generals);

        AssertFullContractShape(report, scenario);
    }

    [Fact]
    public void Infantry_Contract_BasicContractHasNoGaps()
    {
        // The Ranger exposes no command-set special powers, so the install-free runner reports
        // every contract point as Passed. The live weapon/move/attack/die effects are exercised
        // by the supplemental [GameFact] below.
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

    [Fact]
    public void Infantry_GapSurfacing_UnimplementedPowerIsExplicitNotSilent()
    {
        // Inject a synthetic unimplemented special power and confirm the runner surfaces it as a
        // "P6b gap:" failure rather than a silent pass. This locks the gap-surfacing contract that
        // the other archetypes (support, superweapon) rely on.
        var scenario = new UnitContractScenario
        {
            Archetype = Archetype,
            FactionName = DefaultFaction,
            UnitDefinitionName = ReferenceUnit,
            SpecialPowerNames = ["ParticleUplinkCannon"],
        };

        var report = RunContract(scenario, Generals);

        var gap = AssertPointIsGap(report, ContractPoint.WeaponModesAndAbilities);
        Assert.Contains(ReferenceUnit, gap.Detail);
        Assert.Contains("ParticleUplinkCannon", gap.Detail);
        Assert.False(report.AllContractPointsPassed);
    }
}
