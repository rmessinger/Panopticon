using System;
using System.IO;
using System.Numerics;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.FileFormats;

namespace Panopticon.Data.Map;

public abstract class CameraAnimationFrame
{
    public uint FrameIndex { get; private set; }
    public string InterpolationType { get; private set; }

    internal static TDerived Parse<TDerived>(
        BinaryReader reader,
        Action<TDerived> derivedParse)
        where TDerived : CameraAnimationFrame, new()
    {
        var result = new TDerived();

        result.FrameIndex = reader.ReadUInt32();

        result.InterpolationType = reader.ReadFourCc(bigEndian: true);
        if (result.InterpolationType != "catm" && result.InterpolationType != "line")
        {
            throw new InvalidDataException();
        }

        derivedParse(result);

        return result;
    }

    internal void WriteTo(BinaryWriter writer, Action derivedWriteTo)
    {
        writer.Write(FrameIndex);
        writer.WriteFourCc(InterpolationType, bigEndian: true);

        derivedWriteTo();
    }
}

public sealed class FreeCameraAnimationCameraFrame : CameraAnimationFrame
{
    private Vector3? _sourcePosition;
    public Vector3 Position { get; private set; }
    public Quaternion Rotation { get; private set; }
    public float FieldOfView { get; private set; }

    internal static FreeCameraAnimationCameraFrame Parse(BinaryReader reader)
    {
        return Parse<FreeCameraAnimationCameraFrame>(reader, x =>
        {
            var rawPosition = reader.ReadVector3();
            x._sourcePosition = rawPosition;
            x.Position = rawPosition * (float)UnitConversionPolicy.GeneralsFeetToMeters;
            x.Rotation = reader.ReadQuaternion();

            // TODO: Not sure if this is really the field of view, but it's inversely related to focal length.
            x.FieldOfView = reader.ReadSingle();
        });
    }

    internal void WriteTo(BinaryWriter writer)
    {
        WriteTo(writer, () =>
        {
            writer.Write(_sourcePosition ?? (Position / (float)UnitConversionPolicy.GeneralsFeetToMeters));
            writer.Write(Rotation);
            writer.Write(FieldOfView);
        });
    }
}

public sealed class LookAtCameraAnimationCameraFrame : CameraAnimationFrame
{
    private Vector3? _sourcePosition;
    public Vector3 Position { get; private set; }
    public float Roll { get; private set; }
    public float FieldOfView { get; private set; }

    internal static LookAtCameraAnimationCameraFrame Parse(BinaryReader reader)
    {
        return Parse<LookAtCameraAnimationCameraFrame>(reader, x =>
        {
            var rawPosition = reader.ReadVector3();
            x._sourcePosition = rawPosition;
            x.Position = rawPosition * (float)UnitConversionPolicy.GeneralsFeetToMeters;
            x.Roll = reader.ReadSingle();

            // TODO: Not sure if this is really the field of view, but it's inversely related to focal length.
            x.FieldOfView = reader.ReadSingle();
        });
    }

    internal void WriteTo(BinaryWriter writer)
    {
        WriteTo(writer, () =>
        {
            writer.Write(_sourcePosition ?? (Position / (float)UnitConversionPolicy.GeneralsFeetToMeters));
            writer.Write(Roll);
            writer.Write(FieldOfView);
        });
    }
}

public sealed class LookAtCameraAnimationLookAtFrame : CameraAnimationFrame
{
    private Vector3? _sourceLookAt;
    public Vector3 LookAt { get; private set; }

    internal static LookAtCameraAnimationLookAtFrame Parse(BinaryReader reader)
    {
        return Parse<LookAtCameraAnimationLookAtFrame>(reader, x =>
        {
            var rawLookAt = reader.ReadVector3();
            x._sourceLookAt = rawLookAt;
            x.LookAt = rawLookAt * (float)UnitConversionPolicy.GeneralsFeetToMeters;
        });
    }

    internal void WriteTo(BinaryWriter writer)
    {
        WriteTo(writer, () =>
        {
            writer.Write(_sourceLookAt ?? (LookAt / (float)UnitConversionPolicy.GeneralsFeetToMeters));
        });
    }
}
