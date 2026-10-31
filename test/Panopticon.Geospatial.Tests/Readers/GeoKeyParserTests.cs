#nullable enable

using Panopticon.Geospatial;
using Panopticon.Geospatial.Readers;
using Xunit;

namespace Panopticon.Geospatial.Tests.Readers;

public class GeoKeyParserTests
{
    private const ushort ShortParamsTag = 34735;

    private const ushort DoubleParamsTag = 34736;

    private const ushort AsciiParamsTag = 34737;

    // GeoTIFF private-use key ids run 32768..65535; unknown keys are validated, never interpreted.
    private const ushort PrivateKey = 32790;

    private const ushort PrimeMeridianKey = 2051;

    private const ushort PcsCitationKey = 3071;

    public static readonly TheoryData<int, double> ScalarKeysAsFractionalDoubles = new()
    {
        { 1024, 1.9 },
        { 1025, 2.9 },
        { 2048, 4326.9 },
        { 2052, 9001.9 },
        { 2054, 9102.5 },
        { 3072, 32631.9 },
        { 3076, 9002.9 },
        { 4096, 5773.5 },
        { 4099, 9001.25 },
    };

    private static (GeoKeySet? Keys, GeoDiagnosticBag Diagnostics) ParseDirectory(
        ushort[]? directory,
        double[]? doubleParams = null,
        int asciiParamsLength = 0)
    {
        var diagnostics = new GeoDiagnosticBag();

        return (GeoKeyParser.TryParse(directory, doubleParams, asciiParamsLength, diagnostics, "raster.tif"), diagnostics);
    }

    private static GeoDiagnostic SingleParseError(GeoDiagnosticBag diagnostics)
    {
        var error = Assert.Single(diagnostics.Items, static item => item.Severity == GeoSeverity.Error);
        Assert.Equal(GeoDiagnosticCodes.ParseFailure, error.Code);
        Assert.Equal(GeoStage.Parse, error.Stage);

        return error;
    }

    /// <summary>GeoTIFF 1.0 header (version 1, revision 1, minor 0) followed by four-short entries verbatim.</summary>
    private static ushort[] Directory(int declaredCount, params ushort[] flatEntries)
    {
        return DirectoryWithHeader(1, 1, 0, declaredCount, flatEntries);
    }

    private static ushort[] DirectoryWithHeader(
        int version,
        int revision,
        int minor,
        int declaredCount,
        params ushort[] flatEntries)
    {
        var directory = new ushort[4 + flatEntries.Length];
        directory[0] = (ushort)version;
        directory[1] = (ushort)revision;
        directory[2] = (ushort)minor;
        directory[3] = checked((ushort)declaredCount);
        flatEntries.CopyTo(directory, 4);

        return directory;
    }

    /// <summary>Flattens key id and inline value pairs into (id, 0, 1, value) entries.</summary>
    private static ushort[] FlatKeys(params (ushort Id, ushort Value)[] keys)
    {
        var flat = new ushort[keys.Length * 4];

        for (var index = 0; index < keys.Length; index++)
        {
            flat[index * 4] = keys[index].Id;
            flat[index * 4 + 1] = 0;
            flat[index * 4 + 2] = 1;
            flat[index * 4 + 3] = keys[index].Value;
        }

        return flat;
    }

    [Fact]
    public void UnitKeyConstants_MatchTheGeoTiffSpecification()
    {
        // 2051 is the prime meridian key and 3071 the PCS citation key, not unit keys.
        Assert.Equal(2052, (int)GeoKeyParser.GeogLinearUnitsKey);
        Assert.Equal(3076, (int)GeoKeyParser.ProjectedLinearUnitsKey);
        Assert.Equal(4099, (int)GeoKeyParser.VerticalUnitsKey);
        Assert.Equal(34735, (int)GeoKeyParser.GeoKeyDirectoryTag);
    }

