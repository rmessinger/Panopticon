using System.IO;

namespace Panopticon.Data.Map;

public sealed class WaypointPath
{
    public int StartWaypointID { get; private set; }
    public int EndWaypointID { get; private set; }

    internal static WaypointPath Parse(BinaryReader reader)
    {
        return new WaypointPath
        {
            StartWaypointID = reader.ReadInt32(),
            EndWaypointID = reader.ReadInt32()
        };
    }

    internal void WriteTo(BinaryWriter writer)
    {
        writer.Write(StartWaypointID);
        writer.Write(EndWaypointID);
    }
}
