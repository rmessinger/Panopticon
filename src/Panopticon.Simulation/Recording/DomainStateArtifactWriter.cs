#nullable enable annotations
using System.IO;
using Panopticon.Simulation;

namespace Panopticon.Recording;

/// <summary>
/// Streaming artifact writer for domain state recording (A4-4): a schema header followed by
/// type-tagged snapshot and delta records using the save-format
/// <see cref="StatePersister"/> primitives, written live and flushed per record. Read back with
/// <see cref="DomainStateArtifact.Load"/>.
/// </summary>
public sealed class DomainStateArtifactWriter : DisposableBase
{
    private readonly FileStream _stream;
    private readonly StateWriter _writer;

    public DomainStateArtifactWriter(string path, IGame game, DomainStateSchema schema)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // FileShare.Read keeps the artifact loadable while the recorder holds the write
        // stream; disposal order (reverse registration) closes the writer before the stream.
        _stream = AddDisposable(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read));
        _writer = AddDisposable(new StateWriter(_stream, game));
        var headerTag = DomainStateArtifact.SchemaHeaderRecord;
        _writer.PersistByte(ref headerTag, "RecordType");
        DomainStateArtifact.PersistSchema(_writer, schema);
        _writer.BaseStream.Flush();
    }

    public void WriteSnapshot(DomainStateFrame frame)
    {
        var tag = DomainStateArtifact.SnapshotRecord;
        _writer.PersistByte(ref tag, "RecordType");
        _writer.PersistObject(frame, "Snapshot");
        _writer.BaseStream.Flush();
    }

    public void WriteDelta(DomainStateDelta delta)
    {
        var tag = DomainStateArtifact.DeltaRecord;
        _writer.PersistByte(ref tag, "RecordType");
        _writer.PersistObject(delta, "Delta");
        _writer.BaseStream.Flush();
    }
}
