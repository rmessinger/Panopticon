#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using Panopticon.Data.Sav;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Panopticon.Simulation;

namespace Panopticon.Recording;

/// <summary>
/// Checkpoint file header (WI-A1-15 §5.3): frame, theater time, identity hash, and run id.
/// </summary>
public sealed record EngineCheckpointHeader(uint Frame, long TheaterTimeTicks, string IdentityHash, string RunId);

/// <summary>
/// A parsed engine checkpoint: the header plus the decompressed payload of every section.
/// </summary>
public sealed class EngineCheckpointData
{
    public const string EngineSection = "engine";
    public const string RandomSection = "random";
    public const string HostSection = "host";
    public const string DomainRuntimeSection = "domain-runtime";
    public const string DomainStateSection = "domain-state";

    public EngineCheckpointData(EngineCheckpointHeader header, IReadOnlyDictionary<string, byte[]> sections)
    {
        Header = header;
        Sections = sections;
    }

    public EngineCheckpointHeader Header { get; }

    public IReadOnlyDictionary<string, byte[]> Sections { get; }
}

/// <summary>
/// Writer and reader for the PNCK engine checkpoint format (WI-A1-15): a header followed by
/// length-prefixed Brotli-compressed named sections holding engine, random, host, and optional
/// domain runtime and domain state payloads. Restore into a live host is WI-A1-16.
/// </summary>
public static class EngineCheckpoint
{
    private const string Magic = "PNCK";
    private const uint FormatVersion = 1;

    private static readonly string[] RequiredSections =
    [
        EngineCheckpointData.EngineSection,
        EngineCheckpointData.RandomSection,
        EngineCheckpointData.HostSection,
    ];

    public static void Write(Stream output, EngineCheckpointHeader header, IEnumerable<KeyValuePair<string, byte[]>> sections)
    {
        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
        foreach (var ch in Magic)
        {
            writer.Write((byte)ch);
        }

        writer.Write(FormatVersion);
        writer.Write(header.Frame);
        writer.Write(header.TheaterTimeTicks);
        writer.Write(header.IdentityHash);
        writer.Write(header.RunId);

        var sectionList = sections.ToList();
        writer.Write(sectionList.Count);
        foreach (var (name, payload) in sectionList)
        {
            writer.Write(name);
            writer.Write(payload.Length);

            using var compressed = new MemoryStream();
            using (var brotli = new BrotliStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                brotli.Write(payload, 0, payload.Length);
            }

            var compressedBytes = compressed.ToArray();
            writer.Write(compressedBytes.Length);
            writer.Write(compressedBytes);
        }
    }

    public static EngineCheckpointData Read(Stream input)
    {
        using var reader = new BinaryReader(input, System.Text.Encoding.UTF8, leaveOpen: true);
        Span<byte> magic = stackalloc byte[Magic.Length];
        var read = reader.Read(magic);
        if (read != Magic.Length || !magic.SequenceEqual(Magic.Select(c => (byte)c).ToArray()))
        {
            throw new InvalidDataException("Not a Panopticon engine checkpoint: bad magic.");
        }

        var version = reader.ReadUInt32();
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Unsupported engine checkpoint format version {version}.");
        }

        var frame = reader.ReadUInt32();
        var theaterTimeTicks = reader.ReadInt64();
        var identityHash = reader.ReadString();
        var runId = reader.ReadString();
        var header = new EngineCheckpointHeader(frame, theaterTimeTicks, identityHash, runId);

        var sectionCount = reader.ReadInt32();
        var sections = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (var i = 0; i < sectionCount; i++)
        {
            var name = reader.ReadString();
            if (sections.ContainsKey(name))
            {
                throw new InvalidDataException($"Duplicate engine checkpoint section '{name}'.");
            }

            var uncompressedLength = reader.ReadInt32();
            var compressedLength = reader.ReadInt32();
            var compressedBytes = reader.ReadBytes(compressedLength);
            if (compressedBytes.Length != compressedLength)
            {
                throw new InvalidDataException($"Truncated engine checkpoint: section '{name}' compressed payload is cut short.");
            }

            sections[name] = Decompress(name, compressedBytes, uncompressedLength);
        }

        foreach (var required in RequiredSections)
        {
            if (!sections.ContainsKey(required))
            {
                throw new InvalidDataException($"Engine checkpoint is missing required section '{required}'.");
            }
        }

