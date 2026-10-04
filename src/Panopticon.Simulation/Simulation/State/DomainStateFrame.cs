#nullable enable

using System.Collections.Generic;

namespace Panopticon.Simulation;

/// <summary>
/// The complete state of one domain at one frame. Records are ordinal by <see cref="DomainStateRecord.Key"/>
/// for stable diffs and reconstruction regardless of domain iteration order.
/// </summary>
public sealed class DomainStateFrame : IPersistableObject
{
    public uint Frame { get; set; }

    public List<DomainStateRecord> Records { get; } = new();

    public void Persist(StatePersister persister)
    {
        persister.PersistVersion(1);

        var frame = Frame;
        persister.PersistUInt32(ref frame, "Frame");
        Frame = frame;

        var recordCount = (uint)Records.Count;
        persister.PersistUInt32(ref recordCount, "RecordCount");

        if (persister.Mode == StatePersistMode.Read)
        {
            Records.Clear();
            for (var i = 0; i < recordCount; i++)
            {
                var record = new DomainStateRecord();
                persister.PersistObject(record, "Record");
                Records.Add(record);
            }
        }
        else
        {
            foreach (var record in Records)
            {
                persister.PersistObject(record, "Record");
            }
        }
    }
}
