#nullable enable

using Panopticon.Logic.Object;

namespace Panopticon.Logic.AI.AIStates;

internal sealed class ChinookExitMapState : State
{
    internal ChinookExitMapState(ChinookAIUpdateStateMachine stateMachine) : base(stateMachine)
    {
    }

    public override void Persist(StatePersister reader) { }
}
