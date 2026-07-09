using System.Collections.Generic;
using OpenSage.Logic.Object;

namespace OpenSage.Logic;

public sealed class VictorySystem : GameSystem
{
    public static readonly LogicFrameSpan DefaultCheckCadence = new LogicFrameSpan(30);

    private LogicFrame _nextCheckFrame = new LogicFrame(DefaultCheckCadence.Value);
    private readonly HashSet<Player> _playersToRecheck = [];
    private bool _isAttached;

    public GameOutcome CurrentOutcome { get; } = new();

    public int CheckTickCount { get; private set; }
    internal int LifecycleNotificationCount { get; private set; }
    internal int LastProcessedPlayerCount { get; private set; }
    internal int PendingPlayerRecheckCount => _playersToRecheck.Count;

    internal LogicFrame LastCheckFrame { get; private set; } = LogicFrame.Zero;

    public VictorySystem(IGame game)
        : base(game)
    {
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
        _playersToRecheck.Clear();
        _nextCheckFrame = currentFrame + DefaultCheckCadence;
    }

    private void OnObjectLifecycleChanged(object sender, GameObjectLifecycleEventArgs e)
    {
        LifecycleNotificationCount++;

        if (e.PreviousOwner != null)
        {
            _playersToRecheck.Add(e.PreviousOwner);
        }

        if (e.CurrentOwner != null)
        {
            _playersToRecheck.Add(e.CurrentOwner);
        }
    }
}
