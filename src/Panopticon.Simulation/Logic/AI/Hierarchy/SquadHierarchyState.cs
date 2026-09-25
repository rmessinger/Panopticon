#nullable enable

using System.Collections.Generic;

namespace Panopticon.Logic.AI.Hierarchy;

/// <summary>
/// Persistable snapshot of a squad hierarchy (WI-A4-19): squad identities, commander
/// identities, and member object ids only. Object ids resolve lazily against live objects
/// after load, never during persistence.
/// </summary>
public sealed class SquadHierarchyState : IPersistableObject
{
    public List<SquadState> Squads { get; } = new();

    public void Persist(StatePersister reader)
    {
        reader.PersistVersion(1);

        reader.PersistList(
            Squads,
            static (StatePersister persister, ref SquadState item) =>
            {
                item ??= new SquadState();
                persister.PersistObjectValue(item);
            });
    }

    public sealed class SquadState : IPersistableObject
    {
        public uint Id { get; set; }

        /// <summary>Object id index of the commander; 0 when the squad has none yet.</summary>
        public uint CommanderId { get; set; }

        public List<uint> MemberIds { get; } = new();

        public void Persist(StatePersister reader)
        {
            reader.PersistVersion(1);

            var id = Id;
            reader.PersistUInt32(ref id);
            Id = id;

            var commanderId = CommanderId;
            reader.PersistUInt32(ref commanderId);
            CommanderId = commanderId;

            reader.PersistList(
                MemberIds,
                static (StatePersister persister, ref uint item) => persister.PersistUInt32Value(ref item));
        }
    }
}
