namespace Panopticon.FileFormats.Casc;

internal static class MarReader
{
    public static byte[] ReadVector(BinaryReader reader)
    {
        var byteSize = checked((int)reader.ReadUInt64());
        var data = reader.ReadBytes(byteSize);
        if (data.Length != byteSize)
        {
            throw new EndOfStreamException("MAR vector is truncated.");
        }

        var padding = (-byteSize) & 7;
        if (padding != 0)
        {
            var skipped = reader.ReadBytes(padding);
            if (skipped.Length != padding)
            {
                throw new EndOfStreamException("MAR vector padding is truncated.");
            }
        }

        return data;
    }
}
