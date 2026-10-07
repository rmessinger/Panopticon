using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Data;
using Panopticon.Logic;
using Panopticon.Logic.AI;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Simulation;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// The real match evaluator (WI-A4-16): runs one headless skirmish where slot 0 plays the
/// supplied genome through <see cref="AIDecisionPolicyPayload"/> and the rule-based
/// reference AI opposes it, steps it with <see cref="SimulationHost"/> to the frame cap or
/// a decided outcome, and returns the T0 run output for the fitness scorer.
/// </summary>
public sealed class HeadlessNeuralMatchEvaluator : INeuralPolicyMatchEvaluator
{
    public const string GenomePlayerName = "Genome";
    public const string OpponentPlayerName = "RuleBased";

    private readonly GameInstallation _installation;
    private readonly NeuralEvolutionConfig _config;

    public HeadlessNeuralMatchEvaluator(GameInstallation installation, NeuralEvolutionConfig config)
    {
        config.Validate();
        _installation = installation;
        _config = config;
    }

    public NeuralMatchEvaluation Evaluate(int genomeIndex, IReadOnlyList<float> genome, uint matchSeed)
    {
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
                        Type = AIDecisionPolicyType.NeuralNetwork,
                        NeuralNetworkWeights = genome.ToArray(),
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

            // Skirmish slot players take the faction's display name from the map's side
            // list, not the slot's Name; identify the genome player from the actual
            // player it was assigned to (slot 0 = player index 2 after neutral/civilian).
            var genomePlayer = game.Scene3D.Players[2];
            var genomePlayerName = genomePlayer.DisplayName;
            var opponentPlayerName = game.Scene3D.Players[3].DisplayName;
            if (genomePlayerName == opponentPlayerName)
            {
                throw new InvalidOperationException(
                    "Genome and opponent players are indistinguishable in run metrics; configure distinct factions.");
            }

            var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
            var recorder = new RunOutputRecorder(host, game, matchSeed, "neural-policy-evolution");

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
}
