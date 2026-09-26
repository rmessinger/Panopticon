namespace Panopticon.FileFormats.Casc;

public sealed class CascStorage : IDisposable
{
    private readonly CascIndex _index;
    private readonly CascEncoding _encoding;
    private readonly CascArchiveSet _archives;
    private bool _disposed;

    private CascStorage(CascIndex index, CascEncoding encoding, CascArchiveSet archives, MndxRoot root)
    {
        _index = index;
        _encoding = encoding;
        _archives = archives;
        Entries = root.Entries;
        RawEntryCount = root.RawEntryCount;
    }

    public IReadOnlyList<MndxRootEntry> Entries { get; }

    public int RawEntryCount { get; }

    public static CascStorage Open(string installRoot)
    {
        var buildInfo = CascBuildInfo.Read(installRoot);
        var dataDirectory = Path.Combine(installRoot, "SC2Data", "data");
        var index = CascIndex.Load(dataDirectory);
        var archives = new CascArchiveSet(dataDirectory);
        try
        {
            var encoding = CascEncoding.Parse(ReadByEncodingKey(archives, index, buildInfo.EncodingEncodingKey));
            var root = MndxRoot.Parse(ReadByContentKey(archives, index, encoding, buildInfo.RootContentKey));
            return new CascStorage(index, encoding, archives, root);
        }
        catch
        {
            archives.Dispose();
            throw;
        }
    }

    public byte[] OpenFile(CascKey contentKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_encoding.TryGetEntry(contentKey, out var encodingEntry))
        {
            throw new FileNotFoundException($"CASC content key '{contentKey}' is missing from the encoding file.");
        }

        var decoded = ReadByEncodingKey(_archives, _index, encodingEntry.EncodingKey);
        if (decoded.Length != encodingEntry.Size)
        {
            throw new InvalidDataException(
                $"CASC content key '{contentKey}' decoded to {decoded.Length} bytes, expected {encodingEntry.Size}.");
        }

        return decoded;
    }

    private static byte[] ReadByEncodingKey(CascArchiveSet archives, CascIndex index, CascKey encodingKey)
    {
        if (!index.TryGetSpan(encodingKey, out var span))
        {
            throw new FileNotFoundException($"CASC encoding key '{encodingKey}' is missing from the index.");
        }

        var raw = archives.ReadSpan(span);
        return BlteDecoder.Decode(new MemoryStream(raw));
    }

    private static byte[] ReadByContentKey(CascArchiveSet archives, CascIndex index, CascEncoding encoding, CascKey contentKey)
    {
        if (!encoding.TryGetEntry(contentKey, out var encodingEntry))
        {
            throw new FileNotFoundException($"CASC content key '{contentKey}' is missing from the encoding file.");
        }

        return ReadByEncodingKey(archives, index, encodingEntry.EncodingKey);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _archives.Dispose();
        _disposed = true;
    }
}
