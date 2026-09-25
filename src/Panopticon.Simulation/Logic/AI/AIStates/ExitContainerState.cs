#nullable enable

using Panopticon.Logic.Object;

namespace Panopticon.Logic.AI.AIStates;

internal sealed class ExitContainerState : State
{
    private ObjectId _containerObjectId;

    internal ExitContainerState(AIUpdateStateMachine stateMachine) : base(stateMachine)
    {
    }

    public override void Persist(StatePersister reader)
    {
        reader.PersistVersion(1);

        reader.PersistObjectId(ref _containerObjectId);
    }
}
