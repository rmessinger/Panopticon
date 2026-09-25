using System;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Default <see cref="ITestHarnessReporter"/> that writes to <see cref="Console"/>, preserving the
/// exact line formats documented in AGENTS.md (<c>[dispatched]</c>, <c>[completed]</c>,
/// <c>[match-decided]</c>) since agents and parity tooling watch stdout for these strings.
/// </summary>
public sealed class ConsoleTestHarnessReporter : ITestHarnessReporter
{
    public void Info(string message) => Console.WriteLine(message);

    public void Error(string message) => Console.Error.WriteLine(message);

    public void ActionDispatched(ActionType actionType, string? playerName, uint frame) =>
        Console.WriteLine($"  [dispatched] {actionType,-20} player: {playerName ?? "?"} frame: {frame}");

    public void ActionCompleted(ActionType actionType, string? playerName, uint frame) =>
        Console.WriteLine($"  [completed]  {actionType,-20} player: {playerName ?? "?"} frame: {frame}");

    public void MatchOutcomeDecided(Logic.OutcomeStatus status, Logic.OutcomeReason reason, string? winnerName, uint frame) =>
        Console.WriteLine($"  [match-decided] status: {status} reason: {reason} winner: {winnerName ?? "(shared)"} frame: {frame}");
}
