using System;
using System.Collections.Generic;
using Panopticon.Logic.Object;

namespace Panopticon.Logic;

public sealed class VictorySystem : GameSystem
{
    public static readonly LogicFrameSpan DefaultCheckCadence = new LogicFrameSpan(30);
    public const int NoFrameLimit = -1;

    private LogicFrame _nextCheckFrame = new LogicFrame(DefaultCheckCadence.Value);
    private readonly HashSet<Player> _defeatedPlayers = [];
    private readonly Dictionary<Player, uint> _minimumKnownRebuildCosts = [];
    private readonly HashSet<Player> _playersToRecheck = [];
    private bool _isAttached;
    private VictoryConditionRule _conditionRule = new DefaultVictoryConditionRule();
    private Func<Player, int> _scoreMetric = static player => player.BuildingCount;

    public GameOutcome CurrentOutcome { get; } = new();

    internal event EventHandler OutcomeDecided;

    public int CheckTickCount { get; private set; }
    internal int FrameLimit { get; set; } = NoFrameLimit;
    internal VictoryConditionRule ConditionRule
    {
        get => _conditionRule;
        set => _conditionRule = value ?? throw new System.ArgumentNullException(nameof(value));
    }

    internal Func<Player, int> ScoreMetric
    {
        get => _scoreMetric;
        set => _scoreMetric = value ?? throw new System.ArgumentNullException(nameof(value));
    }

    internal int LifecycleNotificationCount { get; private set; }
    internal int LastProcessedPlayerCount { get; private set; }
    internal int PendingPlayerRecheckCount => _playersToRecheck.Count;

    internal LogicFrame LastCheckFrame { get; private set; } = LogicFrame.Zero;

    public VictorySystem(IGame game)
        : base(game)
    {
    }

    internal void ApplyOptions(VictoryOptions options)
    {
        if (options == null)
        {
            return;
        }

        FrameLimit = options.FrameLimit;
        ScoreMetric = VictoryScoreMetrics.ToSelector(options.ScoreMetric);
    }

    internal void Attach(GameLogic gameLogic)
    {
        if (_isAttached)
        {
            return;
        }

        gameLogic.ObjectLifecycleChanged += OnObjectLifecycleChanged;
        _isAttached = true;
    }

    internal void Tick(LogicFrame currentFrame)
    {
        if (CurrentOutcome.Status == OutcomeStatus.Decided || currentFrame < _nextCheckFrame)
        {
            return;
        }

        CheckTickCount++;
        LastCheckFrame = currentFrame;
        LastProcessedPlayerCount = _playersToRecheck.Count;
        EvaluatePendingPlayers();
        EvaluateFrameLimit(currentFrame);
        _playersToRecheck.Clear();
        _nextCheckFrame = currentFrame + DefaultCheckCadence;
    }

    private void OnObjectLifecycleChanged(object sender, GameObjectLifecycleEventArgs e)
    {
        LifecycleNotificationCount++;
        TrackKnownRebuildCost(e.GameObject, e.PreviousOwner);
        TrackKnownRebuildCost(e.GameObject, e.CurrentOwner);

        if (e.PreviousOwner != null)
        {
            QueuePlayerForEvaluation(e.PreviousOwner);
        }

        if (e.CurrentOwner != null)
        {
            QueuePlayerForEvaluation(e.CurrentOwner);
        }
    }

    internal bool HasEnoughResourcesToRebuild(Player player)
    {
        return TryGetMinimumKnownRebuildCost(player, out var rebuildCost)
            && player.BankAccount.Money >= rebuildCost;
    }

    internal bool IsPlayerDefeated(Player player)
    {
        return _defeatedPlayers.Contains(player);
    }

    internal bool Surrender(Player player)
    {
        if (player == null || CurrentOutcome.Status == OutcomeStatus.Decided)
        {
            return false;
        }

        _defeatedPlayers.Add(player);

        var winningPlayers = GetOpposingPlayers(player);
        if (winningPlayers.Count == 0)
        {
            return false;
        }

        return TrySetDecidedOutcome(OutcomeReason.Surrendered, winningPlayers, player);
    }

    internal void QueuePlayerForEvaluation(Player player)
    {
        if (player != null)
        {
            _playersToRecheck.Add(player);
        }
    }

