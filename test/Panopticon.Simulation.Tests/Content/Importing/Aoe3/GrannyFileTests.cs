using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Panopticon.FileFormats.Bar;
using Panopticon.FileFormats.Granny;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.Aoe3;

public sealed class GrannyFileTests
{
    private readonly ITestOutputHelper _output;

    public GrannyFileTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ParsesSyntheticFileToExpectedGraph()
    {
        using var stream = new MemoryStream(SyntheticGrannyBuilder.BuildValid());
        var file = GrannyFile.Read(stream, "synthetic.gr2");

        Assert.Equal(8, file.PointerSize);
        AssertSyntheticGraph(file);
    }

    [Fact]
    public void ParsesSynthetic32BitFileToExpectedGraph()
    {
        using var stream = new MemoryStream(SyntheticGrannyBuilder.BuildValid32Bit());
        var file = GrannyFile.Read(stream, "synthetic-32.gr2");

        Assert.Equal(4, file.PointerSize);
        AssertSyntheticGraph(file);
    }

    private static void AssertSyntheticGraph(GrannyFile file)
    {
        var root = file.Root;
        Assert.Equal("synthetic root", root.GetString("Name"));
        Assert.Equal(42, root.GetInt32("Id"));

        var coords = Assert.IsType<float[]>(root.GetArray("Coords"));
        Assert.Equal(new[] { 1.5f, -2.5f, 3.5f }, coords);

        var placement = root.GetTransform("Placement");
        Assert.NotNull(placement);
        Assert.Equal(7u, placement!.Value.Flags);
        Assert.Equal(new[] { 1f, 2f, 3f }, placement.Value.Translation);
        Assert.Equal(0.70710678f, placement.Value.Rotation[3], 5);

        var child = root.GetObject("Child");
        Assert.NotNull(child);
        Assert.Equal("first", child!.GetString("Label"));
        Assert.Equal(new[] { 10, 20, 30 }, Assert.IsType<int[]>(child.GetArray("Values")));

        var children = root.GetObjects("Children");
        Assert.Equal(2, children.Count);
        Assert.Equal("first", children[0].GetString("Label"));
        Assert.Equal("second", children[1].GetString("Label"));
        Assert.Equal(60, Assert.IsType<int[]>(children[1].GetArray("Values"))[2]);

        var tags = root.GetObjects("Tags");
        Assert.Equal(2, tags.Count);
        Assert.Equal("alpha", tags[0].GetString("Text"));
        Assert.Equal("beta", tags[1].GetString("Text"));

        var payload = root.GetObject("Payload");
        Assert.NotNull(payload);
        Assert.Equal(77, payload!.GetInt32("Value"));

        // Curve user types expose their header fields and stay opaque; the
        // body deliberately cannot be walked with the definition's shape.
        var curve = root.GetObject("Curve");
        Assert.NotNull(curve);
        Assert.Equal(3u, curve!.GetUInt32("Format").GetValueOrDefault());
        Assert.Equal(0u, curve.GetUInt32("Degree").GetValueOrDefault());
        Assert.Equal("Test", curve.GetString("CurveDataKind"));

        // The curve body stays opaque, so its nested definitions are never resolved.
        Assert.Equal(5, file.StructDefinitionCount);
        Assert.Single(file.Sections);
    }

    [Fact]
    public void RejectsBadSignature()
    {
        using var stream = new MemoryStream(SyntheticGrannyBuilder.BuildWithBadSignature());

        var exception = Assert.Throws<InvalidDataException>(() => GrannyFile.Read(stream));
        Assert.Contains("signature", exception.Message);
    }

    [Fact]
    public void RejectsUnsupportedVersion()
    {
        using var stream = new MemoryStream(SyntheticGrannyBuilder.BuildWithVersion(6));

        var exception = Assert.Throws<InvalidDataException>(() => GrannyFile.Read(stream));
        Assert.Contains("version 6", exception.Message);
    }

    [Fact]
    public void RejectsBadHeaderFormat()
    {
        using var stream = new MemoryStream(SyntheticGrannyBuilder.BuildWithHeaderFormat(1));

        var exception = Assert.Throws<InvalidDataException>(() => GrannyFile.Read(stream));
        Assert.Contains("header format 1", exception.Message);
    }

    [Fact]
    public void UnsupportedCompressionThrowsWithType()
    {
        using var stream = new MemoryStream(SyntheticGrannyBuilder.BuildWithCompressedSection(99));

        var exception = Assert.Throws<GrannyCompressedSectionException>(() => GrannyFile.Read(stream, "unit.gr2"));
        Assert.Equal(0, exception.SectionIndex);
        Assert.Equal(99u, exception.Compression);
        Assert.Equal("unit.gr2", exception.FilePath);
        Assert.Contains("compression 99", exception.Message);
    }

