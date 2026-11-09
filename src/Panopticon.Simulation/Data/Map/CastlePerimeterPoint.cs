using System.IO;
using System.Numerics;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.FileFormats;

namespace Panopticon.Data.Map;

public sealed class CastlePerimeterPoint
{
    private Vector3? _sourcePosition;
    public Vector3 Position { get; private set; }

    internal static CastlePerimeterPoint Parse(BinaryReader reader, ushort version)
    {
        float x, y, z;
        if (version >= 3)
        {
            x = reader.ReadSingle();
            y = reader.ReadSingle();
            z = 0.0f;
        }
        else
        {
            x = reader.ReadInt32();
            y = reader.ReadInt32();
            z = reader.ReadInt32();
        }

        var source = new Vector3(x, y, z);
        return new CastlePerimeterPoint
        {
            _sourcePosition = source,
            Position = source * (float)UnitConversionPolicy.GeneralsFeetToMeters
        };
    }

    internal void WriteTo(BinaryWriter writer, ushort version)
    {
        var source = _sourcePosition ?? (Position / (float)UnitConversionPolicy.GeneralsFeetToMeters);
        if (version >= 3)
        {
            writer.Write(source.X);
            writer.Write(source.Y);
        }
        else
        {
            writer.Write((int)source.X);
            writer.Write((int)source.Y);
            writer.Write((int)source.Z);
        }
    }
}
