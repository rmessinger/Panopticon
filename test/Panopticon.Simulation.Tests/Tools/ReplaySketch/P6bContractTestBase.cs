using System.Collections.Generic;
using System.Linq;
using Panopticon;
using Panopticon.Logic.Object;
using Panopticon.Tests.Tools.ReplaySketch;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// Shared helpers for the P6b per-archetype contract tests. Builds scenarios from
/// <see cref="UnitArchetypeCatalog"/>, runs them through <see cref="UnitContractRunner"/>, and
/// asserts the common contract shape (six points, gap surfacing) reused across archetypes.
/// </summary>
public abstract class P6bContractTestBase : MockedGameTest
{
    protected const string DefaultFaction = UnitArchetypeCatalog.FactionUSA;

    protected static UnitContractScenario BuildScenario(UnitArchetype archetype, string faction) =>
        UnitArchetypeCatalog.BuildScenario(archetype, faction);

    protected static ContractReport RunContract(UnitContractScenario scenario, IGame? game = null) =>
        new UnitContractRunner(game).Run(scenario);

    /// <summary>
    /// Asserts the full six-point contract for a scenario: all six contract points are present and
    /// evaluated, every result is either Passed or an explicit gap, and no gap is reported as a pass.
    /// </summary>
    protected static void AssertFullContractShape(ContractReport report, UnitContractScenario scenario)
    {
        Assert.Equal(scenario.UnitDefinitionName, report.UnitDefinitionName);
        Assert.Equal(scenario.Archetype, report.Archetype);
        Assert.Equal(scenario.FactionName, report.FactionName);

        var evaluatedPoints = report.Results.Select(r => r.Point).ToList();
        Assert.Equal(UnitContractScenario.DefaultContractPoints, evaluatedPoints);

        foreach (var result in report.Results)
        {
            // A gap must carry a "P6b gap:" detail and never be Passed.
            if (result.IsGap)
            {
                Assert.NotNull(result.Detail);
                Assert.StartsWith("P6b gap:", result.Detail);
                Assert.False(result.Passed);
            }
        }
    }

    /// <summary>
    /// Asserts that a contract point that should be implemented is reported Passed (no silent gap).
    /// </summary>
    protected static void AssertPointPassed(ContractReport report, ContractPoint point)
    {
        var result = Assert.Single(report.Results, r => r.Point == point);
        Assert.True(result.Passed, $"{point} should pass for {report.UnitDefinitionName}: {result.Detail}");
    }

    /// <summary>
    /// Asserts that a contract point is reported as an explicit gap (unimplemented handler), not a
    /// silent pass, and that the detail names the unit and special power.
    /// </summary>
    protected static ContractPointResult AssertPointIsGap(ContractReport report, ContractPoint point)
    {
        var result = Assert.Single(report.Results, r => r.Point == point);
        Assert.True(result.IsGap, $"{point} should be a gap for {report.UnitDefinitionName}");
        Assert.StartsWith("P6b gap:", result.Detail);
        return result;
    }

    /// <summary>
    /// Returns the KindOf tags a reference unit is expected to carry for the archetype.
    /// </summary>
    protected static IReadOnlyList<ObjectKinds> ReferenceKindOfTags(UnitArchetype archetype, string faction) =>
        UnitArchetypeCatalog.Resolve(archetype, faction)?.KindOfTags
            ?? System.Array.Empty<ObjectKinds>();
}