    [Theory]
    [InlineData(0x70, uint.MaxValue, "2 GiB")]
    [InlineData(0x74, uint.MaxValue, "2 GiB")]
    [InlineData(0x84, uint.MaxValue, "relocation")]
    [InlineData(0x88, uint.MaxValue, "relocation")]
    [InlineData(0x8c, uint.MaxValue, "marshalling")]
    [InlineData(0x90, uint.MaxValue, "marshalling")]
    public void RejectsInvalidSectionRanges(int fieldOffset, uint value, string expected)
    {
        var bytes = SyntheticGrannyBuilder.BuildValid();
        if (fieldOffset == 0x8c)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x90), 1);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(fieldOffset), value);
        using var stream = new MemoryStream(bytes);

        var exception = Assert.Throws<InvalidDataException>(() => GrannyFile.Read(stream, "bad-range.gr2"));

        Assert.Contains("bad-range.gr2", exception.Message);
        Assert.Contains(expected, exception.Message);
    }

    [Fact]
    public void RejectsReversedOodleDictionaryBoundaries()
    {
        var bytes = SyntheticGrannyBuilder.BuildWithCompressedSection(2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x7c), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x80), 1);
        using var stream = new MemoryStream(bytes);

        var exception = Assert.Throws<InvalidDataException>(() => GrannyFile.Read(stream, "bad-stops.gr2"));

        Assert.Contains("bad-stops.gr2", exception.Message);
        Assert.Contains("section 0", exception.Message);
        Assert.Contains("dictionary boundaries", exception.Message);
    }

    [Theory]
    [InlineData(2u)]
    [InlineData(4u)]
    public void CorruptCompressedPayloadRetainsFileAndSectionContext(uint compression)
    {
        var bytes = SyntheticGrannyBuilder.BuildWithCompressedSection(compression);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x70), 1);
        using var stream = new MemoryStream(bytes);

        var exception = Assert.Throws<InvalidDataException>(() => GrannyFile.Read(stream, "bad-codec.gr2"));

        Assert.Contains("bad-codec.gr2", exception.Message);
        Assert.Contains("section 0", exception.Message);
        Assert.Contains($"compression {compression}", exception.Message);
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void MusketeerRootExposesNamedFields()
    {
        var fileSystem = Aoe3Install.CreateFileSystem();
        var entry = fileSystem.GetFile("Art\\units\\infantry_ranged\\musketeer\\musketeer_4age.gr2");
        Assert.NotNull(entry);

        using var stream = entry!.Open();
        var file = GrannyFile.Read(stream, entry.FilePath);

        var root = file.Root;
        Assert.True(root.HasField("ArtToolInfo"));
        Assert.True(root.HasField("Skeletons"));
        Assert.True(root.HasField("Meshes"));
        Assert.True(root.HasField("Models"));

        Assert.Equal(1.5625f, root.GetObject("ArtToolInfo")!.GetSingle("UnitsPerMeter"));

        var bones = root.GetObjects("Skeletons")[0].GetObjects("Bones");
        Assert.Equal(23, bones.Count);
        Assert.NotNull(bones[0].GetInt32("ParentIndex"));

        var mesh = root.GetObjects("Meshes")[0];
        Assert.Equal("musketeer_4ageShape", mesh.GetString("Name"));
        Assert.Equal(1197, mesh.GetObject("PrimaryVertexData")!.GetObjects("Vertices").Count);

        var groups = mesh.GetObject("PrimaryTopology")!.GetObjects("Groups");
        Assert.Single(groups);
        Assert.Equal(1344, groups[0].GetInt32("TriCount"));

        Assert.Single(root.GetObjects("Models"));
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void ArtUnitsCorpusParsesAllFiles()
    {
        var gameDirectory = Aoe3Install.GetGameDirectory();
        var barPath = Path.Combine(gameDirectory, "Art", "ArtUnits.bar");
        using var barStream = File.OpenRead(barPath);
        var archive = BarArchive.Read(barStream);

        long parsed = 0;
        long compressed = 0;
        var failures = new List<string>();
        foreach (var barEntry in archive.Entries)
        {
            if (!barEntry.FullName.EndsWith(".gr2", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var stream = barEntry.Open();
                _ = GrannyFile.Read(stream, barEntry.FullName);
                parsed++;
            }
            catch (GrannyCompressedSectionException)
            {
                compressed++;
            }
            catch (Exception exception)
            {
                failures.Add($"{barEntry.FullName}: {exception.Message}");
            }
        }

        _output.WriteLine($"ArtUnits.bar GR2 corpus: {parsed} parsed, {compressed} compressed, "
            + $"{failures.Count} failed, of {parsed + compressed + failures.Count} total");

        Assert.True(parsed > 4900, $"expected the complete model corpus, got {parsed}");
        Assert.Equal(0, compressed);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures[..Math.Min(5, failures.Count)]));
    }
}
