using System;
using System.Linq;
using Panopticon.Benchmarks.Simulation.Experiments;
using Panopticon.Logic;
using Panopticon.Logic.AI;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// `b1.ai-policy`: two hostile slots where slot 0 is an AI owner driven by the
/// experiment-selected AI decision policy and slot 1 stays passive. The policy arrives as
/// data through the plan's experiment bindings; the scenario never branches on the policy
/// type, so every implementation of the `ai-policy` seam runs through one boot path
/// (WI-A4-41).
/// </summary>
public sealed class AiPolicyBenchmarkScenario : SimulationBenchmarkScenario
{
    public const string ScenarioId = "b1.ai-policy";

    public const string DefaultMapPath = "maps/alpine assault/alpine assault.map";

    // The AI slot's team; the passive slot boots hostile on the other team. Both non-human
    // slots get skirmish AI at boot, so ConfigurePlayers keeps only this team's host.
    private const int AiTeam = 0;

    private AIDecisionPolicyPayload? _selectedPolicy;

    public override string Id => ScenarioId;

    public override SimulationBenchmarkRun CreateRun(SimulationBenchmarkPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _selectedPolicy = ResolveSelectedPolicy(plan);
        try
        {
            return BootSkirmish(plan, plan.MapPath ?? DefaultMapPath, teamA: AiTeam, teamB: 1);
        }
        finally
        {
            _selectedPolicy = null;
        }
    }

    protected override PlayerSetting[] CreatePlayerSettings(Game game, int teamA, int teamB)
    {
        // Mirrors BenchmarkPlayerSettings.TwoSides with slot 0 promoted to an AI owner whose
        // policy payload is the experiment selection.
        var sides = game.GetPlayableSides().ToArray();

        var aiSlot = new PlayerSetting(
            0,
            sides[0].Name,
            game.AssetStore.MultiplayerColors.GetByIndex(0).RgbColor,
            teamA,
            PlayerOwner.EasyAi,
            "bench-ai");
        aiSlot.AIPolicy = _selectedPolicy;

        return
        [
            aiSlot,
            new PlayerSetting(
                1,
                sides[sides.Length > 1 ? 1 : 0].Name,
                game.AssetStore.MultiplayerColors.GetByIndex(1).RgbColor,
                teamB,
                PlayerOwner.None,
                "bench-b"),
        ];
    }

    protected override void ConfigurePlayers(Game game)
    {
        // Both non-human slots boot with a skirmish policy host and the map's civilian keeps
        // a plain AI host; keep only this team's skirmish host so per-tick thinking comes
        // solely from the selected policy.
        var keptPolicyHost = false;
        foreach (var player in game.Scene3D.Players)
        {
            if (player.Team != AiTeam || player.AIPlayer is not SkirmishAIPlayer)
            {
                player.DisableAI();
            }
            else
            {
                keptPolicyHost = true;
            }
        }

        if (!keptPolicyHost)
        {
            throw new InvalidOperationException(
                $"Scenario '{ScenarioId}' expected a booted AI player on team {AiTeam}.");
        }
    }

    private static AIDecisionPolicyPayload ResolveSelectedPolicy(SimulationBenchmarkPlan plan)
    {
        var binding = plan.ExperimentBindings?.FirstOrDefault(
            binding => binding.Descriptor.Seam == AiPolicyExperimentImplementations.SeamKey);

        if (binding is null)
        {
            throw new InvalidOperationException(
                $"Scenario '{ScenarioId}' requires an '{AiPolicyExperimentImplementations.SeamKey}' experiment "
                + "binding; run it through the experiment harness.");
        }

        return binding.Settings as AIDecisionPolicyPayload
            ?? throw new InvalidOperationException(
                $"The '{AiPolicyExperimentImplementations.SeamKey}' binding settings must be an "
                + $"{nameof(AIDecisionPolicyPayload)}.");
    }
}