    [Fact]
    public void AbsentDirectory_ReturnsNullWithoutDiagnostics()
    {
        var (keys, diagnostics) = ParseDirectory(null);

        Assert.Null(keys);
        Assert.Empty(diagnostics.Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Header_GeoTiff10And11BothParse(int minorRevision)
    {
        var (keys, diagnostics) = ParseDirectory(
            DirectoryWithHeader(1, 1, minorRevision, 2, FlatKeys((1024, 2), (2048, 4326))));

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(keys);
        Assert.True(keys!.Contains(GeoKeyParser.GeographicTypeKey));
        Assert.True(keys.TryGetInteger(GeoKeyParser.GeographicTypeKey, out var code));
        Assert.Equal(4326, code);
    }

    [Theory]
    [InlineData(2, 1, 0)]
    [InlineData(1, 2, 0)]
    [InlineData(1, 1, 2)]
    public void Header_UnknownVersionRevisionOrMinorRevisionIsRejected(int version, int revision, int minor)
    {
        var (keys, diagnostics) = ParseDirectory(
            DirectoryWithHeader(version, revision, minor, 1, FlatKeys((1024, 2))));

        Assert.Null(keys);
        Assert.NotNull(SingleParseError(diagnostics));
    }

    [Fact]
    public void Header_DirectoryShorterThanFourShortsIsRejected()
    {
        var (keys, diagnostics) = ParseDirectory([1, 1, 0]);

        Assert.Null(keys);
        Assert.Contains("header", SingleParseError(diagnostics).Message);
    }

    [Fact]
    public void DirectoryWithNoKeys_ParsesToAnEmptySet()
    {
        var (keys, diagnostics) = ParseDirectory(Directory(0));

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(keys);
        Assert.False(keys!.Contains(GeoKeyParser.ModelTypeKey));
    }

    [Fact]
    public void DeclaredCountBeyondCarriedEntries_IsRejected()
    {
        var (keys, diagnostics) = ParseDirectory(Directory(3, FlatKeys((1024, 2))));

        Assert.Null(keys);
        Assert.Contains("declares 3 keys but carries 1", SingleParseError(diagnostics).Message);
    }

    [Fact]
    public void DuplicateGeoKey_IsRejected()
    {
        var (keys, diagnostics) = ParseDirectory(
            Directory(2, 1024, 0, 1, 2, 1024, 0, 1, 1));

        Assert.Null(keys);
        Assert.Contains("appears more than once", SingleParseError(diagnostics).Message);
    }

    [Fact]
    public void KeyIdZero_IsRejected()
    {
        var (keys, diagnostics) = ParseDirectory(Directory(1, 0, 0, 1, 5));

        Assert.Null(keys);
        Assert.Contains("key id 0", SingleParseError(diagnostics).Message);
    }

    [Fact]
    public void InlineEntryWithCountZero_IsRejected()
    {
        var (keys, diagnostics) = ParseDirectory(Directory(1, PrivateKey, 0, 0, 5));

        Assert.Null(keys);
        Assert.Contains("zero values", SingleParseError(diagnostics).Message);
    }

    [Fact]
    public void InlineEntryWithCountAboveOne_IsRejected()
    {
        var (keys, diagnostics) = ParseDirectory(Directory(1, PrivateKey, 0, 2, 5));

        Assert.Null(keys);
        Assert.Contains("count 2, not 1", SingleParseError(diagnostics).Message);
    }

    [Fact]
    public void ReferencedEntryWithCountZero_IsRejected()
    {
        var (keys, diagnostics) = ParseDirectory(Directory(1, PrivateKey, DoubleParamsTag, 0, 0), [1d]);

        Assert.Null(keys);
        Assert.Contains("zero values", SingleParseError(diagnostics).Message);
    }

    [Fact]
    public void TrailingShortParams_AreLegalStorageIndexedFromTheTagStart()
    {
        // Three entries end at short 16; the private key reads the two trailing shorts at absolute offsets 16 and 17.
        var (keys, diagnostics) = ParseDirectory(
            Directory(3, 1024, 0, 1, 2, 2048, 0, 1, 4326, PrivateKey, ShortParamsTag, 2, 16, 10, 20));

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(keys);
        Assert.True(keys!.Contains(PrivateKey));
        Assert.False(keys.TryGetInteger(PrivateKey, out _));
    }

    [Fact]
    public void UnreferencedTrailingShorts_AreAcceptedAsStorage()
    {
        var (keys, diagnostics) = ParseDirectory(Directory(1, 1024, 0, 1, 2, 7, 8, 9));

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(keys);
        Assert.True(keys!.Contains(GeoKeyParser.ModelTypeKey));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void ShortParamsReference_InsideHeaderOrEntriesIsRejected(int offset)
    {
        // With one declared entry the header spans shorts 0..3 and the entry 4..7.
        var (keys, diagnostics) = ParseDirectory(Directory(1, PrivateKey, ShortParamsTag, 1, (ushort)offset, 9001));

        Assert.Null(keys);
        Assert.Contains("header or key entries", SingleParseError(diagnostics).Message);
    }

    [Theory]
    [InlineData(2, 16)]
    [InlineData(1, 8)]
    public void ShortParamsReference_BeyondTheTagIsRejected(int count, int offset)
    {
        var (keys, diagnostics) = ParseDirectory(Directory(1, PrivateKey, ShortParamsTag, (ushort)count, (ushort)offset));

        Assert.Null(keys);
        Assert.Contains("short params", SingleParseError(diagnostics).Message);
    }

    [Theory]
    [InlineData(33550)]
    [InlineData(34734)]
    public void WrongTiffTagLocation_IsRejectedNotIgnored(int location)
    {
        var (keys, diagnostics) = ParseDirectory(Directory(1, PrivateKey, (ushort)location, 1, 0));

        Assert.Null(keys);
        Assert.Contains("34735, 34736, and 34737", SingleParseError(diagnostics).Message);
    }

    [Fact]
    public void DoubleParamsReference_WithoutTheTagIsRejected()
    {
        var (keys, diagnostics) = ParseDirectory(Directory(1, PrivateKey, DoubleParamsTag, 1, 0));

        Assert.Null(keys);
        Assert.Contains("absent", SingleParseError(diagnostics).Message);
    }

    [Fact]
    public void DoubleParamsReference_BeyondTheTagIsRejected()
    {
        var (keys, diagnostics) = ParseDirectory(Directory(1, PrivateKey, DoubleParamsTag, 1, 99), [1d]);

        Assert.Null(keys);
        Assert.Contains("double params", SingleParseError(diagnostics).Message);
    }

    [Theory]
    [InlineData(5, 0, 5, true)]
    [InlineData(6, 0, 5, false)]
    [InlineData(3, 3, 5, false)]
    public void AsciiParamsReferences_AreValidatedAgainstBounds(int count, int offset, int asciiLength, bool valid)
    {
        var (keys, diagnostics) = ParseDirectory(
            Directory(1, PrivateKey, AsciiParamsTag, (ushort)count, (ushort)offset),
            asciiParamsLength: asciiLength);

        Assert.Equal(valid, keys is not null);

        if (!valid)
        {
            Assert.Contains("ASCII params", SingleParseError(diagnostics).Message);
        }
    }

    [Theory]
    [MemberData(nameof(ScalarKeysAsFractionalDoubles))]
    public void ScalarKeysStoredAsFractionalDoubles_AreRejectedNotTruncated(int keyId, double value)
    {
        var (keys, diagnostics) = ParseDirectory(Directory(1, (ushort)keyId, DoubleParamsTag, 1, 0), [value]);

        Assert.Null(keys);
        var error = SingleParseError(diagnostics);
        Assert.Contains(keyId.ToString(), error.Message);
        Assert.Contains("inline", error.Message);
    }

    [Theory]
    [InlineData(1025)]
    [InlineData(2048)]
    [InlineData(3076)]
    public void ScalarKeysStoredInAsciiParams_AreRejected(int keyId)
    {
        var (keys, diagnostics) = ParseDirectory(
            Directory(1, (ushort)keyId, AsciiParamsTag, 1, 0),
            asciiParamsLength: 8);

        Assert.Null(keys);
        Assert.Contains("inline", SingleParseError(diagnostics).Message);
    }

    [Fact]
    public void ScalarKeyStoredInShortParams_IsRejectedDespiteBoundedReference()
    {
        // Offset 8 sits just past this single entry, so only the scalar encoding rule rejects 4099.
        var (keys, diagnostics) = ParseDirectory(Directory(1, 4099, ShortParamsTag, 1, 8, 9001));

        Assert.Null(keys);
        Assert.Contains("4099", SingleParseError(diagnostics).Message);
    }

    [Fact]
    public void GreenwichPrimeMeridianKey2051_IsNotTreatedAsLinearUnits()
    {
        var (keys, diagnostics) = ParseDirectory(
            Directory(2, FlatKeys((GeoKeyParser.GeographicTypeKey, 4326), (PrimeMeridianKey, 8901))));

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(keys);
        Assert.True(keys!.Contains(PrimeMeridianKey));
        Assert.True(keys.TryGetInteger(PrimeMeridianKey, out var primeMeridian));
        Assert.Equal(8901, primeMeridian);
        Assert.False(keys.Contains(GeoKeyParser.GeogLinearUnitsKey));
        Assert.False(keys.TryGetInteger(GeoKeyParser.GeogLinearUnitsKey, out _));
    }

    [Fact]
    public void ProjectedLinearUnitsFootCode_ReadsThroughKey3076()
    {
        var (keys, diagnostics) = ParseDirectory(Directory(1, FlatKeys((GeoKeyParser.ProjectedLinearUnitsKey, 9002))));

        Assert.False(diagnostics.HasErrors);
        Assert.True(keys!.TryGetInteger(GeoKeyParser.ProjectedLinearUnitsKey, out var code));
        Assert.Equal(9002, code);
    }

    [Fact]
    public void CitationKey3071CarryingAUnitCode_IsNotProjectedLinearUnits()
    {
        var (keys, diagnostics) = ParseDirectory(Directory(1, FlatKeys((PcsCitationKey, 9002))));

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(keys);
        Assert.True(keys!.Contains(PcsCitationKey));
        Assert.False(keys.Contains(GeoKeyParser.ProjectedLinearUnitsKey));
        Assert.False(keys.TryGetInteger(GeoKeyParser.ProjectedLinearUnitsKey, out _));
    }

    [Fact]
    public void TryGetInteger_MissingKeyReturnsFalse()
    {
        var (keys, _) = ParseDirectory(Directory(1, FlatKeys((GeoKeyParser.ModelTypeKey, 1))));

        Assert.False(keys!.TryGetInteger(GeoKeyParser.RasterTypeKey, out var value));
        Assert.Equal(0, value);
    }

    [Fact]
    public void TryGetInteger_DoubleStoredUnknownKeyReturnsFalseWithoutTruncation()
    {
        var (keys, diagnostics) = ParseDirectory(Directory(1, PrivateKey, DoubleParamsTag, 1, 0), [2.9]);

        Assert.False(diagnostics.HasErrors);
        Assert.True(keys!.Contains(PrivateKey));
        Assert.False(keys.TryGetInteger(PrivateKey, out var value));
        Assert.Equal(0, value);
    }
}
