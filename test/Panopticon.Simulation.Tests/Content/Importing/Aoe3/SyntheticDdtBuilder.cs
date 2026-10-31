using System;
using System.IO;
using Panopticon.FileFormats.Bar;

namespace Panopticon.Tests.Content.Importing.Aoe3;

/// <summary>
/// Builds synthetic DDT images for install-free tests: 'RTS3' header, a mip
/// offset/size table, then contiguous mip payloads.
/// </summary>
internal static class SyntheticDdtBuilder
{
    internal static byte[] Build(DdtFormat format, uint width, uint height, params byte[][] mipPayloads)
    {
        var mipTableSize = 8 * mipPayloads.Length;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.Unicode, leaveOpen: true);

        writer.Write(System.Text.Encoding.ASCII.GetBytes("RTS3"));
        writer.Write((byte)0); // usage
        writer.Write((byte)0); // alpha bits
        writer.Write((byte)format);
        writer.Write((byte)mipPayloads.Length);
        writer.Write(width);
        writer.Write(height);

        var offset = 16 + mipTableSize;
        foreach (var payload in mipPayloads)
        {
            writer.Write((uint)offset);
            writer.Write((uint)payload.Length);
            offset += payload.Length;
        }

        foreach (var payload in mipPayloads)
        {
            writer.Write(payload);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
