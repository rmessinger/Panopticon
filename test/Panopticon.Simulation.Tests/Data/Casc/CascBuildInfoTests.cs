using System;
using System.IO;
using Panopticon.FileFormats.Casc;
using Xunit;

namespace Panopticon.Tests.Data.Casc;

public sealed class CascBuildInfoTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "panopticon-casc-build-" + Guid.NewGuid().ToString("N"));

    public CascBuildInfoTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void ReadsActiveBuildInfoAndConfig()
    {
        var buildKey = "00112233445566778899aabbccddeeff";
        WriteBuildInfo(buildKey);
        WriteConfig(buildKey);

        var info = CascBuildInfo.Read(_tempDirectory);

        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", info.RootContentKey.ToString());
        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", info.EncodingContentKey.ToString());
        Assert.Equal("cccccccccccccccccccccccccccccccc", info.EncodingEncodingKey.ToString());
        Assert.Equal("s2", info.BuildUid);
    }

    [Fact]
    public void MissingBuildInfoFailsWithFileNotFound()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => CascBuildInfo.Read(_tempDirectory));
        Assert.EndsWith(".build.info", ex.FileName);
    }

    [Fact]
    public void MissingActiveRowFailsWithFileNotFound()
    {
        File.WriteAllText(
            Path.Combine(_tempDirectory, ".build.info"),
            "Active!DEC:1|Build Key!HEX:16\n0|00112233445566778899aabbccddeeff\n");

        var ex = Assert.Throws<FileNotFoundException>(() => CascBuildInfo.Read(_tempDirectory));
        Assert.Contains("active", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private void WriteBuildInfo(string buildKey)
    {
        File.WriteAllText(
            Path.Combine(_tempDirectory, ".build.info"),
            "Active!DEC:1|Build Key!HEX:16|Version!STRING:0\n" +
            "0|ffffffffffffffffffffffffffffffff|old\n" +
            $"1|{buildKey}|current\n");
    }

    private void WriteConfig(string buildKey)
    {
        var configDirectory = Path.Combine(_tempDirectory, "SC2Data", "config", buildKey[..2], buildKey[2..4]);
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(
            Path.Combine(configDirectory, buildKey),
            "root = aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n" +
            "encoding = bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb cccccccccccccccccccccccccccccccc\n" +
            "build-uid = s2\n");
    }

    public void Dispose()
    {
        Directory.Delete(_tempDirectory, recursive: true);
    }
}
