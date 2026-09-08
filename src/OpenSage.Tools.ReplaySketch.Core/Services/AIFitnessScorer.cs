#nullable enable

using System;
using System.Linq;
using OpenSage.Logic;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// One scored match: every fitness term separately visible (WI-A4-14, design
/// implementation/track-a-a4-ai-policy-evolution.md §3).
/// </summary>
public sealed record NeuralFitnessResult(
    double Total,
    double OutcomeTerm,
    double EconomyTerm,
    double ArmyTerm,
    double SpeedTerm,
    string OutcomeLabel)
{
    public const double WinOutcome = 100.0;
    public const double SuddenDeathOutcome = 50.0;
    public const double FrameCapOutcome = 40.0;
    public const double LossOutcome = 0.0;
}

/// <summary>
/// Converts a match's T0 <see cref="RunOutput"/> (terminal outcome record plus per-player
/// metric samples) into the documented shaped scalar fitness. Consumes only existing
/// gameplay instrumentation; incomplete or incompatible outputs throw explicitly, with no
/// success-shaped fallback.
/// </summary>
public static class AIFitnessScorer
{
    public static NeuralFitnessResult Score(RunOutput? output, string genomePlayerName, int matchFrameCap)
    {
        if (output is null)
        {
            throw new InvalidOperationException("Fitness requires a run output.");
        }
        if (output.Outcome is null)
        {
            throw new InvalidOperationException("Fitness requires a terminal outcome record.");
        }
        if (output.MetricSamples.Count == 0)
        {
            throw new InvalidOperationException("Fitness requires at least one metric sample.");
        }
        if (matchFrameCap < 1)
        {
            throw new InvalidOperationException("Match frame cap must be positive.");
        }

        var (outcomeTerm, outcomeLabel, decidedOnFrame) = ClassifyOutcome(output, genomePlayerName, matchFrameCap);
        var (finalMoney, finalOwnedObjects) = FinalMetrics(output, genomePlayerName);

        var economyTerm = 10.0 * Math.Min(finalMoney / 10_000.0, 1.0);
        var armyTerm = 10.0 * Math.Min(finalOwnedObjects / 30.0, 1.0);
        var speedTerm = outcomeLabel == "win" && decidedOnFrame is { } frame
            ? 20.0 * (1.0 - Math.Min((double)frame / matchFrameCap, 1.0))
            : 0.0;

        return new NeuralFitnessResult(
            outcomeTerm + economyTerm + armyTerm + speedTerm,
            outcomeTerm,
            economyTerm,
            armyTerm,
            speedTerm,
            outcomeLabel);
    }

    private static (double Term, string Label, int? DecidedOnFrame) ClassifyOutcome(
        RunOutput output, string genomePlayerName, int matchFrameCap)
    {
        var outcome = output.Outcome!;
        switch (outcome.Status)
        {
            case OutcomeStatus.Decided:
                return outcome.WinningPlayers.Contains(genomePlayerName)
                    ? (NeuralFitnessResult.WinOutcome, "win", outcome.DecidedOnFrame)
                    : (NeuralFitnessResult.LossOutcome, "loss", outcome.DecidedOnFrame);
            case OutcomeStatus.SuddenDeath:
                return (NeuralFitnessResult.SuddenDeathOutcome, "suddenDeath", null);
            case OutcomeStatus.InProgress:
                if (output.TotalLogicFrames < matchFrameCap)
                {
                    throw new InvalidOperationException(
                        $"Match ended in progress at frame {output.TotalLogicFrames} before the frame cap {matchFrameCap}.");
                }

                return (NeuralFitnessResult.FrameCapOutcome, "frameCap", null);
            default:
                throw new InvalidOperationException($"Unknown outcome status {outcome.Status}.");
        }
    }

    private static (uint Money, int OwnedObjects) FinalMetrics(RunOutput output, string genomePlayerName)
    {
        var last = output.MetricSamples[^1];
        var row = last.Players.FirstOrDefault(p => p.Player == genomePlayerName);
        if (row is null)
        {
            throw new InvalidOperationException(
                $"Genome player '{genomePlayerName}' is absent from the run's metric samples.");
        }

        return (row.Money, row.OwnedObjects);
    }
}
