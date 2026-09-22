using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenSage.IO;
using OpenSage.Logic.Orders;

namespace OpenSage.Data.Rep;

public sealed class ReplayFile
{
    public ReplayHeader Header { get; private set; }
    public IReadOnlyList<ReplayChunk> Chunks { get; private set; }

    public static ReplayFile FromFileSystemEntry(FileSystemEntry entry, bool onlyHeader = false)
    {
        using (var stream = entry.Open())
        using (var reader = new BinaryReader(stream, Encoding.Unicode, true))
        {
            var result = new ReplayFile
            {
                Header = ReplayHeader.Parse(reader)
            };

            if (onlyHeader)
            {
                return result;
            }

            var chunks = new List<ReplayChunk>();
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                chunks.Add(ReplayChunk.Parse(reader));
            }

            result.Chunks = chunks;

            if (result.Header.NumTimecodes != chunks[chunks.Count - 1].Header.Timecode)
            {
                throw new InvalidDataException();
            }

            return result;
        }
    }

    /// <summary>
    /// Writes a replay to <paramref name="outputStream"/> using the provided <paramref name="header"/>
    /// and a flat sequence of (frame, order) pairs ordered by frame ascending.
    /// </summary>
    public static void Write(Stream outputStream, ReplayHeader header, IEnumerable<(uint frame, Order order)> frameOrders)
    {
        using var writer = new BinaryWriter(outputStream, Encoding.Unicode, leaveOpen: true);

        var numTimecodesPosition = header.Write(writer);

        ushort lastTimecode = 0;
        foreach (var (frame, order) in frameOrders)
        {
            ReplayChunk.Write(writer, frame, order);
            if (frame > lastTimecode)
            {
                lastTimecode = (ushort)Math.Min(frame, ushort.MaxValue);
            }
        }

        ReplayHeader.PatchNumTimecodes(writer, numTimecodesPosition, lastTimecode);
    }
}
