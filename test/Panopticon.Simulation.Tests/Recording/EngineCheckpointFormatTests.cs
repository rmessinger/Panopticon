using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Panopticon;
using Panopticon.Data.Sav;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Recording;
using Panopticon.Simulation;
using Panopticon.Tests.Data;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Recording;

[Collection("SerialGameTests")]
public class EngineCheckpointFormatTests : StatePersisterTest, IDisposable
{
    private readonly ITestOutputHelper _output;

    public EngineCheckpointFormatTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose()
    {
    }

    [Fact]
    public void RoundTrips_HeaderAndSections_PreservingBytes()
    {
        var header = new EngineCheckpointHeader(1234, 56789, "run-1");
        var sections = new List<KeyValuePair<string, byte[]>>
        {
            new(EngineCheckpointData.EngineSection, new byte[] { 3, 1, 4, 1, 5 }),
            new(EngineCheckpointData.RandomSection, new byte[] { 9 }),
            new(EngineCheckpointData.HostSection, new byte[] { 7, 7 }),
            new(EngineCheckpointData.DomainRuntimeSection, []),
            new(EngineCheckpointData.DomainStateSection, new byte[] { 1, 2, 3 }),
        };

        using var stream = new MemoryStream();
        EngineCheckpoint.Write(stream, header, sections);
        stream.Position = 0;

        var data = EngineCheckpoint.Read(stream);

        Assert.Equal(header, data.Header);
        Assert.Equal(5, data.Sections.Count);
        foreach (var (name, payload) in sections)
        {
            Assert.Equal(payload, data.Sections[name]);
        }
    }

    [Fact]
    public void Rejects_BadMagic()
    {
        var bytes = WriteMinimalCheckpoint();
        bytes[0] = (byte)'X';

        var exception = Assert.Throws<InvalidDataException>(() => EngineCheckpoint.Read(new MemoryStream(bytes)));
        Assert.Contains("magic", exception.Message);
    }

    [Fact]
    public void Rejects_UnsupportedVersion()
    {
        var bytes = WriteMinimalCheckpoint();
        bytes[4] = 99;

        var exception = Assert.Throws<InvalidDataException>(() => EngineCheckpoint.Read(new MemoryStream(bytes)));
        Assert.Contains("version", exception.Message);
    }

    [Fact]
    public void Rejects_TruncatedCompressedPayload()
    {
        using var stream = new MemoryStream();
        var header = new EngineCheckpointHeader(1, 2, "run");
        EngineCheckpoint.Write(
            stream,
            header,
            [new(EngineCheckpointData.EngineSection, new byte[] { 1, 2, 3 })]);
        var bytes = stream.ToArray();

        // Declared compressed length (offset of first section's compressed-length field: magic 4
        // + version 4 + frame 4 + ticks 8 + hash + runId + count 4 + name + uncompressed 4).
        var compressedLengthOffset = FindFirstSectionCompressedLengthOffset(bytes);
        var declared = BitConverter.ToInt32(bytes, compressedLengthOffset);
        var truncated = bytes.Take(compressedLengthOffset + 4 + declared - 1).ToArray();

        var exception = Assert.Throws<InvalidDataException>(() => EngineCheckpoint.Read(new MemoryStream(truncated)));
        Assert.Contains("Truncated", exception.Message);
    }

    [Fact]
    public void Rejects_CorruptCompressedPayload()
    {
        var payload = Encoding.UTF8.GetBytes("checkpoint payload that brequires more than a trivial brotli block");
        using var compressed = new MemoryStream();
        using (var brotli = new BrotliStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            brotli.Write(payload, 0, payload.Length);
        }

        var compressedBytes = compressed.ToArray();
        compressedBytes[compressedBytes.Length / 2] ^= 0xFF;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write("PNCK".Select(c => (byte)c).ToArray());
        writer.Write(1u);
        writer.Write(1u);
        writer.Write(2L);
        writer.Write("run");
        writer.Write(1);
        writer.Write(EngineCheckpointData.EngineSection);
        writer.Write(payload.Length);
        writer.Write(compressedBytes.Length);
        writer.Write(compressedBytes);
        writer.Write(EngineCheckpointData.RandomSection);
        writer.Write(0);
        writer.Write(0);
        writer.Write(Array.Empty<byte>());
        writer.Write(EngineCheckpointData.HostSection);
        writer.Write(0);
        writer.Write(0);
        writer.Write(Array.Empty<byte>());

        Assert.ThrowsAny<InvalidDataException>(() => EngineCheckpoint.Read(new MemoryStream(stream.ToArray())));
    }

