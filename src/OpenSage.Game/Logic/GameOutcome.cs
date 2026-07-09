using System.Collections.Generic;

namespace OpenSage.Logic;

public enum OutcomeStatus
{
    InProgress,
    SuddenDeath,
    Decided,
}

public enum OutcomeReason
{
    None,
    Eliminated,
    Surrendered,
    ScoreDecision,
}

public sealed class GameOutcome
{
    private readonly List<Player> _winningPlayers = [];

    public OutcomeStatus Status { get; internal set; } = OutcomeStatus.InProgress;

    public OutcomeReason Reason { get; internal set; } = OutcomeReason.None;

    public Player WinningPlayer => _winningPlayers.Count == 1 ? _winningPlayers[0] : null;

    public IReadOnlyList<Player> WinningPlayers => _winningPlayers;

    public Team WinningTeam { get; internal set; }

    public Player LosingPlayer { get; internal set; }

    public int? DecidedOnFrame { get; internal set; }

    internal void Reset()
    {
        Status = OutcomeStatus.InProgress;
        Reason = OutcomeReason.None;
        WinningTeam = null;
        LosingPlayer = null;
        DecidedOnFrame = null;
        _winningPlayers.Clear();
    }

    internal void SetWinningPlayers(IEnumerable<Player> winningPlayers)
    {
        _winningPlayers.Clear();
        if (winningPlayers == null)
        {
            return;
        }

        _winningPlayers.AddRange(winningPlayers);
    }
}
