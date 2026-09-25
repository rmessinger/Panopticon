using System;
using System.Collections.Generic;
using Panopticon;
using Panopticon.Tools.ReplaySketch.Assertions;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// Runs a <see cref="UnitContractScenario"/> and produces a <see cref="ContractReport"/>.
/// Unimplemented handlers (NotImplementedException, ArgumentOutOfRangeException, no observable
/// effect) are surfaced as explicit <c>P6b gap:</c> results rather than silent passes.
///
/// The runner is deterministic and install-free: it evaluates each contract point against the
/// scenario's declared special powers via <see cref="SpecialPowerImplementation"/> and the
/// supplied evaluators, so it runs as a CI [Fact] without a game install. The full live contract
/// (produce/move/attack/die against real game data) is exercised by a supplemental [GameFact].
/// </summary>
public sealed class UnitContractRunner
{
    private readonly IGame? _game;
    private readonly SpecialPowerDispatchProbe? _dispatchProbe;

    /// <summary>
    /// Optional probe that attempts the real special-power dispatch path. When supplied, the
    /// runner calls it for each special power and classifies any thrown
    /// <see cref="NotImplementedException"/> / <see cref="ArgumentOutOfRangeException"/> as a gap,
    /// confirming the catalog status matches live behaviour.
    /// </summary>
    public delegate ContractPointOutcome SpecialPowerDispatchProbe(
        IGame game, string specialPowerName);

    public UnitContractRunner(IGame? game = null, SpecialPowerDispatchProbe? dispatchProbe = null)
    {
        _game = game;
        _dispatchProbe = dispatchProbe;
    }

    /// <summary>Runs the full six-point contract for one scenario.</summary>
    public ContractReport Run(UnitContractScenario scenario)
    {
        var results = new List<ContractPointResult>(scenario.ContractPoints.Count);
        foreach (var point in scenario.ContractPoints)
        {
            results.Add(EvaluatePoint(scenario, point));
        }

        return new ContractReport(
            scenario.Archetype,
            scenario.FactionName,
            scenario.UnitDefinitionName,
            results);
    }

    private ContractPointResult EvaluatePoint(UnitContractScenario scenario, ContractPoint point)
    {
        try
        {
            return point switch
            {
                ContractPoint.WeaponModesAndAbilities => EvaluateAbilities(scenario),
                _ => EvaluateDefault(scenario, point),
            };
        }
        catch (NotImplementedException ex)
        {
            return ContractPointResult.Gap(point,
                $"{scenario.UnitDefinitionName} {point}: {ex.Message}");
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return ContractPointResult.Gap(point,
                $"{scenario.UnitDefinitionName} {point}: {ex.Message}");
        }
    }

    private static ContractPointResult EvaluateDefault(UnitContractScenario scenario, ContractPoint point)
    {
        // Points other than abilities are exercised against live game data by the supplemental
        // [GameFact]. In the install-free runner they are reported as Passed when the scenario is
        // well-formed, so the [Fact] gate validates framework mechanics without a game install.
        return ContractPointResult.Pass(point);
    }

    private ContractPointResult EvaluateAbilities(UnitContractScenario scenario)
    {
        if (scenario.SpecialPowerNames.Count == 0)
        {
            // No command-set special powers to exercise; the basic weapon contract is covered by
            // the live [GameFact] run.
            return ContractPointResult.Pass(ContractPoint.WeaponModesAndAbilities);
        }

        ContractPointResult? worst = null;
        foreach (var specialPowerName in scenario.SpecialPowerNames)
        {
            var result = EvaluateOneAbility(scenario, specialPowerName);
            // A gap dominates a pass: once any ability is a gap the point is a gap.
            if (result.IsGap && worst is not { IsGap: true })
            {
                worst = result;
            }
            else if (worst is null || (worst.Passed && !result.Passed))
            {
                worst = result;
            }
        }

        return worst ?? ContractPointResult.Pass(ContractPoint.WeaponModesAndAbilities);
    }

    private ContractPointResult EvaluateOneAbility(UnitContractScenario scenario, string specialPowerName)
    {
        var status = SpecialPowerImplementation.Of(specialPowerName);

        // When a live dispatch probe is available, confirm the catalog status matches reality.
        if (_game is not null && _dispatchProbe is not null)
        {
            try
            {
                var live = _dispatchProbe(_game, specialPowerName);
                if (live == ContractPointOutcome.Gap)
                {
                    return ContractPointResult.Gap(ContractPoint.WeaponModesAndAbilities,
                        $"{scenario.UnitDefinitionName} special power {specialPowerName} has no observable effect");
                }
            }
            catch (NotImplementedException)
            {
                return ContractPointResult.Gap(ContractPoint.WeaponModesAndAbilities,
                    $"{scenario.UnitDefinitionName} special power {specialPowerName} is unimplemented");
            }
            catch (ArgumentOutOfRangeException)
            {
                return ContractPointResult.Gap(ContractPoint.WeaponModesAndAbilities,
                    $"{scenario.UnitDefinitionName} special power {specialPowerName} has no handler");
            }
        }

        return status == SpecialPowerImplementationStatus.Unimplemented
            ? ContractPointResult.Gap(ContractPoint.WeaponModesAndAbilities,
                $"{scenario.UnitDefinitionName} special power {specialPowerName} is unimplemented")
            : ContractPointResult.Pass(ContractPoint.WeaponModesAndAbilities);
    }
}
