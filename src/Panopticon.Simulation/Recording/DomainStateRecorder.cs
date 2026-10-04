#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using Panopticon.Simulation;

namespace Panopticon.Recording;

/// <summary>
/// Domain state recording (A4-4): captures <see cref="IDomainStateProjection"/> state once per
/// sampled frame, keeps periodic snapshots plus per-frame deltas for reconstruction, and
/// optionally streams them to a binary artifact written live so a crash still leaves a usable
/// prefix. Samples exactly the frames the other tiers sample through
/// <see cref="WorldStateSampler.FrameSampled"/>.
/// </summary>
public sealed class DomainStateRecorder : IDisposable
{
    private readonly WorldStateSampler _sampler;
    private readonly IDomainStateProjection _projection;
    private readonly DomainStateArtifactWriter? _writer;
    private DomainStateFrame? _previous;

    public DomainStateSchema Schema { get; }
    public uint SnapshotInterval { get; }
    public string? ArtifactPath { get; }
    public List<DomainStateFrame> Snapshots { get; } = new();
    public List<DomainStateDelta> Deltas { get; } = new();

    public DomainStateRecorder(
        WorldStateSampler sampler,
        IDomainStateProjection projection,
        uint snapshotInterval = 300,
        string? artifactPath = null,
        IGame? game = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentOutOfRangeException.ThrowIfZero(snapshotInterval);

        if (artifactPath is not null && game is null)
        {
            throw new ArgumentException($"{nameof(game)} is required when {nameof(artifactPath)} is set.", nameof(game));
        }

        projection.StateSchema.Validate();

        _sampler = sampler;
        _projection = projection;
        Schema = projection.StateSchema;
        SnapshotInterval = snapshotInterval;
        ArtifactPath = artifactPath;
        _writer = artifactPath is not null ? new DomainStateArtifactWriter(artifactPath, game!, projection.StateSchema) : null;
        _sampler.FrameSampled += OnFrameSampled;
    }

    private void OnFrameSampled(WorldStateSummary summary, WorldStateDelta worldDelta)
    {
        var current = _projection.CaptureState(summary.Frame);
        var previous = _previous;
        _previous = current;

        DomainStateDelta delta;
        if (previous is null)
        {
            delta = new DomainStateDelta
            {
                Frame = current.Frame,
            };
        }
        else
        {
            delta = DomainStateDelta.Diff(current.Frame, previous, current);
        }

        Deltas.Add(delta);
        _writer?.WriteDelta(delta);

        // Full snapshots at the interval; deltas every frame (the reconstruction unit).
        if (current.Frame % SnapshotInterval == 0)
        {
            Snapshots.Add(current);
            _writer?.WriteSnapshot(current);
        }
    }

    /// <summary>
    /// Reconstructs the domain state at <paramref name="frame"/> from the recorded snapshots
    /// and deltas.
    /// </summary>
    public DomainStateFrame ReconstructStateAt(uint frame) =>
        DomainStateReconstruction.Reconstruct(Snapshots, Deltas, frame);

    public void Dispose()
    {
        _sampler.FrameSampled -= OnFrameSampled;
        _writer?.Dispose();
    }
}

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

public static class DomainStateArtifact
{
    internal const byte SchemaHeaderRecord = 1;
    internal const byte SnapshotRecord = 2;
    internal const byte DeltaRecord = 3;
    private const uint FormatVersion = 1;

    /// <summary>
    /// Loads a domain-state artifact written by <see cref="DomainStateArtifactWriter"/>. The
    /// schema header must be the first record; an unknown record tag or format version throws
    /// <see cref="InvalidDataException"/>.
    /// </summary>
    public static (DomainStateSchema Schema, List<DomainStateFrame> Snapshots, List<DomainStateDelta> Deltas) Load(string path, IGame game)
    {
        var schema = (DomainStateSchema?)null;
        var snapshots = new List<DomainStateFrame>();
        var deltas = new List<DomainStateDelta>();

        // FileShare.ReadWrite: artifacts are flushed per record, so loading is legal while the
        // owning recorder still holds the write stream.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StateReader(stream, game);
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var tag = byte.MaxValue;
            reader.PersistByte(ref tag, "RecordType");
            switch (tag)
            {
                case SchemaHeaderRecord:
                    schema = PersistSchema(reader, new DomainStateSchema("", 0, []));
                    break;
                case SnapshotRecord:
                    var snapshot = new DomainStateFrame();
                    reader.PersistObject(snapshot, "Snapshot");
                    snapshots.Add(snapshot);
                    break;
                case DeltaRecord:
                    var delta = new DomainStateDelta();
                    reader.PersistObject(delta, "Delta");
                    deltas.Add(delta);
                    break;
                default:
                    throw new InvalidDataException($"Unknown domain-state artifact record type {tag} at offset {reader.BaseStream.Position - 1}.");
            }
        }