    [Fact]
    public void Rejects_DuplicateSection()
    {
        var header = new EngineCheckpointHeader(1, 2, "run");
        using var stream = new MemoryStream();
        EngineCheckpoint.Write(
            stream,
            header,
        [
            new(EngineCheckpointData.EngineSection, [1]),
            new(EngineCheckpointData.RandomSection, [2]),
            new(EngineCheckpointData.HostSection, [3]),
            new(EngineCheckpointData.EngineSection, [4]),
        ]);

        var exception = Assert.Throws<InvalidDataException>(() => EngineCheckpoint.Read(new MemoryStream(stream.ToArray())));
        Assert.Contains("Duplicate", exception.Message);
    }

    [Fact]
    public void Rejects_MissingRequiredSection()
    {
        var header = new EngineCheckpointHeader(1, 2, "run");
        using var stream = new MemoryStream();
        EngineCheckpoint.Write(
            stream,
            header,
        [
            new(EngineCheckpointData.EngineSection, [1]),
            new(EngineCheckpointData.HostSection, [3]),
        ]);

        var exception = Assert.Throws<InvalidDataException>(() => EngineCheckpoint.Read(new MemoryStream(stream.ToArray())));
        Assert.Contains("missing required section 'random'", exception.Message);
    }

    [Fact]
    public void Rejects_UncompressedLengthMismatch()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        using var compressed = new MemoryStream();
        using (var brotli = new BrotliStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            brotli.Write(payload, 0, payload.Length);
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write("PNCK".Select(c => (byte)c).ToArray());
        writer.Write(1u);
        writer.Write(1u);
        writer.Write(2L);
        writer.Write("run");
        writer.Write(1);
        writer.Write(EngineCheckpointData.EngineSection);
        writer.Write(5); // one shorter than the payload decompresses to
        writer.Write((int)compressed.Length);
        writer.Write(compressed.ToArray());
        writer.Write(EngineCheckpointData.RandomSection);
        writer.Write(0);
        writer.Write(0);
        writer.Write(Array.Empty<byte>());
        writer.Write(EngineCheckpointData.HostSection);
        writer.Write(0);
        writer.Write(0);
        writer.Write(Array.Empty<byte>());

        var exception = Assert.Throws<InvalidDataException>(() => EngineCheckpoint.Read(new MemoryStream(stream.ToArray())));
        Assert.Contains("more bytes than declared", exception.Message);
    }

    [Fact]
    public void RoundTrips_OrderEncoding_EveryArgumentType()
    {
        var order = new Order(3, OrderType.SpecialPower);
        order.AddIntegerArgument(-5);
        order.AddFloatArgument(1.5f);
        order.AddBooleanArgument(true);
        order.AddObjectIdArgument(new ObjectId(77));
        order.AddPositionArgument(new System.Numerics.Vector3(1f, 2f, 3f));
        order.AddScreenPositionArgument(new Point2D(4, 5));
        order.AddScreenRectangleArgument(new Rectangle(6, 7, 8, 9));

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            EngineCheckpoint.WriteOrder(writer, order);
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        var decoded = EngineCheckpoint.ReadOrder(reader);

        Assert.Equal(order.PlayerIndex, decoded.PlayerIndex);
        Assert.Equal(order.OrderType, decoded.OrderType);
        Assert.Equal(order.Arguments.Count, decoded.Arguments.Count);
        for (var i = 0; i < order.Arguments.Count; i++)
        {
            Assert.Equal(order.Arguments[i].ToString(), decoded.Arguments[i].ToString());
        }
    }

    [Fact]
    public void CaptureRandomSection_DecodesToSageRandomState()
    {
        var host = CreateHost();
        var random = (SageRandom)host.Game.GameLogic.Random;
        random.Initialize(42);
        random.Next(0, 100);

        var bytes = EngineCheckpoint.CaptureRandomSection(host);

        using var reader = new BinaryReader(new MemoryStream(bytes));
        var seed = reader.ReadUInt32();
        var words = new uint[SageRandom.StateWordCount];
        for (var i = 0; i < words.Length; i++)
        {
            words[i] = reader.ReadUInt32();
        }

        var expected = new uint[SageRandom.StateWordCount];
        random.CopyState(expected);
        Assert.Equal(42u, seed);
        Assert.Equal(expected, words);
    }

    [Fact]
    public void CaptureHostSection_DecodesFrameMapTimeAndPendingOrders()
    {
        var host = CreateHost();
        host.InjectOrders(9, [Order.CreateMoveOrder(1, new System.Numerics.Vector3(4, 5, 6))]);
        host.Run(3);

        var bytes = EngineCheckpoint.CaptureHostSection(host);
        var (frame, mapTime, pending) = DecodeHostSection(bytes);

        var expected = host.CaptureHostState();
        Assert.Equal(expected.Frame, frame);
        Assert.Equal(expected.MapTime.Ticks, mapTime);
        var (scheduledFrame, orders) = Assert.Single(pending);
        Assert.Equal(9u, scheduledFrame);
        var order = Assert.Single(orders);
        Assert.Equal("MoveTo(Position:<4, 5, 6>)", order.ToString());
    }

