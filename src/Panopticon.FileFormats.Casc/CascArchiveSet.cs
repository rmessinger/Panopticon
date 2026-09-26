namespace Panopticon.FileFormats.Casc;

public sealed class CascArchiveSet : IDisposable
{
    private readonly string _dataDirectory;
    private readonly Dictionary<int, FileStream> _streams = [];
    private readonly object _gate = new();
    private bool _disposed;

    public CascArchiveSet(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    public byte[] ReadSpan(CascSpan span)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var stream = GetStream(span.Archive);
            stream.Position = span.Offset;
            var result = new byte[span.Size];
            stream.ReadExactly(result);
            return result;
        }
    }

    private FileStream GetStream(int archive)
    {
        if (_streams.TryGetValue(archive, out var stream))
        {
            return stream;
        }

        var path = Path.Combine(_dataDirectory, $"data.{archive:000}");
        stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        _streams.Add(archive, stream);
        return stream;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var stream in _streams.Values)
            {
                stream.Dispose();
            }

            _disposed = true;
        }
    }
}
