using System.Collections.Generic;
using System.Numerics;

namespace Panopticon.FileFormats.Nif;

/// <summary>One NiVisData sample: a time plus a visibility byte (0 hidden, nonzero visible).</summary>
public readonly record struct NifVisKey(float Time, byte Visible);

/// <summary>
/// NiVisData at 10.1.0.0: a bare count plus (time, byte) pairs. Unlike key
/// groups there is no key-type word; every key is a step key by definition.
/// </summary>
public sealed record NifVisData : NifBlock
{
    public IReadOnlyList<NifVisKey> Keys { get; }

    private NifVisData(NifBinaryReader reader)
    {
        var count = reader.ReadElementCount(5, "Keys.Count");
        var keys = new NifVisKey[count];
        for (var i = 0; i < count; i++)
        {
            var time = reader.ReadSingle($"Keys[{i}].Time");
            var visible = reader.ReadByte($"Keys[{i}].Visible");
            keys[i] = new NifVisKey(time, visible);
        }

        Keys = keys;
    }

    internal static NifVisData Read(NifBinaryReader reader)
    {
        return new NifVisData(reader);
    }
}

/// <summary>
/// NiBoolData at 10.1.0.0: one byte key group. Accepted wherever NiVisData
/// is expected; linear and constant keys share the same wire shape.
/// </summary>
public sealed record NifBoolData : NifBlock
{
    public NifKeyGroup<NifKey<byte>>? Keys { get; }

    private NifBoolData(NifBinaryReader reader)
    {
        Keys = NifKeyGroupReader.ReadByteGroup(reader, "NiBoolData", "Keys");
    }

    internal static NifBoolData Read(NifBinaryReader reader)
    {
        return new NifBoolData(reader);
    }
}

/// <summary>
/// NiFloatData at 10.1.0.0: one float key group, shared by alpha and
/// texture-transform controllers.
/// </summary>
public sealed record NifFloatData : NifBlock
{
    public NifKeyGroup<NifKey<float>>? Keys { get; }

    private NifFloatData(NifBinaryReader reader)
    {
        Keys = NifKeyGroupReader.ReadFloatGroup(reader, "NiFloatData", "Keys");
    }

    internal static NifFloatData Read(NifBinaryReader reader)
    {
        return new NifFloatData(reader);
    }
}

/// <summary>
/// NiPosData at 10.1.0.0: one Vector3 key group of RGB triples, used by
/// material-color controllers. Values are preserved without clamping.
/// </summary>
public sealed record NifPosData : NifBlock
{
    public NifKeyGroup<NifKey<Vector3>>? Keys { get; }

    private NifPosData(NifBinaryReader reader)
    {
        Keys = NifKeyGroupReader.ReadVector3Group(reader, "NiPosData", "Keys");
    }

    internal static NifPosData Read(NifBinaryReader reader)
    {
        return new NifPosData(reader);
    }
}

/// <summary>
/// NiUVData at 10.1.0.0: four float key groups (U translate, V translate,
/// U scale, V scale) consumed by NiUVController. Absent groups are null.
/// </summary>
public sealed record NifUVData : NifBlock
{
    public NifKeyGroup<NifKey<float>>? UTranslationKeys { get; }
    public NifKeyGroup<NifKey<float>>? VTranslationKeys { get; }
    public NifKeyGroup<NifKey<float>>? UScaleKeys { get; }
    public NifKeyGroup<NifKey<float>>? VScaleKeys { get; }

    private NifUVData(NifBinaryReader reader)
    {
        UTranslationKeys = NifKeyGroupReader.ReadFloatGroup(reader, "NiUVData", "UTranslationKeys");
        VTranslationKeys = NifKeyGroupReader.ReadFloatGroup(reader, "NiUVData", "VTranslationKeys");
        UScaleKeys = NifKeyGroupReader.ReadFloatGroup(reader, "NiUVData", "UScaleKeys");
        VScaleKeys = NifKeyGroupReader.ReadFloatGroup(reader, "NiUVData", "VScaleKeys");
    }

    internal static NifUVData Read(NifBinaryReader reader)
    {
        return new NifUVData(reader);
    }
}
