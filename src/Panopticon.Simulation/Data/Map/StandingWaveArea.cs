using System;
using System.IO;
using System.Numerics;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Data.Utilities.Extensions;
using Panopticon.FileFormats;

namespace Panopticon.Data.Map;

[AddedIn(SageGame.Bfme2)]
public sealed class StandingWaveArea
{
    private Vector2[] _sourcePoints;
    private uint? _sourceFinalWidth;
    private uint? _sourceFinalHeight;
    private uint? _sourceDistanceFromShore;

    public uint UniqueID { get; private set; }
    public string Name { get; private set; }
    public string LayerName { get; private set; }

    // Unused?
    public float UvScrollSpeed { get; private set; }
    public bool UseAdditiveBlending { get; private set; }

    public Vector2[] Points { get; private set; }

    public float FinalWidth { get; private set; }
    public float FinalHeight { get; private set; }
    public uint InitialWidthFraction { get; private set; }
    public uint InitialHeightFraction { get; private set; }
    public uint InitialVelocity { get; private set; }
    public uint TimeToFade { get; private set; }
    public uint TimeToCompress { get; private set; }
    public uint TimeOffset2ndWave { get; private set; }
    public float DistanceFromShore { get; private set; }
    public string Texture { get; private set; }
    public bool EnablePcaWave { get; private set; }

    [AddedIn(SageGame.Ra3)]
    public string WaveParticleFXName { get; private set; }

    internal static StandingWaveArea Parse(BinaryReader reader, ushort version)
    {
        var result = new StandingWaveArea
        {
            UniqueID = reader.ReadUInt32(),
            Name = reader.ReadUInt16PrefixedAsciiString(),
            LayerName = reader.ReadUInt16PrefixedAsciiString(),
            UvScrollSpeed = reader.ReadSingle(),
            UseAdditiveBlending = reader.ReadBooleanChecked()
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

        var unknown = reader.ReadUInt32();
        if (unknown != 0)
        {
            throw new InvalidDataException();
        }

        if (version < 3)
        {
            var rawWidth = reader.ReadUInt32();
            result._sourceFinalWidth = rawWidth;
            result.FinalWidth = (float)UnitConversionPolicy.ScaleLength(rawWidth);

            var rawHeight = reader.ReadUInt32();
            result._sourceFinalHeight = rawHeight;
            result.FinalHeight = (float)UnitConversionPolicy.ScaleLength(rawHeight);

            result.InitialWidthFraction = reader.ReadUInt32();
            result.InitialHeightFraction = reader.ReadUInt32();
            result.InitialVelocity = reader.ReadUInt32();
            result.TimeToFade = reader.ReadUInt32();
            result.TimeToCompress = reader.ReadUInt32();
            result.TimeOffset2ndWave = reader.ReadUInt32();

            var rawDist = reader.ReadUInt32();
            result._sourceDistanceFromShore = rawDist;
            result.DistanceFromShore = (float)UnitConversionPolicy.ScaleLength(rawDist);
            result.Texture = reader.ReadUInt16PrefixedAsciiString();

            if (result.InitialVelocity != 0)
            {
                RetainedBackendDiagnostics.ReportUnclassified("StandingWaveArea.InitialVelocity");
            }

            if (version == 2)
            {
                result.EnablePcaWave = reader.ReadBooleanUInt32Checked();
            }
        }

        if (version >= 4)
        {
            result.WaveParticleFXName = reader.ReadUInt16PrefixedAsciiString();
        }

        return result;
    }

    internal void WriteTo(BinaryWriter writer, ushort version)
    {
        writer.Write(UniqueID);
        writer.WriteUInt16PrefixedAsciiString(Name);
        writer.WriteUInt16PrefixedAsciiString(LayerName);
        writer.Write(UvScrollSpeed);
        writer.Write(UseAdditiveBlending);

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

        if (version < 3)
        {
            writer.Write(_sourceFinalWidth ?? (uint)Math.Round(UnitConversionPolicy.UnscaleLength(FinalWidth)));
            writer.Write(_sourceFinalHeight ?? (uint)Math.Round(UnitConversionPolicy.UnscaleLength(FinalHeight)));
            writer.Write(InitialWidthFraction);
            writer.Write(InitialHeightFraction);
            writer.Write(InitialVelocity);
            writer.Write(TimeToFade);
            writer.Write(TimeToCompress);
            writer.Write(TimeOffset2ndWave);
            writer.Write(_sourceDistanceFromShore ?? (uint)Math.Round(UnitConversionPolicy.UnscaleLength(DistanceFromShore)));
            writer.WriteUInt16PrefixedAsciiString(Texture);

            if (version == 2)
            {
                writer.WriteBooleanUInt32(EnablePcaWave);
            }
        }

        if (version >= 4)
        {
            writer.WriteUInt16PrefixedAsciiString(WaveParticleFXName);
        }
    }
}
