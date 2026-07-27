using OpenSage.Tools.ReplaySketch.Assertions;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// Metadata for one P7 regression scenario: its end state, the checked-in scenario file, and the
/// expected terminal <c>GameOutcome</c> the scenario must reach. The outcome assertion is built from
/// these fields, so the full expectation is authoring-time data, not code.
/// </summary>
public sealed class RegressionScenario
{
    public EndState EndState { get; init; }

    /// <summary>File name under <c>Scenarios/Regression/</c>, e.g. <c>player-eliminated.json</c>.</summary>
    public string ScenarioFileName { get; init; } = string.Empty;

    /// <summary>Expected <c>OutcomeStatus</c> name (<c>Decided</c> / <c>SuddenDeath</c>).</summary>
    public string ExpectedStatus { get; init; } = "Decided";

    /// <summary>Expected <c>OutcomeReason</c> name (<c>Eliminated</c> / <c>Surrendered</c> / <c>ScoreDecision</c>).</summary>
    public string ExpectedReason { get; init; } = "Eliminated";

    /// <summary>Expected winning competitive-player index (0-based into PlayerManager.Players).</summary>
    public int? ExpectedWinningPlayerIndex { get; init; }

    /// <summary>Expected losing competitive-player index.</summary>
    public int? ExpectedLosingPlayerIndex { get; init; }

    /// <summary>Expected winning team id, set for team-win scenarios.</summary>
    public int? ExpectedWinningTeam { get; init; }

    /// <summary>Builds the harness outcome assertion for this scenario's terminal state.</summary>
    public OutcomeAssertion BuildOutcomeAssertion() => new()
    {
        Id = $"p7-{EndState}",
        ExpectedStatus = ExpectedStatus,
        ExpectedReason = ExpectedReason,
        ExpectedWinningPlayerIndex = ExpectedWinningPlayerIndex,
        ExpectedLosingPlayerIndex = ExpectedLosingPlayerIndex,
        ExpectedWinningTeam = ExpectedWinningTeam,
    };
}
