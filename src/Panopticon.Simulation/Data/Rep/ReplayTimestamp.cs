using System;
using System.IO;
using Panopticon.Data.Utilities.Extensions;
using Panopticon.FileFormats;

namespace Panopticon.Data.Rep;

public sealed class ReplayTimestamp
{
    public ushort Year { get; private set; }
    public ushort Month { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public ushort Day { get; private set; }

    public ushort Hour { get; private set; }
    public ushort Minute { get; private set; }
    public ushort Second { get; private set; }
    public ushort Millisecond { get; private set; }

    internal static ReplayTimestamp Parse(BinaryReader reader)
    {
        return new ReplayTimestamp
        {
            Year = reader.ReadUInt16(),
            Month = reader.ReadUInt16(),
            DayOfWeek = reader.ReadUInt16AsEnum<DayOfWeek>(),
            Day = reader.ReadUInt16(),

            Hour = reader.ReadUInt16(),
            Minute = reader.ReadUInt16(),
            Second = reader.ReadUInt16(),
            Millisecond = reader.ReadUInt16()
        };
    }

    internal static ReplayTimestamp FromDateTime(DateTime dt) => new ReplayTimestamp
    {
        Year = (ushort)dt.Year,
        Month = (ushort)dt.Month,
        DayOfWeek = dt.DayOfWeek,
        Day = (ushort)dt.Day,
        Hour = (ushort)dt.Hour,
        Minute = (ushort)dt.Minute,
        Second = (ushort)dt.Second,
        Millisecond = (ushort)dt.Millisecond
    };

    internal void Write(BinaryWriter writer)
    {
        writer.Write(Year);
        writer.Write(Month);
        writer.Write((ushort)DayOfWeek);
        writer.Write(Day);
        writer.Write(Hour);
        writer.Write(Minute);
        writer.Write(Second);
        writer.Write(Millisecond);
    }
}