        if (schema is null)
        {
            throw new InvalidDataException("Domain-state artifact has no schema header record.");
        }

        return (schema, snapshots, deltas);
    }

    /// <summary>
    /// Persists the schema header record body (without the record tag) in both modes; read mode
    /// reconstructs and returns the schema, write mode returns the input unchanged. Rejects
    /// unknown artifact format versions instead of guessing.
    /// </summary>
    internal static DomainStateSchema PersistSchema(StatePersister persister, DomainStateSchema schema)
    {
        var formatVersion = FormatVersion;
        persister.PersistUInt32(ref formatVersion, "FormatVersion");
        if (formatVersion != FormatVersion)
        {
            throw new InvalidDataException($"Unknown domain-state artifact format version {formatVersion}.");
        }

        var domainId = schema.DomainId;
        persister.PersistAsciiString(ref domainId, "DomainId");

        var version = schema.Version;
        persister.PersistInt32(ref version, "SchemaVersion");

        var kindCount = (uint)schema.Kinds.Count;
        persister.PersistUInt32(ref kindCount, "KindCount");

        var kinds = new List<DomainRecordKind>();
        for (var i = 0; i < kindCount; i++)
        {
            var kind = persister.Mode == StatePersistMode.Read ? null! : schema.Kinds[(int)i];

            var name = kind?.Name ?? "";
            persister.PersistAsciiString(ref name, "Name");

            var layer = kind?.Layer ?? "";
            persister.PersistAsciiString(ref layer, "Layer");

            var fields = PersistFields(persister, kind?.Fields);

            var referenceCount = (uint)(kind?.ReferenceNames.Count ?? 0);
            persister.PersistUInt32(ref referenceCount, "ReferenceCount");
            var references = new List<string>();
            for (var r = 0; r < referenceCount; r++)
            {
                var reference = persister.Mode == StatePersistMode.Read ? "" : kind.ReferenceNames[r];
                persister.PersistAsciiString(ref reference, "Reference");
                references.Add(reference);
            }

            if (persister.Mode == StatePersistMode.Read)
            {
                kinds.Add(new DomainRecordKind(name, layer, fields, references));
            }
        }

        return persister.Mode == StatePersistMode.Read
            ? new DomainStateSchema(domainId, version, kinds)
            : schema;
    }

    private static List<DomainStateField> PersistFields(StatePersister persister, IReadOnlyList<DomainStateField>? fields)
    {
        var fieldCount = (uint)(fields?.Count ?? 0);
        persister.PersistUInt32(ref fieldCount, "FieldCount");

        var result = new List<DomainStateField>();
        for (var i = 0; i < fieldCount; i++)
        {
            var field = persister.Mode == StatePersistMode.Read ? null! : fields![i];

            var name = field?.Name ?? "";
            persister.PersistAsciiString(ref name, "Name");

            var unit = field?.Unit ?? "";
            persister.PersistAsciiString(ref unit, "Unit");

            var type = field?.Type ?? DomainQuantityType.Integer;
            persister.PersistEnum(ref type, "Type");

            var role = field?.Role ?? DomainConservationRole.None;
            persister.PersistEnum(ref role, "Role");

            var hasFamily = field?.Family is not null;
            persister.PersistBoolean(ref hasFamily, "HasFamily");
            var family = field?.Family ?? "";
            persister.PersistAsciiString(ref family, "Family");

            if (persister.Mode == StatePersistMode.Read)
            {
                result.Add(new DomainStateField(name, unit, type, role, hasFamily ? family : null));
            }
        }

        return result;
    }
}
