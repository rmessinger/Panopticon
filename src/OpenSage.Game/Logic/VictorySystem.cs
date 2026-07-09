using OpenSage.Logic.Object;

namespace OpenSage.Logic;

public sealed class VictorySystem : GameSystem
{
    public static readonly LogicFrameSpan DefaultCheckCadence = new LogicFrameSpan(30);

    private LogicFrame _nextCheckFrame = new LogicFrame(DefaultCheckCadence.Value);

    public GameOutcome CurrentOutcome { get; } = new();

    public int CheckTickCount { get; private set; }

    internal LogicFrame LastCheckFrame { get; private set; } = LogicFrame.Zero;

    public VictorySystem(IGame game)
        : base(game)
    {
    }

    internal void Tick(LogicFrame currentFrame)
    {
        if (CurrentOutcome.Status == OutcomeStatus.Decided || currentFrame < _nextCheckFrame)
        {
            return;
        }

        CheckTickCount++;
        LastCheckFrame = currentFrame;
        _nextCheckFrame = currentFrame + DefaultCheckCadence;
    }
}
