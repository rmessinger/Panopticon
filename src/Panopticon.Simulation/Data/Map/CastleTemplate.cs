using System.IO;
using System.Numerics;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.FileFormats;

namespace Panopticon.Data.Map;

[AddedIn(SageGame.Bfme)]
public sealed class CastleTemplate
{
    private Vector3? _sourceOffset;
    public string Name { get; private set; }
    public string TemplateName { get; private set; }
    public Vector3 Offset { get; private set; }
    public float Angle { get; private set; }

    public uint Priority { get; private set; }
    public uint Phase { get; private set; }

    internal static CastleTemplate Parse(BinaryReader reader, ushort version)
    {
        var name = reader.ReadUInt16PrefixedAsciiString();
        var templateName = reader.ReadUInt16PrefixedAsciiString();
        var rawOffset = reader.ReadVector3();
        var result = new CastleTemplate
        {
            _sourceOffset = rawOffset,
            Name = name,
            TemplateName = templateName,
            Offset = rawOffset * (float)UnitConversionPolicy.GeneralsFeetToMeters,
            Angle = reader.ReadSingle()
        };

        if (version >= 4)
        {
            result.Priority = reader.ReadUInt32();
            result.Phase = reader.ReadUInt32();
        }

        return result;
    }

    internal void WriteTo(BinaryWriter writer, ushort version)
    {
        writer.WriteUInt16PrefixedAsciiString(Name);
        writer.WriteUInt16PrefixedAsciiString(TemplateName);
        writer.Write(_sourceOffset ?? (Offset / (float)UnitConversionPolicy.GeneralsFeetToMeters));
        writer.Write(Angle);

        if (version >= 4)
        {
            writer.Write(Priority);
            writer.Write(Phase);
        }
    }
}
