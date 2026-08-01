using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.TestHarness;

/// <summary>
/// Receives all TestHarness run status: routine/error messages, per-action dispatch and
/// completion notifications, and match-outcome notifications. <see cref="ScenarioBuilder"/>,
/// <see cref="ReplayScenarioRunner"/> and <see cref="ValidationGroundRunner"/> report through this
/// interface instead of writing to <see cref="System.Console"/> directly, so every status string
/// lives in one place (<see cref="ConsoleTestHarnessReporter"/>).
/// </summary>
public interface ITestHarnessReporter
{
    /// <summary>A routine status message (progress, saved-file paths, report text).</summary>
    void Info(string message);

    /// <summary>A failure or diagnostic message.</summary>
    void Error(string message);

    /// <summary>An <see cref="ActionType"/> was dispatched to the engine for the given player.</summary>
    void ActionDispatched(ActionType actionType, string? playerName, uint frame);

    /// <summary>An <see cref="ActionType"/> finished (e.g. a move arrived, a target was destroyed).</summary>
    void ActionCompleted(ActionType actionType, string? playerName, uint frame);

    /// <summary>The match reached a decided (or sudden-death) outcome.</summary>
    void MatchOutcomeDecided(Logic.OutcomeStatus status, Logic.OutcomeReason reason, string? winnerName, uint frame);
}
