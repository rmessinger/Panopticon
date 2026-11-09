using System;
using System.IO;
using System.Numerics;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Data.Utilities.Extensions;
using Panopticon.FileFormats;
using Panopticon.Mathematics;

namespace Panopticon.Data.Map;

public sealed class RiverArea
{
    private Line2D[] _sourceLines;
    private uint? _sourceWaterHeight;

    public uint UniqueID { get; private set; }
    public string Name { get; private set; }
    public string LayerName { get; private set; }

    public float UvScrollSpeed { get; private set; }
    public bool UseAdditiveBlending { get; private set; }

    public string RiverTexture { get; private set; }
    public string NoiseTexture { get; private set; }
    public string AlphaEdgeTexture { get; private set; }
    public string SparkleTexture { get; private set; }
    public ColorRgb Color { get; private set; }
    public float Alpha { get; private set; }
    public float WaterHeight { get; private set; }

    [AddedIn(SageGame.Cnc3)]
    public string RiverType { get; private set; }

    public string MinimumWaterLod { get; private set; }

    public Line2D[] Lines { get; private set; }

    internal static RiverArea Parse(BinaryReader reader, ushort version)
    {
        var result = new RiverArea
        {
            UniqueID = reader.ReadUInt32(),
            Name = reader.ReadUInt16PrefixedAsciiString(),
            LayerName = reader.ReadUInt16PrefixedAsciiString(),
            UvScrollSpeed = reader.ReadSingle(),
            UseAdditiveBlending = reader.ReadBooleanChecked(),
            RiverTexture = reader.ReadUInt16PrefixedAsciiString(),
            NoiseTexture = reader.ReadUInt16PrefixedAsciiString(),
            AlphaEdgeTexture = reader.ReadUInt16PrefixedAsciiString(),
            SparkleTexture = reader.ReadUInt16PrefixedAsciiString(),
            Color = reader.ReadColorRgb()
        };

        var unusedColorA = reader.ReadByte();
        if (unusedColorA != 0)
        {
            throw new InvalidDataException();
        }

        result.Alpha = reader.ReadSingle();
        var rawHeight = reader.ReadUInt32();
        result._sourceWaterHeight = rawHeight;
        result.WaterHeight = (float)UnitConversionPolicy.ScaleLength(rawHeight);

        if (version >= 3)
        {
            result.RiverType = reader.ReadUInt16PrefixedAsciiString();
        }

        result.MinimumWaterLod = reader.ReadUInt16PrefixedAsciiString();

        var numLines = reader.ReadUInt32();
        var rawLines = new Line2D[numLines];
        result.Lines = new Line2D[numLines];

        for (var i = 0; i < numLines; i++)
        {
            var raw = reader.ReadLine2D();
            rawLines[i] = raw;
            result.Lines[i] = new Line2D(
                raw.V0 * (float)UnitConversionPolicy.GeneralsFeetToMeters,
                raw.V1 * (float)UnitConversionPolicy.GeneralsFeetToMeters);
        }

        result._sourceLines = rawLines;

        return result;
    }

    internal void WriteTo(BinaryWriter writer, ushort version)
    {
        writer.Write(UniqueID);
        writer.WriteUInt16PrefixedAsciiString(Name);
        writer.WriteUInt16PrefixedAsciiString(LayerName);
        writer.Write(UvScrollSpeed);
        writer.Write(UseAdditiveBlending);
        writer.WriteUInt16PrefixedAsciiString(RiverTexture);
        writer.WriteUInt16PrefixedAsciiString(NoiseTexture);
        writer.WriteUInt16PrefixedAsciiString(AlphaEdgeTexture);
        writer.WriteUInt16PrefixedAsciiString(SparkleTexture);
        writer.Write(Color);
        writer.Write((byte)0);
        writer.Write(Alpha);
        writer.Write(_sourceWaterHeight ?? (uint)Math.Round(UnitConversionPolicy.UnscaleLength(WaterHeight)));

        if (version >= 3)
        {
            writer.WriteUInt16PrefixedAsciiString(RiverType);
        }

        writer.WriteUInt16PrefixedAsciiString(MinimumWaterLod);

        writer.Write((uint)Lines.Length);
        if (_sourceLines != null && _sourceLines.Length == Lines.Length)
        {
            foreach (var line in _sourceLines)
            {
                writer.Write(line);
            }
        }
        else
        {
            foreach (var line in Lines)
            {
                writer.Write(new Line2D(
                    line.V0 / (float)UnitConversionPolicy.GeneralsFeetToMeters,
                    line.V1 / (float)UnitConversionPolicy.GeneralsFeetToMeters));
            }
        }
    }
}
