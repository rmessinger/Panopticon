using System;
using System.IO;
using System.Text;
using Panopticon.Data.Utilities.Extensions;
using Panopticon.FileFormats;

namespace Panopticon.Data.Rep;

public sealed class ReplayHeader
{
    public ReplayGameType GameType { get; private set; }

    public DateTime StartTime { get; private set; }
    public DateTime EndTime { get; private set; }

    public ushort NumTimecodes { get; internal set; }

    public string Filename { get; private set; }

    public ReplayTimestamp Timestamp { get; private set; }

    public string Version { get; private set; }
    public string BuildDate { get; private set; }

    public ushort VersionMinor { get; private set; }
    public ushort VersionMajor { get; private set; }

    // Maybe a hash of... something.
    public byte[] UnknownHash { get; private set; }

    public ReplayMetadata Metadata { get; private set; }

    public ushort Unknown1 { get; private set; }
    public uint Unknown2 { get; private set; }
    public uint Unknown3 { get; private set; }
    public uint Unknown4 { get; private set; }

    public uint GameSpeed { get; private set; }

    /// <summary>
    /// Creates a minimal header suitable for recording a new Generals skirmish replay.
    /// </summary>
    public static ReplayHeader Create(ReplayMetadata metadata, string version = "1.8", string buildDate = "") =>
        new ReplayHeader
        {
            GameType = ReplayGameType.Generals,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow, // patched on save
            NumTimecodes = 0,          // patched on save
            Filename = string.Empty,
            Timestamp = ReplayTimestamp.FromDateTime(DateTime.Now),
            Version = version,
            BuildDate = buildDate,
            VersionMinor = 0,
            VersionMajor = 0,
            UnknownHash = new byte[8],
            Metadata = metadata,
            Unknown1 = 0,
            Unknown2 = 0,
            Unknown3 = 0,
            Unknown4 = 0,
            GameSpeed = 30
        };

    public static ReplayHeader Parse(BinaryReader reader)
    {
        var gameType = ParseGameType(reader.BaseStream);

        var result = new ReplayHeader
        {
            GameType = gameType
        };

        result.StartTime = ReadTimestamp(reader);
        result.EndTime = ReadTimestamp(reader);

        if (gameType == ReplayGameType.Generals)
        {
            result.NumTimecodes = reader.ReadUInt16();

            var zero = reader.ReadBytes(12);
            // TODO
            //for (var i = 0; i < zero.Length; i++)
            //{
            //    if (zero[i] != 0)
            //    {
            //        throw new InvalidDataException();
            //    }
            //}
        }
        else
        {
            throw new NotImplementedException();
        }

        result.Filename = reader.ReadNullTerminatedString();

        result.Timestamp = ReplayTimestamp.Parse(reader);

        result.Version = reader.ReadNullTerminatedString();
        result.BuildDate = reader.ReadNullTerminatedString();

        result.VersionMinor = reader.ReadUInt16();
        result.VersionMajor = reader.ReadUInt16();

        result.UnknownHash = reader.ReadBytes(8);

        result.Metadata = ReplayMetadata.Parse(reader);

        result.Unknown1 = reader.ReadUInt16();

        result.Unknown2 = reader.ReadUInt32();
        result.Unknown3 = reader.ReadUInt32();
        result.Unknown4 = reader.ReadUInt32();

        result.GameSpeed = reader.ReadUInt32();

        return result;
    }

    /// <summary>
    /// Writes the header to <paramref name="writer"/>.
    /// Returns the stream position of the <c>NumTimecodes</c> field so it can be
    /// patched via <see cref="PatchNumTimecodes"/> once all chunks are written.
    /// </summary>
    internal long Write(BinaryWriter writer)
    {
        // Magic (6 ASCII bytes, no null terminator)
        var asciiBytes = Encoding.ASCII.GetBytes("GENREP");
        writer.Write(asciiBytes);

        WriteUnixTimestamp(writer, StartTime);
        WriteUnixTimestamp(writer, EndTime);

        // Record position so we can patch NumTimecodes afterwards
        var numTimecodesPosition = writer.BaseStream.Position;
        writer.Write(NumTimecodes);       // uint16 placeholder
        writer.Write(new byte[12]);       // 12 zero bytes

        writer.WriteNullTerminatedUnicodeString(Filename);
        Timestamp.Write(writer);
        writer.WriteNullTerminatedUnicodeString(Version);
        writer.WriteNullTerminatedUnicodeString(BuildDate);
        writer.Write(VersionMinor);
        writer.Write(VersionMajor);
        writer.Write(UnknownHash);        // 8 bytes

        Metadata.Write(writer);

        writer.Write(Unknown1);
        writer.Write(Unknown2);
        writer.Write(Unknown3);
        writer.Write(Unknown4);
        writer.Write(GameSpeed);

        return numTimecodesPosition;
    }

    /// <summary>
    /// Seeks back to the <c>NumTimecodes</c> field and writes the final value.
    /// </summary>
    internal static void PatchNumTimecodes(BinaryWriter writer, long numTimecodesPosition, ushort value)
    {
        var savedPosition = writer.BaseStream.Position;
        writer.BaseStream.Seek(numTimecodesPosition, SeekOrigin.Begin);
        writer.Write(value);
        writer.BaseStream.Seek(savedPosition, SeekOrigin.Begin);
    }

    private static ReplayGameType ParseGameType(Stream stream)
    {
        using (var asciiReader = new BinaryReader(stream, Encoding.ASCII, true))
        {
            var gameTypeHeader = asciiReader.ReadFixedLengthString(6);
            if (gameTypeHeader == "GENREP")
            {
                return ReplayGameType.Generals;
            }
            else
            {
                stream.Seek(0, SeekOrigin.Begin);

                gameTypeHeader = asciiReader.ReadFixedLengthString(8);
                switch (gameTypeHeader)
                {
                    case "BFMEREPL":
                        return ReplayGameType.Bfme;

                    case "BFME2RPL":
                        return ReplayGameType.Bfme2;

                    default:
                        throw new NotImplementedException("Replay type not yet implemented: " + gameTypeHeader);
                }
            }
        }
    }

    private static DateTime ReadTimestamp(BinaryReader reader)
    {
        var origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        return origin.AddSeconds(reader.ReadUInt32());
    }

    private static void WriteUnixTimestamp(BinaryWriter writer, DateTime dt)
    {
        var origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        var seconds = (uint)(dt.ToUniversalTime() - origin).TotalSeconds;
        writer.Write(seconds);
    }
}
