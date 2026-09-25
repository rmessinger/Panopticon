#nullable enable

using System.Numerics;
using Panopticon.Logic.Object;

namespace Panopticon.Logic.AI.AIStates;

internal sealed class ChinookTakeoffAndLandingState : State
{
    private Vector3 _targetPosition;
    private bool _landing;

    internal ChinookTakeoffAndLandingState(ChinookAIUpdateStateMachine stateMachine, bool landing) : base(stateMachine)
    {
        _landing = landing;
    }

    public override void Persist(StatePersister reader)
    {
        reader.PersistVersion(1);

        reader.PersistVector3(ref _targetPosition);
        reader.PersistBoolean(ref _landing);
    }
}
