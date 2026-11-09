using System;

namespace Panopticon.Data.Sav;

public sealed class GameState : IPersistableObject
{
    public SaveGameType GameType;
    public string MapPath;
    public DateTime Timestamp;
    public string DisplayName;
    public string MapFileName;
    public string Side;
    public uint MissionIndex;

    public void Persist(StatePersister reader)
    {
        var version = reader.PersistVersion(3);
        if (version < 3)
        {
            reader.UnitContext = StreamUnitContext.LegacyFeet;
        }

        reader.PersistEnum(ref GameType);
        reader.PersistAsciiString(ref MapPath);
        reader.PersistDateTime(ref Timestamp);
        reader.PersistUnicodeString(ref DisplayName);
        reader.PersistAsciiString(ref MapFileName);
        reader.PersistAsciiString(ref Side);
        reader.PersistUInt32(ref MissionIndex);
    }
}
