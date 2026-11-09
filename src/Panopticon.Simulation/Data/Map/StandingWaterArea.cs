using System;
using System.IO;
using System.Numerics;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.FileFormats;

namespace Panopticon.Data.Map;

public sealed class StandingWaterArea
{
    private Vector2[] _sourcePoints;
    private uint? _sourceWaterHeight;

    public uint UniqueID { get; private set; }
    public string Name { get; private set; }
    public string LayerName { get; private set; }

    public float UvScrollSpeed { get; private set; }
    public bool UseAdditiveBlending { get; private set; }

    public string BumpMapTexture { get; private set; }
    public string SkyTexture { get; private set; }

    public Vector2[] Points { get; private set; }

    public float WaterHeight { get; private set; }

    public string FxShader { get; private set; }
    public string DepthColors { get; private set; }

    internal static StandingWaterArea Parse(BinaryReader reader)
    {
        var result = new StandingWaterArea
        {
            UniqueID = reader.ReadUInt32(),
            Name = reader.ReadUInt16PrefixedAsciiString(),
            LayerName = reader.ReadUInt16PrefixedAsciiString(),
            UvScrollSpeed = reader.ReadSingle(),
            UseAdditiveBlending = reader.ReadBooleanChecked(),
            BumpMapTexture = reader.ReadUInt16PrefixedAsciiString(),
            SkyTexture = reader.ReadUInt16PrefixedAsciiString(),
        };

        var numPoints = reader.ReadUInt32();
        var rawPoints = new Vector2[numPoints];
        result.Points = new Vector2[numPoints];

        for (var i = 0; i < numPoints; i++)
        {
            var raw = reader.ReadVector2();
            rawPoints[i] = raw;
            result.Points[i] = raw * (float)UnitConversionPolicy.GeneralsFeetToMeters;
        }

        result._sourcePoints = rawPoints;

        var rawHeight = reader.ReadUInt32();
        result._sourceWaterHeight = rawHeight;
        result.WaterHeight = (float)UnitConversionPolicy.ScaleLength(rawHeight);
        result.FxShader = reader.ReadUInt16PrefixedAsciiString();
        result.DepthColors = reader.ReadUInt16PrefixedAsciiString();

        return result;
    }

    internal void WriteTo(BinaryWriter writer)
    {
        writer.Write(UniqueID);
        writer.WriteUInt16PrefixedAsciiString(Name);
        writer.WriteUInt16PrefixedAsciiString(LayerName);
        writer.Write(UvScrollSpeed);
        writer.Write(UseAdditiveBlending);
        writer.WriteUInt16PrefixedAsciiString(BumpMapTexture);
        writer.WriteUInt16PrefixedAsciiString(SkyTexture);

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

        writer.Write(_sourceWaterHeight ?? (uint)Math.Round(UnitConversionPolicy.UnscaleLength(WaterHeight)));
        writer.WriteUInt16PrefixedAsciiString(FxShader);
        writer.WriteUInt16PrefixedAsciiString(DepthColors);
    }
}
