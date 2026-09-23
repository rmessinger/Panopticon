using System;
using System.IO;
using System.Numerics;

namespace OpenSage.FileFormats.Nif;

/// <summary>
/// NiKeyframeData at 10.1.0.0: rotation keys (linear quaternion, or XYZ
/// Euler as three float key groups under a rotation order word), a Vector3
/// translation group, and a float scale group. Rotation is null when the
/// rotation key count is zero.
/// </summary>
public sealed record NifKeyframeData : NifBlock
{
    private const string BlockName = "NiKeyframeData";

    public NifKeyGroup<NifQuaternionKey>? QuaternionRotationKeys { get; }
    public float? XyzRotationOrder { get; }
    public NifKeyGroup<NifKey<float>>? XRotationKeys { get; }
    public NifKeyGroup<NifKey<float>>? YRotationKeys { get; }
    public NifKeyGroup<NifKey<float>>? ZRotationKeys { get; }
    public NifKeyGroup<NifKey<Vector3>>? TranslationKeys { get; }
    public NifKeyGroup<NifKey<float>>? ScaleKeys { get; }

    private NifKeyframeData(NifBinaryReader reader)
    {
        // 20 bytes is the smallest possible rotation key (time plus four
        // floats); the XYZ layout always leaves at least that much behind.
        var rotationCount = reader.ReadElementCount(20, "RotationKeys.Count");
        if (rotationCount != 0)
        {
            var rotationType = NifKeyGroupReader.ReadKeyType(reader, "RotationKeys");
            switch (rotationType)
            {
                case NifKeyType.Linear:
                    QuaternionRotationKeys = NifKeyGroupReader.ReadLinearQuaternionKeys(
                        reader, rotationCount, BlockName, "RotationKeys");
                    break;

                case NifKeyType.XyzRotation:
                    if (rotationCount != 1)
                    {
                        throw new InvalidDataException(
                            $"NIF NiKeyframeData XYZ rotation declares key count {rotationCount}; expected 1.");
                    }

                    XyzRotationOrder = reader.ReadSingle("RotationKeys.XyzOrder");
                    XRotationKeys = NifKeyGroupReader.ReadFloatGroup(reader, BlockName, "RotationKeys.X");
                    YRotationKeys = NifKeyGroupReader.ReadFloatGroup(reader, BlockName, "RotationKeys.Y");
                    ZRotationKeys = NifKeyGroupReader.ReadFloatGroup(reader, BlockName, "RotationKeys.Z");
                    break;

                default:
                    throw new NotSupportedException(
                        $"NIF NiKeyframeData rotation keys use unsupported key type " +
                        $"{rotationType} ({(uint)rotationType}); C4c supports Linear (1) quaternion " +
                        $"and XyzRotation (4) keys.");
            }
        }

        TranslationKeys = NifKeyGroupReader.ReadVector3Group(reader, BlockName, "TranslationKeys");
        ScaleKeys = NifKeyGroupReader.ReadFloatGroup(reader, BlockName, "ScaleKeys");
    }

    internal static NifKeyframeData Read(NifBinaryReader reader)
    {
        return new NifKeyframeData(reader);
    }
}
