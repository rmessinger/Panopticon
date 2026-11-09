using System;
using System.IO;
using System.Numerics;
using Panopticon.Data.Rep;
using Panopticon.Logic.Orders;
using Xunit;

namespace Panopticon.Tests.Data.Rep;

public class ReplayChunkPositionTests
{
    [Fact]
    public void PositionArgument_WritesLegacyUnitsAndParsesMetric()
    {
        var order = new Order(0, OrderType.ForceAttackGround);
        order.AddPositionArgument(new Vector3(30.48f, 60.96f, 0f));

        byte[] bytes;
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream))
            {
                ReplayChunk.Write(writer, 0, order);
            }

            bytes = stream.ToArray();
        }

        // Chunk header (12 bytes), argument-type count (1 byte), one (type, count) pair (2 bytes).
        const int positionOffset = 15;
        Assert.Equal(100.0f, BitConverter.ToSingle(bytes, positionOffset), precision: 3);
        Assert.Equal(200.0f, BitConverter.ToSingle(bytes, positionOffset + 4), precision: 3);
        Assert.Equal(0.0f, BitConverter.ToSingle(bytes, positionOffset + 8), precision: 3);

        using var readStream = new MemoryStream(bytes);
        using var reader = new BinaryReader(readStream);
        var chunk = ReplayChunk.Parse(reader);

        var position = chunk.Order.Arguments[0].Value.Position;
        Assert.Equal(30.48f, position.X, precision: 3);
        Assert.Equal(60.96f, position.Y, precision: 3);
        Assert.Equal(0.0f, position.Z, precision: 3);
    }
}
