using System;
using System.IO;
using Panopticon.Recording;
using Xunit;

namespace Panopticon.Tests.Recording;

public sealed class EngineCheckpointVersionTests
{
    private const int VersionOffset = 4;

    [Fact]
    public void WriteThenRead_RoundTripsAtVersionTwo()
    {
        var bytes = WriteMinimalCheckpoint();
        Assert.Equal(2u, BitConverter.ToUInt32(bytes, VersionOffset));

        var data = EngineCheckpoint.Read(new MemoryStream(bytes));

        Assert.Equal(new EngineCheckpointHeader(1, 2, "run"), data.Header);
    }

    [Fact]
    public void Read_RejectsPreMetricVersionOne_WithRegenerateGuidance()
    {
        var bytes = WriteMinimalCheckpoint();
        bytes[VersionOffset] = 1;

        var error = Assert.Throws<InvalidDataException>(() => EngineCheckpoint.Read(new MemoryStream(bytes)));
        Assert.Contains("predates the metric format", error.Message);
        Assert.Contains("Pre-metric checkpoints are unsupported", error.Message);
        Assert.Contains("regenerate", error.Message);
    }

    [Fact]
    public void Read_RejectsVersionAboveCurrent()
    {
        var bytes = WriteMinimalCheckpoint();
        bytes[VersionOffset] = 3;

        var error = Assert.Throws<InvalidDataException>(() => EngineCheckpoint.Read(new MemoryStream(bytes)));
        Assert.Equal("Unsupported engine checkpoint format version 3.", error.Message);
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
}
