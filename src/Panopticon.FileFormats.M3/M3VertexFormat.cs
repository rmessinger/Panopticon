using System;

namespace Panopticon.FileFormats.M3;

public sealed record M3VertexFormat(uint Flags, int Stride, bool HasUv1)
{
    public static M3VertexFormat FromFlags(uint flags)
    {
        return flags switch
        {
            0x0186007D => new M3VertexFormat(flags, 36, true),
            0x0182007D => new M3VertexFormat(flags, 32, false),
            _ => throw new NotSupportedException($"Unsupported M3 vertex flags 0x{flags:X8}."),
        };
    }
}
