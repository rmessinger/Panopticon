using System;
using System.IO;
using System.Numerics;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Data.Rep;
using Panopticon.Logic.Orders;
using Xunit;

namespace Panopticon.Tests.Data.Rep;

public class ReplayChunkByteIdentityTests
{
    private const int PositionOffset = 15;

    private static readonly float MetersPerLegacyUnit = (float)UnitConversionPolicy.GeneralsFeetToMeters;

    [Fact]
    public void ParsedChunk_WithIrregularPositions_WritesSourceBytes()
    {
        var input = BuildChunk(writer =>
        {
            writer.Write((byte)1);
            writer.Write((byte)OrderArgumentType.Position);
            writer.Write((byte)2);
            WriteVector3(writer, new Vector3(123.456f, -987.654f, 0.1f));
            WriteVector3(writer, new Vector3(-0.3f, 2.5f, 7.123f));
        });

        var chunk = ParseChunk(input);
        var output = WriteChunk(chunk.Header.Timecode, chunk.Order);

        Assert.Equal(input, output);
    }

    [Fact]
    public void ParsedChunk_WithMixedArguments_RoundTripsBytes()
    {
        var input = BuildChunk(writer =>
        {
            writer.Write((byte)7);
            writer.Write((byte)OrderArgumentType.Integer);
            writer.Write((byte)1);
            writer.Write((byte)OrderArgumentType.Float);
            writer.Write((byte)1);
            writer.Write((byte)OrderArgumentType.Boolean);
            writer.Write((byte)1);
            writer.Write((byte)OrderArgumentType.ObjectId);
            writer.Write((byte)1);
            writer.Write((byte)OrderArgumentType.ScreenPosition);
            writer.Write((byte)1);
            writer.Write((byte)OrderArgumentType.ScreenRectangle);
            writer.Write((byte)1);
            writer.Write((byte)OrderArgumentType.Position);
            writer.Write((byte)2);

            writer.Write(-42);
            writer.Write(0.1f);
            writer.Write(true);
            writer.Write(17u);
            writer.Write(10);
            writer.Write(20);
            writer.Write(1);
            writer.Write(2);
            writer.Write(3);
            writer.Write(4);
            WriteVector3(writer, new Vector3(123.456f, -987.654f, 0.1f));
            WriteVector3(writer, new Vector3(-0.3f, 2.5f, 7.123f));
        });

        var chunk = ParseChunk(input);
        var output = WriteChunk(chunk.Header.Timecode, chunk.Order);

        Assert.Equal(input, output);
    }

    [Fact]
    public void ParsedPosition_ChangedAtRuntime_WritesLegacyUnits()
    {
        var input = BuildChunk(writer =>
        {
            writer.Write((byte)1);
            writer.Write((byte)OrderArgumentType.Position);
            writer.Write((byte)1);
            WriteVector3(writer, new Vector3(123.456f, -987.654f, 0.1f));
        });

        var chunk = ParseChunk(input);
        var changed = new Vector3(30.48f, 60.96f, 0f);
        var edited = new Order(chunk.Order.PlayerIndex, chunk.Order.OrderType);
        edited.AddPositionArgument(changed);

        var output = WriteChunk(chunk.Header.Timecode, edited);

        Assert.Equal(input.Length, output.Length);
        Assert.Equal(input[..PositionOffset], output[..PositionOffset]);
        Assert.Equal(changed.X / MetersPerLegacyUnit, BitConverter.ToSingle(output, PositionOffset));
        Assert.Equal(changed.Y / MetersPerLegacyUnit, BitConverter.ToSingle(output, PositionOffset + 4));
        Assert.Equal(changed.Z / MetersPerLegacyUnit, BitConverter.ToSingle(output, PositionOffset + 8));
    }

    [Fact]
    public void RuntimePosition_WritesLegacyUnits()
    {
        var order = new Order(0, OrderType.ForceAttackGround);
        var position = new Vector3(123.456f, -987.654f, 0.1f);
        order.AddPositionArgument(position);

        var output = WriteChunk(0, order);

        Assert.Equal(position.X / MetersPerLegacyUnit, BitConverter.ToSingle(output, PositionOffset));
        Assert.Equal(position.Y / MetersPerLegacyUnit, BitConverter.ToSingle(output, PositionOffset + 4));
        Assert.Equal(position.Z / MetersPerLegacyUnit, BitConverter.ToSingle(output, PositionOffset + 8));
    }

    private static byte[] BuildChunk(Action<BinaryWriter> writeArguments)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(7u);
            writer.Write((uint)OrderType.ForceAttackGround);
            writer.Write(1u);
            writeArguments(writer);
        }

        return stream.ToArray();
    }

    private static void WriteVector3(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static ReplayChunk ParseChunk(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        return ReplayChunk.Parse(reader);
    }

    private static byte[] WriteChunk(uint timecode, Order order)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            ReplayChunk.Write(writer, timecode, order);
        }

        return stream.ToArray();
    }
}
