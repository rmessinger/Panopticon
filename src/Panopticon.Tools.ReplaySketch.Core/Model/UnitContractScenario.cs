using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// Result of evaluating one <see cref="ContractPoint"/> for one archetype scenario.
/// A gap (unimplemented handler) has <see cref="IsGap"/> true and a <see cref="Detail"/>
/// beginning "P6b gap:"; it is never reported as <see cref="Passed"/>.
/// </summary>
public sealed class ContractPointResult
{
    public ContractPoint Point { get; init; }
    public ContractPointOutcome Outcome { get; init; }
    public bool Passed => Outcome == ContractPointOutcome.Passed;
    public bool IsGap => Outcome == ContractPointOutcome.Gap;
    public string? Detail { get; init; }

    public static ContractPointResult Pass(ContractPoint point) => new()
    {
        Point = point,
        Outcome = ContractPointOutcome.Passed,
    };

    public static ContractPointResult Fail(ContractPoint point, string detail) => new()
    {
        Point = point,
        Outcome = ContractPointOutcome.Failed,
        Detail = detail,
    };

    public static ContractPointResult Gap(ContractPoint point, string detail)
    {
        var message = detail.StartsWith("P6b gap:", StringComparison.Ordinal)
            ? detail
            : $"P6b gap: {detail}";
        return new ContractPointResult
        {
            Point = point,
            Outcome = ContractPointOutcome.Gap,
            Detail = message,
        };
    }
}

/// <summary>
/// Aggregate result of running one <see cref="UnitContractScenario"/>. Gaps are unimplemented
/// handlers surfaced explicitly; <see cref="AllContractPointsPassed"/> is false while any remain.
/// </summary>
public sealed class ContractReport
{
    public UnitArchetype Archetype { get; }
    public string FactionName { get; }
    public string UnitDefinitionName { get; }
    public IReadOnlyList<ContractPointResult> Results { get; }

    public ContractReport(
        UnitArchetype archetype,
        string factionName,
        string unitDefinitionName,
        IReadOnlyList<ContractPointResult> results)
    {
        Archetype = archetype;
        FactionName = factionName;
        UnitDefinitionName = unitDefinitionName;
        Results = results;
    }

    public bool AllContractPointsPassed => Results.Count > 0 && Results.All(r => r.Passed);
    public bool HasGaps => Results.Any(r => r.IsGap);
    public IEnumerable<ContractPointResult> Gaps => Results.Where(r => r.IsGap);

    public IEnumerable<string> GapMessages => Gaps.Select(g => g.Detail ?? string.Empty);
}

/// <summary>
/// One archetype contract scenario: the reference unit plus the special powers its command set
/// exposes. The six <see cref="ContractPoint"/>s are asserted in order. Built from
/// <see cref="UnitArchetypeCatalog"/>.
/// </summary>
public sealed class UnitContractScenario
{
    public UnitArchetype Archetype { get; init; }
    public string FactionName { get; init; } = string.Empty;
    public string UnitDefinitionName { get; init; } = string.Empty;
    public IReadOnlyList<string> SpecialPowerNames { get; init; } = [];

    /// <summary>The contract points this scenario asserts, in execution order.</summary>
    public IReadOnlyList<ContractPoint> ContractPoints { get; init; } = DefaultContractPoints;

    public static readonly IReadOnlyList<ContractPoint> DefaultContractPoints =
    [
        ContractPoint.Produced,
        ContractPoint.MoveOrStatic,
        ContractPoint.ArchetypeAction,
        ContractPoint.AttackOrDecline,
        ContractPoint.WeaponModesAndAbilities,
        ContractPoint.DieWithCleanup,
    ];
}
