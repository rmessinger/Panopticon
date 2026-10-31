namespace Panopticon.FileFormats.Bar;

public enum BarCompression
{
    /// <summary>Stored uncompressed; stored size equals decompressed size.</summary>
    None = 0,

    /// <summary>alz4 wrapper: 16-byte header followed by a raw LZ4 block.</summary>
    Alz4 = 1,

    /// <summary>Encrypted; not supported for reading.</summary>
    Encrypted = 2,
}
