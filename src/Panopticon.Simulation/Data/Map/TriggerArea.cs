using System.IO;
using System.Numerics;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.FileFormats;

namespace Panopticon.Data.Map;

public sealed class TriggerArea
{
    private Vector2[] _sourcePoints;
    public string Name { get; private set; }
    public string LayerName { get; private set; }
    public uint ID { get; private set; }
    public Vector2[] Points { get; private set; }

    internal static TriggerArea Parse(BinaryReader reader)
    {
        var name = reader.ReadUInt16PrefixedAsciiString();

        var layerName = reader.ReadUInt16PrefixedAsciiString();

        var id = reader.ReadUInt32();

        var numPoints = reader.ReadUInt32();
        var rawPoints = new Vector2[numPoints];
        var points = new Vector2[numPoints];

        for (var i = 0; i < numPoints; i++)
        {
            var raw = reader.ReadVector2();
            rawPoints[i] = raw;
            points[i] = raw * (float)UnitConversionPolicy.GeneralsFeetToMeters;
        }

        var unknown2 = reader.ReadUInt32();
        if (unknown2 != 0)
        {
            throw new InvalidDataException();
        }

        return new TriggerArea
        {
            Name = name,
            LayerName = layerName,
            ID = id,
            _sourcePoints = rawPoints,
            Points = points
        };
    }

    internal void WriteTo(BinaryWriter writer)
    {
        writer.WriteUInt16PrefixedAsciiString(Name);

        writer.WriteUInt16PrefixedAsciiString(LayerName);

        writer.Write(ID);

        writer.Write((uint)Points.Length);
        if (_sourcePoints != null && _sourcePoints.Length == Points.Length)
        {
            foreach (var point in _sourcePoints)
            {
                writer.Write(point);
            }
        }
        else
        {
            foreach (var point in Points)
            {
                writer.Write(point / (float)UnitConversionPolicy.GeneralsFeetToMeters);
            }
        }

        writer.Write((uint)0);
    }
}