        return new EngineCheckpointData(header, sections);
    }

    /// <summary>
    /// Captures every section a host can provide: engine logic chunks, logic random state, host
    /// frame/map-time/pending orders, and domain runtime/state when the domain model implements
    /// the A4-4/A1-6 contracts. Sections whose capability is absent are omitted.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, byte[]>> CaptureSections(SimulationHost host, string identityHash, string runId)
    {
        var sections = new List<KeyValuePair<string, byte[]>>
        {
            new(EngineCheckpointData.EngineSection, CaptureEngineSection(host.Game)),
            new(EngineCheckpointData.RandomSection, CaptureRandomSection(host)),
            new(EngineCheckpointData.HostSection, CaptureHostSection(host)),
        };

        if (host.DomainModel is IReconstructableDomain reconstructable)
        {
            sections.Add(new(EngineCheckpointData.DomainRuntimeSection, CaptureDomainRuntimeSection(host, reconstructable)));
        }

        if (host.DomainModel is IDomainStateProjection projection)
        {
            sections.Add(new(EngineCheckpointData.DomainStateSection, CaptureDomainStateSection(host, projection)));
        }

        return sections;
    }

    internal static byte[] CaptureEngineSection(IGame game)
    {
        var stream = new MemoryStream();
        using (var writer = new StateWriter(stream, game))
        {
            SaveFile.Persist(writer, SaveFile.IsLogicChunk);
        }

        return stream.ToArray();
    }

    internal static byte[] CaptureRandomSection(SimulationHost host)
    {
        var random = host.Game.GameLogic.Random;
        if (random is not SageRandom sageRandom)
        {
            throw new InvalidOperationException("The logic random must be a persistable SageRandom (WI-A1-14).");
        }

        var words = new uint[SageRandom.StateWordCount];
        sageRandom.CopyState(words);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(sageRandom.Seed);
        foreach (var word in words)
        {
            writer.Write(word);
        }

        return stream.ToArray();
    }

    internal static byte[] CaptureHostSection(SimulationHost host)
    {
        var (frame, mapTime, pendingOrders) = host.CaptureHostState();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(frame);
        writer.Write(mapTime.Ticks);
        writer.Write(pendingOrders.Count);
        foreach (var (scheduledFrame, orders) in pendingOrders)
        {
            writer.Write(scheduledFrame);
            writer.Write(orders.Count);
            foreach (var order in orders)
            {
                WriteOrder(writer, order);
            }
        }

        return stream.ToArray();
    }

    internal static (uint Seed, uint[] Words) DecodeRandomSection(byte[] payload)
    {
        using var reader = new BinaryReader(new MemoryStream(payload, writable: false));
        var seed = reader.ReadUInt32();
        var words = new uint[SageRandom.StateWordCount];
        for (var i = 0; i < words.Length; i++)
        {
            words[i] = reader.ReadUInt32();
        }

        return (seed, words);
    }

    internal static (uint Frame, TimeSpan MapTime, SortedDictionary<uint, List<Order>> PendingOrders) DecodeHostSection(byte[] payload)
    {
        using var reader = new BinaryReader(new MemoryStream(payload, writable: false));
        var frame = reader.ReadUInt32();
        var mapTime = TimeSpan.FromTicks(reader.ReadInt64());
        var pendingOrders = new SortedDictionary<uint, List<Order>>();
        var entryCount = reader.ReadInt32();
        for (var i = 0; i < entryCount; i++)
        {
            var scheduledFrame = reader.ReadUInt32();
            var orderCount = reader.ReadInt32();
            var orders = new List<Order>(orderCount);
            for (var j = 0; j < orderCount; j++)
            {
                orders.Add(ReadOrder(reader));
            }

            pendingOrders[scheduledFrame] = orders;
        }

        return (frame, mapTime, pendingOrders);
    }

    internal static byte[] CaptureDomainRuntimeSection(SimulationHost host, IReconstructableDomain domain)
    {
        var stream = new MemoryStream();
        using var writer = new StateWriter(stream, host.Game);
        domain.PersistRuntime(writer);
        return stream.ToArray();
    }

    internal static byte[] CaptureDomainStateSection(SimulationHost host, IDomainStateProjection projection)
    {
        var stateFrame = projection.CaptureState(host.LogicFrame.Value);
        var stream = new MemoryStream();
        using var writer = new StateWriter(stream, host.Game);
        writer.PersistObject(stateFrame, "DomainStateFrame");
        return stream.ToArray();
    }

    internal static DomainStateFrame DecodeDomainStateSection(byte[] payload, IGame game)
    {
        using var reader = new StateReader(new MemoryStream(payload, writable: false), game);
        var frame = new DomainStateFrame();
        reader.PersistObject(frame, "DomainStateFrame");
        return frame;
    }

    internal static void WriteOrder(BinaryWriter writer, Order order)
    {
        writer.Write(order.PlayerIndex);
        writer.Write((int)order.OrderType);
        writer.Write(order.Arguments.Count);
        foreach (var argument in order.Arguments)
        {
            writer.Write((byte)argument.ArgumentType);
            switch (argument.ArgumentType)
            {
                case OrderArgumentType.Integer:
                    writer.Write(argument.Value.Integer);
                    break;
                case OrderArgumentType.Float:
                    writer.Write(argument.Value.Float);
                    break;
                case OrderArgumentType.Boolean:
                    writer.Write(argument.Value.Boolean);
                    break;
                case OrderArgumentType.ObjectId:
                    writer.Write(argument.Value.ObjectId.Index);
                    break;
                case OrderArgumentType.Position:
                    writer.Write(argument.Value.Position.X);
                    writer.Write(argument.Value.Position.Y);
                    writer.Write(argument.Value.Position.Z);
                    break;
                case OrderArgumentType.ScreenPosition:
                    writer.Write(argument.Value.ScreenPosition.X);
                    writer.Write(argument.Value.ScreenPosition.Y);
                    break;
                case OrderArgumentType.ScreenRectangle:
                    writer.Write(argument.Value.ScreenRectangle.X);
                    writer.Write(argument.Value.ScreenRectangle.Y);
                    writer.Write(argument.Value.ScreenRectangle.Width);
                    writer.Write(argument.Value.ScreenRectangle.Height);
                    break;
                default:
                    throw new InvalidDataException($"Unknown order argument type {argument.ArgumentType}.");
            }
        }
    }

    internal static Order ReadOrder(BinaryReader reader)
    {
        var playerIndex = reader.ReadInt32();
        var orderType = (OrderType)reader.ReadInt32();
        var order = new Order(playerIndex, orderType);
        var argumentCount = reader.ReadInt32();
        for (var i = 0; i < argumentCount; i++)
        {
            var argumentType = (OrderArgumentType)reader.ReadByte();
            switch (argumentType)
            {
                case OrderArgumentType.Integer:
                    order.AddIntegerArgument(reader.ReadInt32());
                    break;
                case OrderArgumentType.Float:
                    order.AddFloatArgument(reader.ReadSingle());
                    break;
                case OrderArgumentType.Boolean:
                    order.AddBooleanArgument(reader.ReadBoolean());
                    break;
                case OrderArgumentType.ObjectId:
                    order.AddObjectIdArgument(new ObjectId(reader.ReadUInt32()));
                    break;
                case OrderArgumentType.Position:
                    order.AddPositionArgument(new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
                    break;
                case OrderArgumentType.ScreenPosition:
                    order.AddScreenPositionArgument(new Point2D(reader.ReadInt32(), reader.ReadInt32()));
                    break;
                case OrderArgumentType.ScreenRectangle:
                    order.AddScreenRectangleArgument(new Rectangle(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
                    break;
                default:
                    throw new InvalidDataException($"Unknown order argument type {argumentType}.");
            }
        }

        return order;
    }

    private static byte[] Decompress(string name, byte[] compressedBytes, int uncompressedLength)
    {
        using var compressed = new MemoryStream(compressedBytes);
        using var brotli = new BrotliStream(compressed, CompressionMode.Decompress);
        var result = new byte[uncompressedLength];
        try
        {
            var totalRead = 0;
            while (totalRead < uncompressedLength)
            {
                var read = brotli.Read(result, totalRead, uncompressedLength - totalRead);
                if (read == 0)
                {
                    throw new InvalidDataException($"Truncated engine checkpoint: section '{name}' decompresses to fewer bytes than declared.");
                }

                totalRead += read;
            }

            if (brotli.ReadByte() != -1)
            {
                throw new InvalidDataException($"Corrupt engine checkpoint: section '{name}' decompresses to more bytes than declared.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or EndOfStreamException)
        {
            throw new InvalidDataException($"Corrupt engine checkpoint: section '{name}' failed to decompress.", exception);
        }

        return result;
    }
}
