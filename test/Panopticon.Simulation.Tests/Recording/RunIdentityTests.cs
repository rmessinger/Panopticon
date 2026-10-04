using System;
using System.Collections.Generic;
using System.IO;
using Panopticon.Recording;
using Xunit;

namespace Panopticon.Tests.Recording;

public sealed class RunIdentityTests
{
    private static RunIdentity Synthetic(
        uint seed = 11,
        string shardLayoutId = "single",
        string runtimeDescription = ".NET 10.0.0",
        IReadOnlyList<ModuleVersion>? modules = null,
        IReadOnlyList<InputHash>? inputs = null,
        IReadOnlyList<ArchiveHash>? archives = null,
        bool avx2Supported = true)
    {
        return new RunIdentity(
            modules ?? [new ModuleVersion("Panopticon.Simulation", "1.0.0")],
            runtimeDescription,
            "X64",
            "X64",
            vector512Accelerated: false,
            avx2Supported: avx2Supported,
            advSimdSupported: false,
            shardLayoutId,
            seed,
            inputs ?? [new InputHash("scenario", 0x1122334455667788UL)],
            archives ?? [new ArchiveHash("INI.big", 12_345, 0xAABBCCDD00112233UL)]);
    }

    [Fact]
    public void IdenticalInputs_ProduceEqualIdentities_AndStableHash()
    {
        var a = Synthetic();
        var b = Synthetic();

        Assert.True(a.Matches(b));
        Assert.True(b.Matches(a));
        Assert.Empty(a.Differences(b));
        Assert.Equal(a.StableHash(), b.StableHash());
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Matches("^[0-9A-F]{16}$", a.StableHash());
    }

    [Theory]
    [InlineData(11u, 12u)]
    public void SeedChange_IsReportedAsMismatch(uint mine, uint theirs)
    {
        var differences = Synthetic(seed: mine).Differences(Synthetic(seed: theirs));

        var difference = Assert.Single(differences);
        Assert.Contains("Seed", difference);
    }

    [Fact]
    public void ShardLayoutChange_IsReportedAsMismatch()
    {
        var differences = Synthetic(shardLayoutId: "single").Differences(Synthetic(shardLayoutId: "sharded-4"));

        var difference = Assert.Single(differences);
        Assert.Contains("ShardLayoutId", difference);
    }

    [Fact]
    public void RuntimeFieldChange_IsReportedAsMismatch()
    {
        var differences = Synthetic(runtimeDescription: ".NET 10.0.0").Differences(Synthetic(runtimeDescription: ".NET 11.0.0"));

        var difference = Assert.Single(differences);
        Assert.Contains("RuntimeDescription", difference);
    }

    [Fact]
    public void ArchiveHashChange_IsReportedAsMismatch()
    {
        var mine = Synthetic(archives: [new ArchiveHash("INI.big", 12_345, 0xAABBCCDD00112233UL)]);
        var theirs = Synthetic(archives: [new ArchiveHash("INI.big", 12_346, 0x0000000000000001UL)]);

        var difference = Assert.Single(mine.Differences(theirs));
        Assert.Contains("Archive INI.big", difference);
    }

    [Fact]
    public void ArchiveMissingOnOneSide_IsReportedAsMismatch()
    {
        var mine = Synthetic(archives: [new ArchiveHash("INI.big", 12_345, 0xAABBCCDD00112233UL)]);
        var theirs = Synthetic(archives:
        [
            new ArchiveHash("INI.big", 12_345, 0xAABBCCDD00112233UL),
            new ArchiveHash("W3D.big", 999, 0x1UL),
        ]);

        var difference = Assert.Single(mine.Differences(theirs));
        Assert.Contains("W3D.big", difference);
        Assert.Contains("missing on one side", difference);
    }

