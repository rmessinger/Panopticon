using System.IO;
using Panopticon.FileFormats;
using Panopticon.Logic.Orders;

namespace Panopticon.Data.Rep;

public sealed class ReplayChunkHeader
{
    public uint Timecode { get; private set; }
    public OrderType OrderType { get; private set; }
    public uint Number { get; private set; }

    internal static ReplayChunkHeader Parse(BinaryReader reader)
    {
        return new ReplayChunkHeader
        {
            Timecode = reader.ReadUInt32(),
            OrderType = reader.ReadUInt32AsEnum<OrderType>(),
            Number = reader.ReadUInt32()
        };
    }
}