    [GameFact(SageGame.CncGenerals)]
    public void FullCheckpoint_OnInstalledGame_EngineSectionExcludesPresentationChunks()
    {
        using var game = BootGame(out _);
        var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
        host.Run(30);

        var sections = EngineCheckpoint.CaptureSections(host);
        var header = new EngineCheckpointHeader(host.LogicFrame.Value, 0, "run-1");
        using var stream = new MemoryStream();
        EngineCheckpoint.Write(stream, header, sections);
        stream.Position = 0;

        var data = EngineCheckpoint.Read(stream);
        Assert.Equal(header.Frame, data.Header.Frame);

        var engineSection = data.Sections[EngineCheckpointData.EngineSection];
        var unfilteredStream = new MemoryStream();
        using (var writer = new StateWriter(unfilteredStream, game))
        {
            SaveFile.Persist(writer, chunkFilter: null);
        }

        var unfiltered = unfilteredStream.ToArray();
        _output.WriteLine($"engine section {engineSection.Length} bytes, unfiltered {unfiltered.Length} bytes");
        Assert.True(engineSection.Length < unfiltered.Length, "logic-chunk engine section must be smaller than a full save");
        foreach (var presentationChunk in new[] { "TacticalView", "GameClient", "TerrainVisual", "ParticleSystem" })
        {
            Assert.False(ContainsAscii(engineSection, presentationChunk), $"engine section must not contain presentation chunk {presentationChunk}");
            Assert.True(ContainsAscii(unfiltered, presentationChunk), $"unfiltered save must contain presentation chunk {presentationChunk}");
        }

        var randomBytes = data.Sections[EngineCheckpointData.RandomSection];
        using var randomReader = new BinaryReader(new MemoryStream(randomBytes));
        Assert.Equal(((SageRandom)game.GameLogic.Random).Seed, randomReader.ReadUInt32());
    }

    private static bool ContainsAscii(byte[] haystack, string needle)
    {
        return Encoding.ASCII.GetString(haystack).Contains(needle, StringComparison.Ordinal);
    }

    private SimulationHost CreateHost()
    {
        return new SimulationHost(Generals.GameEngine, new RtsSimulationDomainModel(Generals.GameEngine));
    }

    private static (uint Frame, long MapTimeTicks, List<KeyValuePair<uint, List<Order>>> Pending) DecodeHostSection(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        var frame = reader.ReadUInt32();
        var mapTimeTicks = reader.ReadInt64();
        var pendingCount = reader.ReadInt32();
        var pending = new List<KeyValuePair<uint, List<Order>>>();
        for (var i = 0; i < pendingCount; i++)
        {
            var scheduledFrame = reader.ReadUInt32();
            var orderCount = reader.ReadInt32();
            var orders = new List<Order>();
            for (var j = 0; j < orderCount; j++)
            {
                orders.Add(EngineCheckpoint.ReadOrder(reader));
            }

            pending.Add(new(scheduledFrame, orders));
        }

        return (frame, mapTimeTicks, pending);
    }

    private static byte[] WriteMinimalCheckpoint()
    {
        using var stream = new MemoryStream();
        EngineCheckpoint.Write(
            stream,
            new EngineCheckpointHeader(1, 2, "run"),
        [
            new(EngineCheckpointData.EngineSection, [1]),
            new(EngineCheckpointData.RandomSection, [2]),
            new(EngineCheckpointData.HostSection, [3]),
        ]);
        return stream.ToArray();
    }

    private static int FindFirstSectionCompressedLengthOffset(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        reader.ReadBytes(4); // magic
        reader.ReadUInt32(); // version
        reader.ReadUInt32(); // frame
        reader.ReadInt64(); // theater time ticks
        reader.ReadString(); // run id
        reader.ReadInt32(); // section count
        reader.ReadString(); // section name
        reader.ReadInt32(); // uncompressed length
        return (int)reader.BaseStream.Position;
    }

    private Game BootGame(out List<string> log)
    {
        log = [];
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        Platform.Start();
        var game = new Game(installation, null, new Configuration
        {
            NoAudio = true,
            SkipStartingBuildings = true,
        }, null);
        var playerSettings = new PlayerSetting[]
        {
            new(0, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.Player, "USA"),
            new(1, "FactionChina", new ColorRgb(0, 255, 0), 0, PlayerOwner.EasyAi, "China"),
        };
        game.StartSkirmishOrMultiPlayerGame(
            "maps/alpine assault/alpine assault.map",
            new EchoConnection(),
            playerSettings,
            seed: 12345,
            isMultiPlayer: false);
        game.StartRun();
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        foreach (var player in game.Scene3D.Players)
        {
            player.DisableAI();
        }

        game.EnableFixedLogicTimestep();
        return game;
    }
}
