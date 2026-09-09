#nullable enable

namespace OpenSage.Tools.ReplaySketch.Model;

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