    private void EvaluatePendingPlayers()
    {
        if (CurrentOutcome.Status == OutcomeStatus.Decided)
        {
            return;
        }

        var newlyDefeatedPlayers = new List<Player>();
        foreach (var player in _playersToRecheck)
        {
            if (!IsCompetitivePlayer(player) || _defeatedPlayers.Contains(player))
            {
                continue;
            }

            if (!ConditionRule.IsPlayerDefeated(player, this))
            {
                continue;
            }

            newlyDefeatedPlayers.Add(player);
        }

        if (newlyDefeatedPlayers.Count == 0)
        {
            return;
        }

        if (CurrentOutcome.Status != OutcomeStatus.SuddenDeath && ShouldEnterSuddenDeath(newlyDefeatedPlayers))
        {
            EnterSuddenDeath();
            return;
        }

        foreach (var newlyDefeatedPlayer in newlyDefeatedPlayers)
        {
            _defeatedPlayers.Add(newlyDefeatedPlayer);
        }

        foreach (var defeatedPlayer in newlyDefeatedPlayers)
        {
            var winningPlayers = TryResolveWinningPlayers(defeatedPlayer);
            if (winningPlayers == null)
            {
                continue;
            }

            TrySetDecidedOutcome(OutcomeReason.Eliminated, winningPlayers, defeatedPlayer);
            break;
        }

        // A repeat full mutual elimination while already in sudden death leaves no
        // survivor to declare winner; resolve it as a shared defeat among the last
        // cohort instead of looping in sudden death indefinitely.
        ResolveAllPlayersEliminated(newlyDefeatedPlayers);
    }

    private void ResolveAllPlayersEliminated(List<Player> lastDefeatedCohort)
    {
        if (CurrentOutcome.Status == OutcomeStatus.Decided)
        {
            return;
        }

        foreach (var player in GetCompetitivePlayers())
        {
            if (!_defeatedPlayers.Contains(player))
            {
                return;
            }
        }

        TrySetDecidedOutcome(OutcomeReason.Eliminated, lastDefeatedCohort, null);
    }

    private List<Player> TryResolveWinningPlayers(Player defeatedPlayer)
    {
        foreach (var candidate in GetCompetitivePlayers())
        {
            if (_defeatedPlayers.Contains(candidate) || candidate == defeatedPlayer)
            {
                continue;
            }

            var hasRemainingOpponent = false;
            foreach (var otherPlayer in GetCompetitivePlayers())
            {
                if (_defeatedPlayers.Contains(otherPlayer) || otherPlayer == candidate)
                {
                    continue;
                }

                if (!candidate.Allies.Contains(otherPlayer))
                {
                    hasRemainingOpponent = true;
                    break;
                }
            }

            if (hasRemainingOpponent)
            {
                continue;
            }

            var winners = new List<Player> { candidate };
            foreach (var ally in candidate.Allies)
            {
                if (IsCompetitivePlayer(ally) && !_defeatedPlayers.Contains(ally) && !winners.Contains(ally))
                {
                    winners.Add(ally);
                }
            }

            return winners;
        }

        return null;
    }

    private Team TryResolveWinningTeam(List<Player> winningPlayers)
    {
        if (winningPlayers.Count == 0)
        {
            return null;
        }

        var winningTeam = winningPlayers[0].DefaultTeam;
        if (winningTeam == null)
        {
            return null;
        }

        foreach (var winningPlayer in winningPlayers)
        {
            if (winningPlayer.DefaultTeam != winningTeam)
            {
                return null;
            }
        }

        return winningTeam;
    }

    private void EnterSuddenDeath()
    {
        CurrentOutcome.Status = OutcomeStatus.SuddenDeath;
        CurrentOutcome.Reason = OutcomeReason.None;
        CurrentOutcome.LosingPlayer = null;
        CurrentOutcome.WinningTeam = null;
        CurrentOutcome.SetWinningPlayers(null);
        CurrentOutcome.DecidedOnFrame = null;
    }

