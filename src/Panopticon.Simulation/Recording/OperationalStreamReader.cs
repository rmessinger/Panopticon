using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Panopticon;
using Panopticon.Logic.Orders;
using Panopticon.Simulation;

namespace Panopticon.Recording;

/// <summary>
/// Operational stream reader (WI-A1-20 §5.2): validates the PNOR header, rebuilds the chunk
/// index from the footer when present, and falls back to scanning chunks when the footer is
/// missing (crash before footer write). An incomplete trailing chunk is ignored. Read chunks in
/// ascending index order: the string table accumulates across calls.
/// </summary>
internal sealed class OperationalStreamReader : IDisposable
{
    public const string FooterMagic = "PNDF";

    private readonly Stream _stream;
    private readonly BinaryReader _reader;
    private readonly long _dataStart;
    private readonly Dictionary<uint, string> _strings = new();

    static OperationalStreamReader()
    {
        // The footer magic must never collide with a plausible chunk length prefix.
        if (BitConverter.ToInt32(Encoding.ASCII.GetBytes(FooterMagic)) < 1_000_000_000)
        {
            throw new InvalidOperationException("Footer magic overlaps plausible chunk lengths.");
        }
    }

    private OperationalStreamReader(Stream stream, BinaryReader reader, long dataStart, string runId, string domainId, int schemaVersion, IGame? game)
    {
        _stream = stream;
        _reader = reader;
        _dataStart = dataStart;
        _game = game;
        RunId = runId;
        DomainId = domainId;
        DomainSchemaVersion = schemaVersion;
    }

    private readonly IGame? _game;

    public string RunId { get; }

    public string DomainId { get; }

    public int DomainSchemaVersion { get; }

    public static OperationalStreamReader Open(Stream stream, IGame? game = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Stream must be readable and seekable.", nameof(stream));
        }

        var reader = new BinaryReader(stream, Encoding.Unicode, leaveOpen: true);

        ValidateMagic(reader);

        var version = reader.ReadUInt32();
        if (version != OperationalStreamWriter.FormatVersion)
        {
            throw new InvalidDataException($"Unsupported operational stream version {version}.");
        }

