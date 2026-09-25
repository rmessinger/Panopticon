#nullable enable

using System.Collections.Generic;
using System.Linq;
using Panopticon.Data;
using Panopticon.Logic;
using Panopticon.Logic.AI;
using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Simulation;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// The real hierarchical match evaluator (WI-A4-30): one headless skirmish where slot 0
/// plays the assembled hierarchy (advancing genome plus partner genomes, through
/// <see cref="HierarchicalRankWeights"/>) against the rule-based reference opponent, stepped
/// by <see cref="SimulationHost"/> to the frame cap or a decided outcome.
/// </summary>
public sealed class HeadlessHierarchicalMatchEvaluator : IHierarchicalPolicyMatchEvaluator
{
    public const string GenomePlayerName = "Genome";
    public const string OpponentPlayerName = "RuleBased";

    private readonly GameInstallation _installation;
    private readonly NeuralEvolutionConfig _config;

    public HeadlessHierarchicalMatchEvaluator(GameInstallation installation, NeuralEvolutionConfig config)
    {
        config.Validate();
        _installation = installation;
        _config = config;
    }

    public NeuralMatchEvaluation Evaluate(
        NeuralRankKind advancingRank,
        int genomeIndex,
        IReadOnlyList<float> genome,
        IReadOnlyDictionary<NeuralRankKind, IReadOnlyList<float>> partners,
        uint matchSeed)
    {
        var general = WeightOf(NeuralRankKind.General, genome, partners);
        var commander = WeightOf(NeuralRankKind.Commander, genome, partners);
        var soldier = WeightOf(NeuralRankKind.Soldier, genome, partners);

        Platform.Start();
        try
        {
            using var game = new Game(
                _installation,
                preferredBackend: null,
                new Configuration { NoAudio = true },
                window: null);

            var playerSettings = new PlayerSetting[]
            {
                new(0, _config.GenomeFaction, new ColorRgb(255, 0, 0), 0, PlayerOwner.MediumAi, GenomePlayerName)
                {
                    AIPolicy = new AIDecisionPolicyPayload
                    {
                        Type = AIDecisionPolicyType.Hierarchical,
                        HierarchicalRankWeights = new HierarchicalRankWeights(
                            Soldier: soldier.ToArray(),
                            Commander: commander.ToArray(),
                            General: general.ToArray()),
                    },
                },
                new(1, _config.OpponentFaction, new ColorRgb(0, 255, 0), 1, PlayerOwner.EasyAi, OpponentPlayerName),
            };

            game.StartSkirmishOrMultiPlayerGame(
                _config.MapPath,
                new EchoConnection(),
                playerSettings,
                (int)matchSeed,
                isMultiPlayer: false);
            game.StartRun();

            var genomePlayer = game.Scene3D.Players[2];
            var genomePlayerName = genomePlayer.DisplayName;
            var opponentPlayerName = game.Scene3D.Players[3].DisplayName;
            if (genomePlayerName == opponentPlayerName)
            {
                throw new System.InvalidOperationException(
                    "Genome and opponent players are indistinguishable in run metrics; configure distinct factions.");
            }

            var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
            var recorder = new RunOutputRecorder(host, game, matchSeed, "hierarchical-neural-evolution");

            RunOutput output;
            try
            {
                host.Run(
                    _config.MatchFrameCap,
                    h => !game.IsRunning || game.CurrentOutcome.Status == OutcomeStatus.Decided);
            }
            finally
            {
                output = recorder.Complete();
                recorder.Dispose();
            }

            return new NeuralMatchEvaluation(output, genomePlayerName);
        }
        finally
        {
            Platform.Stop();
        }
    }

    private static IReadOnlyList<float> WeightOf(
        NeuralRankKind rank,
        IReadOnlyList<float> advancingGenome,
        IReadOnlyDictionary<NeuralRankKind, IReadOnlyList<float>> partners) =>
        partners.TryGetValue(rank, out var partner) ? partner : advancingGenome;
}