    private bool TrySetDecidedOutcome(OutcomeReason reason, List<Player> winningPlayers, Player losingPlayer)
    {
        if (CurrentOutcome.Status == OutcomeStatus.Decided)
        {
            return false;
        }

        CurrentOutcome.Status = OutcomeStatus.Decided;
        CurrentOutcome.Reason = reason;
        CurrentOutcome.LosingPlayer = losingPlayer;
        CurrentOutcome.SetWinningPlayers(winningPlayers);
        CurrentOutcome.WinningTeam = TryResolveWinningTeam(winningPlayers);
        CurrentOutcome.DecidedOnFrame = unchecked((int)Game.GameLogic.CurrentFrame.Value);
        Game.IsLogicRunning = false;
        OutcomeDecided?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private IEnumerable<Player> GetCompetitivePlayers()
    {
        foreach (var player in Game.PlayerManager.Players)
        {
            if (IsCompetitivePlayer(player))
            {
                yield return player;
            }
        }
    }

    private bool IsCompetitivePlayer(Player player)
    {
        return Game.PlayerManager.GetPlayerIndex(player) >= 2;
    }

    private void TrackKnownRebuildCost(GameObject gameObject, Player owner)
    {
        if (gameObject == null || owner == null || !ConditionRule.IsQualifyingStructure(gameObject))
        {
            return;
        }

        var buildCost = (uint)gameObject.Definition.BuildCost;
        if (buildCost == 0)
        {
            return;
        }

        if (_minimumKnownRebuildCosts.TryGetValue(owner, out var existingCost))
        {
            _minimumKnownRebuildCosts[owner] = System.Math.Min(existingCost, buildCost);
        }
        else
        {
            _minimumKnownRebuildCosts.Add(owner, buildCost);
        }
    }

    private bool TryGetMinimumKnownRebuildCost(Player player, out uint rebuildCost)
    {
        rebuildCost = uint.MaxValue;

        foreach (var ownedObject in player.OwnedObjects)
        {
            if (!ConditionRule.IsQualifyingStructure(ownedObject))
            {
                continue;
            }

            var buildCost = (uint)ownedObject.Definition.BuildCost;
            if (buildCost == 0)
            {
                continue;
            }

            rebuildCost = System.Math.Min(rebuildCost, buildCost);
        }

        if (rebuildCost != uint.MaxValue)
        {
            if (_minimumKnownRebuildCosts.TryGetValue(player, out var cachedCost))
            {
                rebuildCost = System.Math.Min(rebuildCost, cachedCost);
            }

            _minimumKnownRebuildCosts[player] = rebuildCost;

            return true;
        }

        return _minimumKnownRebuildCosts.TryGetValue(player, out rebuildCost);
    }

    private List<Player> GetOpposingPlayers(Player player)
    {
        var winners = new List<Player>();
        foreach (var candidate in GetCompetitivePlayers())
        {
            if (_defeatedPlayers.Contains(candidate) || candidate == player || candidate.Allies.Contains(player))
            {
                continue;
            }

            winners.Add(candidate);
        }

        return winners;
    }

    private bool ShouldEnterSuddenDeath(List<Player> newlyDefeatedPlayers)
    {
        var activePlayers = new List<Player>();
        foreach (var player in GetCompetitivePlayers())
        {
            if (!_defeatedPlayers.Contains(player))
            {
                activePlayers.Add(player);
            }
        }

        return activePlayers.Count > 1 && newlyDefeatedPlayers.Count == activePlayers.Count;
    }

    private void EvaluateFrameLimit(LogicFrame currentFrame)
    {
        if (CurrentOutcome.Status == OutcomeStatus.Decided || FrameLimit == NoFrameLimit || currentFrame.Value < FrameLimit)
        {
            return;
        }

        var winningPlayers = new List<Player>();
        var bestScore = int.MinValue;

        foreach (var player in GetCompetitivePlayers())
        {
            if (_defeatedPlayers.Contains(player))
            {
                continue;
            }

            var score = ScoreMetric(player);
            if (score > bestScore)
            {
                winningPlayers.Clear();
                winningPlayers.Add(player);
                bestScore = score;
            }
            else if (score == bestScore)
            {
                winningPlayers.Add(player);
            }
        }

        if (winningPlayers.Count > 0)
        {
            TrySetDecidedOutcome(OutcomeReason.ScoreDecision, winningPlayers, null);
        }
    }
}
