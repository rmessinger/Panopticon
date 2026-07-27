namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// The six end states the P7 regression suite drives a skirmish to. One per terminal
/// <c>GameOutcome</c> path. See docs/roadmap/deep-dives/p7-end-to-end-regression.md section 2.
/// </summary>
public enum EndState
{
    PlayerEliminated,
    PlayerSurrenders,
    AISurrenders,
    TeamWin,
    MutualElimination,
    ScoreDecision,
}
