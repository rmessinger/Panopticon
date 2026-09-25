using System;

namespace Panopticon.Logic.Object;

internal abstract class FixedDurationWeaponState : BaseWeaponState
{
    private LogicFrame _exitTime;

    protected abstract RangeDuration Duration { get; }

    protected FixedDurationWeaponState(WeaponStateContext context)
        : base(context)
    {
    }

    protected override void OnEnterStateImpl()
    {
        // TODO: Randomly pick value between Duration.Min and Duration.Max
        // A state occupies at least one logic frame. Without this, a zero-min duration makes
        // _exitTime == CurrentFrame, so IsTimeToExitState is immediately true and the state machine
        // re-enters the fire cycle (Firing/IdleAfterFiring/BetweenShots) forever within a single
        // tick (CurrentFrame does not advance inside WeaponStateMachine.Update's transition loop).
        _exitTime = Context.GameEngine.GameLogic.CurrentFrame + new LogicFrameSpan(Math.Max(1u, Duration.Min.Value));
    }

    protected bool IsTimeToExitState() =>
        Context.GameEngine.GameLogic.CurrentFrame >= _exitTime;
}
