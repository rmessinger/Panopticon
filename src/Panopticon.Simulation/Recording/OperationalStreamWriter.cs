using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Panopticon;
using Panopticon.Simulation;

namespace Panopticon.Recording;

/// <summary>One operational stream chunk: its file offset and boundary frame.</summary>
internal readonly record struct OperationalChunkEntry(long Offset, uint Frame);

/// <summary>
/// Operational stream writer (WI-A1-20 §5.2): PNOR header, then one length-prefixed
/// Brotli-compressed chunk per operational boundary, flushed on write. Chunks intern strings
/// into an append-only table and emit the new entries as the first record of their chunk. The
/// footer index is written only on <see cref="WriteFooter"/>; disposing without it leaves a
/// crash-equivalent file a reader can still scan.
/// </summary>
internal sealed class OperationalStreamWriter : IDisposable
{
    public const string Magic = "PNOR";
    public const uint FormatVersion = 1;

    private readonly Stream _stream;
    private readonly BinaryWriter _writer;
    private readonly IGame? _game;
    private readonly Dictionary<string, uint> _stringIds = new(StringComparer.Ordinal);
    private readonly List<string> _pendingStrings = new();
    private readonly List<OperationalChunkEntry> _index = new();
    private uint _nextStringId;

    public OperationalStreamWriter(Stream stream, string runId, string domainId, int domainSchemaVersion, IGame? game = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(runId);
        ArgumentNullException.ThrowIfNull(domainId);

        _game = game;
        if (!stream.CanWrite)
        {
            throw new ArgumentException("Stream must be writable.", nameof(stream));
        }

        _stream = stream;
        _writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);
        _writer.Write(Magic.Select(c => (byte)c).ToArray());
        _writer.Write(FormatVersion);
        _writer.Write(runId);
        _writer.Write(domainId);
        _writer.Write(domainSchemaVersion);
        _writer.Flush();
    }

    public int ChunkCount => _index.Count;

    /// <summary>Writes one boundary's chunk: optional Strings record first, then the records.</summary>
    public void WriteChunk(uint boundaryFrame, IReadOnlyList<OperationalRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        _pendingStrings.Clear();

        var body = new MemoryStream();
        using (var bodyWriter = new BinaryWriter(body, Encoding.Unicode, leaveOpen: true))
        {
            foreach (var record in records)
            {
                bodyWriter.Write((byte)record.Type);
                EncodeRecord(bodyWriter, record);
            }
        }

        var chunk = new MemoryStream();
        using (var chunkWriter = new BinaryWriter(chunk, Encoding.Unicode, leaveOpen: true))
        {
            if (_pendingStrings.Count > 0)
            {
                chunkWriter.Write((byte)OperationalRecordType.Strings);
                chunkWriter.Write(_pendingStrings.Count);
                foreach (var entry in _pendingStrings)
                {
                    chunkWriter.Write(entry);
                }
            }

            chunkWriter.Write(body.ToArray());
        }

        var payload = chunk.ToArray();
        using var compressed = new MemoryStream();
        using (var brotli = new BrotliStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            brotli.Write(payload);
        }

        var compressedBytes = compressed.ToArray();
        var offset = _stream.Position;
        _writer.Write(payload.Length);
        _writer.Write(compressedBytes.Length);
        _writer.Write(boundaryFrame);
        _writer.Write(compressedBytes);
        _writer.Flush();

        _index.Add(new OperationalChunkEntry(offset, boundaryFrame));
    }

    /// <summary>Writes the footer index of chunk offsets and frames. Call once at completion.</summary>
    public void WriteFooter()
    {
        _writer.Write(OperationalStreamReader.FooterMagic.Select(c => (byte)c).ToArray());
        _writer.Write(_index.Count);
        foreach (var entry in _index)
        {
            _writer.Write(entry.Offset);
            _writer.Write(entry.Frame);
        }

        _writer.Flush();
    }

    private uint InternString(string value)
    {
        if (_stringIds.TryGetValue(value, out var id))
        {
            return id;
        }

        id = _nextStringId++;
        _stringIds.Add(value, id);
        _pendingStrings.Add(value);
        return id;
    }

    private static void WriteFloatArray(BinaryWriter writer, IReadOnlyList<double> values)
    {
        writer.Write(values.Count);
        foreach (var value in values)
        {
            writer.Write(value);
        }
    }

    private static void WriteIntegerArray(BinaryWriter writer, IReadOnlyList<long> values)
    {
        writer.Write(values.Count);
        foreach (var value in values)
        {
            writer.Write(value);
        }
    }

    private void WriteRecord(BinaryWriter writer, DomainStateRecord record)
    {
        writer.Write(InternString(record.Key));
        writer.Write(InternString(record.Kind));
        writer.Write(record.OwnerIndex);
        WriteIntegerArray(writer, record.Integers);
        WriteFloatArray(writer, record.Reals);

        writer.Write(record.References.Length);
        foreach (var reference in record.References)
        {
            writer.Write(InternString(reference));
        }
    }

    private void EncodeRecord(BinaryWriter writer, OperationalRecord record)
    {
        switch (record)
        {
            case OperationalTick tick:
                writer.Write(tick.HostFrame);
                writer.Write(tick.LogicFrame);
                writer.Write(tick.TheaterTimeTicks.HasValue);
                if (tick.TheaterTimeTicks.HasValue)
                {
                    writer.Write(tick.TheaterTimeTicks.Value);
                }

                break;

            case OperationalDomainDelta delta:
                writer.Write(delta.BoundaryFrame);
                writer.Write(delta.Delta.Frame);
                writer.Write(delta.Delta.Upserted.Count);
                foreach (var upserted in delta.Delta.Upserted)
                {
                    WriteRecord(writer, upserted);
                }

                writer.Write(delta.Delta.Removed.Count);
                foreach (var removed in delta.Delta.Removed)
                {
                    writer.Write(InternString(removed));
                }

                break;

            case OperationalDomainSnapshot snapshot:
                writer.Write(snapshot.BoundaryFrame);
                writer.Write(snapshot.Frame.Frame);
                writer.Write(snapshot.Frame.Records.Count);
                foreach (var frameRecord in snapshot.Frame.Records)
                {
                    WriteRecord(writer, frameRecord);
                }

                break;

            case OperationalRollups rollups:
                writer.Write(rollups.BoundaryFrame);
                writer.Write(rollups.Records.Count);
                foreach (var rollup in rollups.Records)
                {
                    WriteRecord(writer, rollup);
                }

                break;

            case OperationalMetrics metrics:
                writer.Write(metrics.BoundaryFrame);
                writer.Write(metrics.Samples.Count);
                foreach (var sample in metrics.Samples)
                {
                    writer.Write(InternString(sample.Series));
                    writer.Write(InternString(sample.Key));
                    writer.Write(sample.Value);
                }

                break;

            case OperationalEvents events:
                writer.Write(events.BoundaryFrame);
                writer.Write(events.Events.Count);
                foreach (var domainEvent in events.Events)
                {
                    writer.Write(InternString(domainEvent.Kind));
                    writer.Write(domainEvent.Detail is not null);
                    if (domainEvent.Detail is not null)
                    {
                        writer.Write(domainEvent.Detail);
                    }
                }

                break;

            case OperationalInputs inputs:
                writer.Write(inputs.Orders.Count);
                foreach (var dispatched in inputs.Orders)
                {
                    writer.Write(dispatched.InjectionFrame);
                    writer.Write(dispatched.DispatchFrame);
                    EngineCheckpoint.WriteOrder(writer, dispatched.Order);
                }

                break;

            case OperationalT1 t1:
                writer.Write(t1.BoundaryFrame);
                writer.Write(t1.Payload.Count);
                foreach (var b in t1.Payload)
                {
                    writer.Write(b);
                }

                break;

            case OperationalWorldDelta worldDelta:
                if (_game is null)
                {
                    throw new InvalidDataException("World-delta records require a game reference.");
                }

                writer.Write(worldDelta.BoundaryFrame);
                using (var payload = new MemoryStream())
                {
                    using (var persister = new StateWriter(payload, _game))
                    {
                        persister.PersistObject(worldDelta.Delta, "WorldStateDelta");
                    }

                    writer.Write((int)payload.Length);
                    writer.Write(payload.ToArray());
                }

                break;

            default:
                throw new InvalidDataException($"Unsupported operational record type {record.GetType().Name}.");
        }
    }

    public void Dispose() => _writer.Dispose();
}
