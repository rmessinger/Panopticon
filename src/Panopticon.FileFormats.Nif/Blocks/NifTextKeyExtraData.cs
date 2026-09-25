using System.Collections.Generic;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiTextKeyExtraData at 10.1.0.0: a name (empty in the EE2 KF targets) and
/// (time, label) pairs such as "start" and "end".
/// </summary>
public sealed record NifTextKeyExtraData : NifBlock
{
    public string Name { get; }
    public IReadOnlyList<NifTextKey> Keys { get; }

    private NifTextKeyExtraData(NifBinaryReader reader)
    {
        Name = reader.ReadSizedString("Name");
        var count = reader.ReadElementCount(8, "TextKeys.Count");
        var keys = new NifTextKey[count];
        for (var i = 0; i < count; i++)
        {
            keys[i] = new NifTextKey(
                reader.ReadSingle($"TextKeys[{i}].Time"),
                reader.ReadSizedString($"TextKeys[{i}].Value"));
        }

        Keys = keys;
    }

    internal static NifTextKeyExtraData Read(NifBinaryReader reader)
    {
        return new NifTextKeyExtraData(reader);
    }
}

/// <summary>
/// One text key: a time and its label.
/// </summary>
public sealed record NifTextKey(float Time, string Value);
