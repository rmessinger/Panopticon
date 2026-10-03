using System;
using System.IO;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// Base for blocks that carry the NiObjectNET fields at 10.1.0.0:
/// name, extra-data reference list, controller reference. EE2 cooked
/// files encode an absent extra-data list as a 0xFFFFFFFF count, which
/// also omits the controller reference entirely.
/// </summary>
public abstract record NifObjectNet : NifBlock
{
    private const uint NoExtraDataList = 0xFFFFFFFF;

    public string Name { get; }
    public int[] ExtraDataReferences { get; }
    public int ControllerReference { get; }

    protected NifObjectNet(NifBinaryReader reader)
    {
        Name = reader.ReadSizedString("Name");
        var extraDataCount = reader.ReadUInt32("ExtraDataList.Count");
        if (extraDataCount == NoExtraDataList)
        {
            ExtraDataReferences = Array.Empty<int>();
            ControllerReference = -1;
            return;
        }

        if (extraDataCount > (long)reader.RemainingBytes / 4)
        {
            throw new InvalidDataException(
                $"NIF field 'ExtraDataList.Count': count {extraDataCount} exceeds " +
                $"remaining {reader.RemainingBytes} bytes");
        }

        var extraData = new int[extraDataCount];
        for (var i = 0; i < extraDataCount; i++)
        {
            extraData[i] = reader.ReadBlockReference($"ExtraDataList[{i}]");
        }

        ExtraDataReferences = extraData;
        ControllerReference = reader.ReadBlockReference("Controller");
    }
}