    [Fact]
    public void ModuleVersionChange_IsReportedAsMismatch()
    {
        var mine = Synthetic(modules: [new ModuleVersion("Panopticon.Simulation", "1.0.0")]);
        var theirs = Synthetic(modules: [new ModuleVersion("Panopticon.Simulation", "1.1.0")]);

        var difference = Assert.Single(mine.Differences(theirs));
        Assert.Contains("Module Panopticon.Simulation", difference);
    }

    [Fact]
    public void StableHash_DistinguishesEveryField()
    {
        var baseline = Synthetic();
        var variants = new[]
        {
            Synthetic(seed: 12),
            Synthetic(shardLayoutId: "sharded-4"),
            Synthetic(runtimeDescription: "other"),
            Synthetic(modules: [new ModuleVersion("Panopticon.Simulation", "2.0.0")]),
            Synthetic(inputs: [new InputHash("scenario", 1)]),
            Synthetic(archives: [new ArchiveHash("INI.big", 1, 1)]),
            Synthetic(avx2Supported: false),
        };

        foreach (var variant in variants)
        {
            Assert.NotEqual(baseline.StableHash(), variant.StableHash());
        }
    }

    [Fact]
    public void Capture_FillsRuntimeAndModuleFields()
    {
        var identity = RunIdentity.Capture(
            seed: 1234,
            shardLayoutId: "single",
            inputs: [new InputContent("scenario", [1, 2, 3])],
            archiveFilePaths: []);

        Assert.NotEmpty(identity.RuntimeDescription);
        Assert.NotEmpty(identity.OSArchitecture);
        Assert.NotEmpty(identity.ProcessArchitecture);
        Assert.Contains(identity.Modules, m => m.Name == "Panopticon.Simulation");
        Assert.DoesNotContain(identity.Modules, m => m.Name.Contains(".Tests"));
        var input = Assert.Single(identity.Inputs);
        Assert.Equal("scenario", input.Name);
        Assert.NotEqual(0UL, input.Hash);
        Assert.Empty(identity.Archives);
        Assert.Matches("^[0-9A-F]{16}$", identity.StableHash());
    }

    [Fact]
    public void ArchiveContentHashCache_ReusesCachedHashForKeyAndRecomputesOnChange()
    {
        var directory = Path.Combine(Path.GetTempPath(), "panopticon-run-identity-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var archivePath = Path.Combine(directory, "TEST.big");
            var cachePath = Path.Combine(directory, "cache.json");
            File.WriteAllBytes(archivePath, [1, 2, 3, 4, 5]);

            var cache = new ArchiveContentHashCache(cachePath);
            var first = cache.GetOrCompute(archivePath);
            var second = cache.GetOrCompute(archivePath);
            Assert.Equal(first, second);
            Assert.Equal(1, cache.EntryCount);
            Assert.True(File.Exists(cachePath));

            // Same size, new mtime and content: the cache key changes and the hash is recomputed.
            var lastWrite = new FileInfo(archivePath).LastWriteTimeUtc;
            File.WriteAllBytes(archivePath, [5, 4, 3, 2, 1]);
            File.SetLastWriteTimeUtc(archivePath, lastWrite.AddSeconds(5));

            var third = cache.GetOrCompute(archivePath);
            Assert.NotEqual(first, third);
            Assert.True(third != 0);

            // A fresh cache instance reads the persisted entries.
            var reloaded = new ArchiveContentHashCache(cachePath);
            Assert.Equal(third, reloaded.GetOrCompute(archivePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Capture_HashesArchiveContentsThroughTheCache()
    {
        var directory = Path.Combine(Path.GetTempPath(), "panopticon-run-identity-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var archivePath = Path.Combine(directory, "TEST.big");
            File.WriteAllBytes(archivePath, [9, 8, 7]);
            var cache = new ArchiveContentHashCache(Path.Combine(directory, "cache.json"));

            var identity = RunIdentity.Capture(7, "single", [], [archivePath], cache);

            var archive = Assert.Single(identity.Archives);
            Assert.Equal("TEST.big", archive.Name);
            Assert.Equal(3, archive.Length);
            Assert.Equal(cache.GetOrCompute(archivePath), archive.ContentHash);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
