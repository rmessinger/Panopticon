using System.IO;
using System.Numerics;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.FileFormats;

namespace Panopticon.Data.Map;

public sealed class NamedCamera
{
    private Vector3? _sourceLookAtPoint;
    public Vector3 LookAtPoint { get; private set; }
    public string Name { get; private set; }

    public float Pitch { get; private set; }
    public float Roll { get; private set; }
    public float Yaw { get; private set; }
    public float Zoom { get; private set; }
    public float FieldOfView { get; private set; }
    public float Unknown { get; private set; }

    internal static NamedCamera Parse(BinaryReader reader)
    {
        var rawLookAt = reader.ReadVector3();
        var result = new NamedCamera
        {
            _sourceLookAtPoint = rawLookAt,
            LookAtPoint = rawLookAt * (float)UnitConversionPolicy.GeneralsFeetToMeters,
            Name = reader.ReadUInt16PrefixedAsciiString(),
            Pitch = reader.ReadSingle(),
            Roll = reader.ReadSingle(),
            Yaw = reader.ReadSingle(),
            Zoom = reader.ReadSingle(),
            FieldOfView = reader.ReadSingle(),
            Unknown = reader.ReadSingle()
        };

        if (result.Zoom != 0f)
        {
            RetainedBackendDiagnostics.ReportUnclassified("NamedCamera.Zoom");
        }

        if (result.Unknown != 0f)
        {
            RetainedBackendDiagnostics.ReportUnclassified("NamedCamera.Unknown");
        }

        return result;
    }

    internal void WriteTo(BinaryWriter writer)
    {
        writer.Write(_sourceLookAtPoint ?? (LookAtPoint / (float)UnitConversionPolicy.GeneralsFeetToMeters));
        writer.WriteUInt16PrefixedAsciiString(Name);
        writer.Write(Pitch);
        writer.Write(Roll);
        writer.Write(Yaw);
        writer.Write(Zoom);
        writer.Write(FieldOfView);
        writer.Write(Unknown);
    }
}