        var runId = reader.ReadString();
        var domainId = reader.ReadString();
        var schemaVersion = reader.ReadInt32();
        return new OperationalStreamReader(stream, reader, stream.Position, runId, domainId, schemaVersion, game);
    }

    private static void ValidateMagic(BinaryReader reader)
    {
        var magic = reader.ReadBytes(OperationalStreamWriter.Magic.Length);
        if (magic.Length != OperationalStreamWriter.Magic.Length || Encoding.ASCII.GetString(magic) != OperationalStreamWriter.Magic)
        {
            throw new InvalidDataException("Bad operational stream magic.");
        }
    }

    /// <summary>Returns the footer index, or null when the footer is absent or truncated.</summary>
    public IReadOnlyList<OperationalChunkEntry>? TryReadFooterIndex()
    {
        _stream.Position = _dataStart;
        while (_stream.Position < _stream.Length)
        {
            if (PeekFooter())
            {
                return ReadFooterEntries();
            }

            var remaining = _stream.Length - _stream.Position;
            if (remaining < 12)
            {
                return null;
            }

            var uncompressedLength = _reader.ReadInt32();
            var compressedLength = _reader.ReadInt32();
            _stream.Position += 4; // boundary frame
            if (uncompressedLength < 0 || compressedLength < 0 || _stream.Length - _stream.Position < compressedLength)
            {
                return null;
            }

            _stream.Position += compressedLength;
        }

        return null;
    }

    /// <summary>
    /// Scans chunks from the data start, stopping at the footer and ignoring a truncated
    /// trailing chunk (the crash-before-footer case).
    /// </summary>
    public IReadOnlyList<OperationalChunkEntry> BuildScanIndex()
    {
        _stream.Position = _dataStart;
        var entries = new List<OperationalChunkEntry>();
        while (_stream.Position < _stream.Length)
        {
            if (PeekFooter())
            {
                break;
            }

            var offset = _stream.Position;
            var remaining = _stream.Length - _stream.Position;
            if (remaining < 12)
            {
                break;
            }

            var uncompressedLength = _reader.ReadInt32();
            var compressedLength = _reader.ReadInt32();
            var frame = _reader.ReadUInt32();
            if (uncompressedLength < 0 || compressedLength < 0)
            {
                throw new InvalidDataException($"Malformed chunk header at offset {offset}.");
            }

            if (_stream.Length - _stream.Position < compressedLength)
            {
                break;
            }

            entries.Add(new OperationalChunkEntry(offset, frame));
            _stream.Position += compressedLength;
        }

        return entries;
    }

    /// <summary>Footer index when present, else the scan-built index.</summary>
    public IReadOnlyList<OperationalChunkEntry> ReadIndex() => TryReadFooterIndex() ?? BuildScanIndex();

    /// <summary>
    /// Reads, decompresses, and parses one chunk. Call in ascending index order so string ids
    /// resolve against the accumulated table.
    /// </summary>
    public (uint Frame, IReadOnlyList<OperationalRecord> Records) ReadChunk(OperationalChunkEntry entry)
    {
        _stream.Position = entry.Offset;
        var uncompressedLength = _reader.ReadInt32();
        var compressedLength = _reader.ReadInt32();
        var frame = _reader.ReadUInt32();
        if (uncompressedLength < 0 || compressedLength < 0)
        {
            throw new InvalidDataException($"Malformed chunk header at offset {entry.Offset}.");
        }

        var compressed = _reader.ReadBytes(compressedLength);
        if (compressed.Length != compressedLength)
        {
            throw new InvalidDataException($"Truncated chunk payload at offset {entry.Offset}.");
        }

        byte[] payload;
        try
        {
            using var decompressed = new MemoryStream();
            using (var brotli = new BrotliStream(new MemoryStream(compressed), CompressionMode.Decompress))
            {
                brotli.CopyTo(decompressed);
            }

            payload = decompressed.ToArray();
        }
        catch (InvalidOperationException)
        {
            throw new InvalidDataException($"Failed to decompress chunk at offset {entry.Offset}.");
        }

        if (payload.Length != uncompressedLength)
        {
            throw new InvalidDataException($"Chunk at offset {entry.Offset} decompressed to {payload.Length} bytes, expected {uncompressedLength}.");
        }

        using var payloadReader = new BinaryReader(new MemoryStream(payload, writable: false), Encoding.Unicode);
        var records = new List<OperationalRecord>();
        while (payloadReader.BaseStream.Position < payloadReader.BaseStream.Length)
        {
            records.Add(DecodeRecord(payloadReader));
        }

        return (frame, records);
    }

    private bool PeekFooter()
    {
        if (_stream.Length - _stream.Position < FooterMagic.Length)
        {
            return false;
        }

        var position = _stream.Position;
        var magic = _reader.ReadBytes(FooterMagic.Length);
        _stream.Position = position;
        return magic.Length == FooterMagic.Length && Encoding.ASCII.GetString(magic) == FooterMagic;
    }

    private IReadOnlyList<OperationalChunkEntry> ReadFooterEntries()
    {
        _stream.Position += FooterMagic.Length;
        var count = _reader.ReadInt32();
        if (count < 0 || _stream.Length - _stream.Position < count * 12L)
        {
            return new List<OperationalChunkEntry>();
        }

        var entries = new List<OperationalChunkEntry>(count);
        for (var i = 0; i < count; i++)
        {
            entries.Add(new OperationalChunkEntry(_reader.ReadInt64(), _reader.ReadUInt32()));
        }

        return entries;
    }

    private string ResolveString(uint id)
    {
        if (_strings.TryGetValue(id, out var value))
        {
            return value;
        }

        throw new InvalidDataException($"Unknown string id {id} in operational stream.");
    }

    private static long[] ReadIntegerArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        if (count < 0)
        {
            throw new InvalidDataException("Negative integer array length.");
        }

        var values = new long[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = reader.ReadInt64();
        }

        return values;
    }

    private static double[] ReadFloatArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        if (count < 0)
        {
            throw new InvalidDataException("Negative real array length.");
        }

        var values = new double[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = reader.ReadDouble();
        }

        return values;
    }

    private DomainStateRecord ReadStateRecord(BinaryReader reader)
    {
        var key = ResolveString(reader.ReadUInt32());
        var kind = ResolveString(reader.ReadUInt32());
        var ownerIndex = reader.ReadInt32();
        var integers = ReadIntegerArray(reader);
        var reals = ReadFloatArray(reader);
        var referenceCount = reader.ReadInt32();
        if (referenceCount < 0)
        {
            throw new InvalidDataException("Negative reference count.");
        }

        var references = new string[referenceCount];
        for (var i = 0; i < referenceCount; i++)
        {
            references[i] = ResolveString(reader.ReadUInt32());
        }

        return new DomainStateRecord
        {
            Key = key,
            Kind = kind,
            OwnerIndex = ownerIndex,
            Integers = integers,
            Reals = reals,
            References = references,
        };
    }

    private OperationalRecord DecodeRecord(BinaryReader reader)
    {
        var type = (OperationalRecordType)reader.ReadByte();
        switch (type)
        {
            case OperationalRecordType.Strings:
                {
                    var count = reader.ReadInt32();
                    if (count < 0)
                    {
                        throw new InvalidDataException("Negative string table entry count.");
                    }

                    var entries = new string[count];
                    for (var i = 0; i < count; i++)
                    {
                        entries[i] = reader.ReadString();
                        _strings[(uint)_strings.Count] = entries[i];
                    }

                    return new OperationalStrings(entries);
                }

            case OperationalRecordType.Tick:
                {
                    var hostFrame = reader.ReadUInt32();
                    var logicFrame = reader.ReadUInt32();
                    long? theaterTime = reader.ReadBoolean() ? reader.ReadInt64() : null;
                    return new OperationalTick(hostFrame, logicFrame, theaterTime);
                }

            case OperationalRecordType.DomainDelta:
                {
                    var boundaryFrame = reader.ReadUInt32();
                    var deltaFrame = reader.ReadUInt32();
                    var upsertCount = reader.ReadInt32();
                    if (upsertCount < 0)
                    {
                        throw new InvalidDataException("Negative upsert count.");
                    }

                    var delta = new DomainStateDelta
                    {
                        Frame = deltaFrame,
                    };

                    for (var i = 0; i < upsertCount; i++)
                    {
                        delta.Upserted.Add(ReadStateRecord(reader));
                    }

                    var removedCount = reader.ReadInt32();
                    if (removedCount < 0)
                    {
                        throw new InvalidDataException("Negative removal count.");
                    }

                    for (var i = 0; i < removedCount; i++)
                    {
                        delta.Removed.Add(ResolveString(reader.ReadUInt32()));
                    }

                    return new OperationalDomainDelta(boundaryFrame, delta);
                }

            case OperationalRecordType.DomainSnapshot:
                {
                    var boundaryFrame = reader.ReadUInt32();
                    var stateFrame = reader.ReadUInt32();
                    var recordCount = reader.ReadInt32();
                    if (recordCount < 0)
                    {
                        throw new InvalidDataException("Negative snapshot record count.");
                    }

                    var frame = new DomainStateFrame
                    {
                        Frame = stateFrame,
                    };

                    for (var i = 0; i < recordCount; i++)
                    {
                        frame.Records.Add(ReadStateRecord(reader));
                    }

                    return new OperationalDomainSnapshot(boundaryFrame, frame);
                }

            case OperationalRecordType.Rollups:
                {
                    var boundaryFrame = reader.ReadUInt32();
                    var recordCount = reader.ReadInt32();
                    if (recordCount < 0)
                    {
                        throw new InvalidDataException("Negative rollup record count.");
                    }

                    var records = new List<DomainStateRecord>(recordCount);
                    for (var i = 0; i < recordCount; i++)
                    {
                        records.Add(ReadStateRecord(reader));
                    }

                    return new OperationalRollups(boundaryFrame, records);
                }

            case OperationalRecordType.Metrics:
                {
                    var boundaryFrame = reader.ReadUInt32();
                    var sampleCount = reader.ReadInt32();
                    if (sampleCount < 0)
                    {
                        throw new InvalidDataException("Negative metric sample count.");
                    }

                    var samples = new OperationalMetricSample[sampleCount];
                    for (var i = 0; i < sampleCount; i++)
                    {
                        var series = ResolveString(reader.ReadUInt32());
                        var key = ResolveString(reader.ReadUInt32());
                        samples[i] = new OperationalMetricSample(series, key, reader.ReadDouble());
                    }

                    return new OperationalMetrics(boundaryFrame, samples);
                }

            case OperationalRecordType.Events:
                {
                    var boundaryFrame = reader.ReadUInt32();
                    var eventCount = reader.ReadInt32();
                    if (eventCount < 0)
                    {
                        throw new InvalidDataException("Negative event count.");
                    }

                    var events = new DomainEvent[eventCount];
                    for (var i = 0; i < eventCount; i++)
                    {
                        var kind = ResolveString(reader.ReadUInt32());
                        var detail = reader.ReadBoolean() ? reader.ReadString() : null;
                        events[i] = new DomainEvent(kind, detail);
                    }

                    return new OperationalEvents(boundaryFrame, events);
                }

            case OperationalRecordType.Inputs:
                {
                    var orderCount = reader.ReadInt32();
                    if (orderCount < 0)
                    {
                        throw new InvalidDataException("Negative dispatched order count.");
                    }

                    var orders = new List<OperationalDispatchedOrder>(orderCount);
                    for (var i = 0; i < orderCount; i++)
                    {
                        var injectionFrame = reader.ReadUInt32();
                        var dispatchFrame = reader.ReadUInt32();
                        orders.Add(new OperationalDispatchedOrder(injectionFrame, dispatchFrame, EngineCheckpoint.ReadOrder(reader)));
                    }

                    return new OperationalInputs(orders);
                }

            case OperationalRecordType.T1:
                {
                    var boundaryFrame = reader.ReadUInt32();
                    var byteCount = reader.ReadInt32();
                    if (byteCount < 0)
                    {
                        throw new InvalidDataException("Negative T1 payload length.");
                    }

                    var payload = reader.ReadBytes(byteCount);
                    return new OperationalT1(boundaryFrame, payload);
                }

            case OperationalRecordType.WorldDelta:
                {
                    var boundaryFrame = reader.ReadUInt32();
                    var byteCount = reader.ReadInt32();
                    if (byteCount < 0)
                    {
                        throw new InvalidDataException("Negative world-delta payload length.");
                    }

                    if (_game is null)
                    {
                        throw new InvalidDataException("World-delta records require a game reference.");
                    }

                    var payload = reader.ReadBytes(byteCount);
                    var delta = new WorldStateDelta();
                    using (var payloadStream = new MemoryStream(payload, writable: false))
                    using (var persister = new StateReader(payloadStream, _game))
                    {
                        persister.PersistObject(delta, "WorldStateDelta");
                    }

                    return new OperationalWorldDelta(boundaryFrame, delta);
                }

            default:
                throw new InvalidDataException($"Unknown operational record type {(byte)type}.");
        }
    }

    public void Dispose() => _reader.Dispose();
}
