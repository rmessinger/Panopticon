#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Text.Json.Serialization;

namespace Panopticon.Recording;

/// <summary>
/// Identifies the build, runtime, platform, seed, shard layout, and input set of a recorded run.
/// Operational bundles refuse reconstruction when the recorded identity does not match.
/// </summary>
public sealed class RunIdentity
{
    public IReadOnlyList<ModuleVersion> Modules { get; }

    public string RuntimeDescription { get; }

    public string OSArchitecture { get; }

    public string ProcessArchitecture { get; }

    public bool Vector512Accelerated { get; }

    public bool Avx2Supported { get; }

    public bool AdvSimdSupported { get; }

    public string ShardLayoutId { get; }

    public uint Seed { get; }

    public IReadOnlyList<InputHash> Inputs { get; }

    public IReadOnlyList<ArchiveHash> Archives { get; }

    // The manifest round-trips this class through System.Text.Json (WI-A1-21); the constructor
    // parameter names bind to the properties case-insensitively.
    [JsonConstructor]
    internal RunIdentity(
        IReadOnlyList<ModuleVersion> modules,
        string runtimeDescription,
        string osArchitecture,
        string processArchitecture,
        bool vector512Accelerated,
        bool avx2Supported,
        bool advSimdSupported,
        string shardLayoutId,
        uint seed,
        IReadOnlyList<InputHash> inputs,
        IReadOnlyList<ArchiveHash> archives)
    {
        Modules = modules;
        RuntimeDescription = runtimeDescription;
        OSArchitecture = osArchitecture;
        ProcessArchitecture = processArchitecture;
        Vector512Accelerated = vector512Accelerated;
        Avx2Supported = avx2Supported;
        AdvSimdSupported = advSimdSupported;
        ShardLayoutId = shardLayoutId;
        Seed = seed;
        Inputs = inputs;
        Archives = archives;
    }

    public static RunIdentity Capture(
        uint seed,
        string shardLayoutId,
        IEnumerable<InputContent> inputs,
        IEnumerable<string> archiveFilePaths,
        ArchiveContentHashCache? cache = null)
    {
        var modules = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName())
            .Where(n => n.Name is not null && n.Name.StartsWith("Panopticon", StringComparison.Ordinal) && !n.Name.Contains(".Tests", StringComparison.Ordinal))
            .Select(n => new ModuleVersion(n.Name, n.Version?.ToString() ?? ""))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        var hashedInputs = inputs
            .OrderBy(i => i.Name, StringComparer.Ordinal)
            .Select(i => new InputHash(i.Name, XxHash3.HashToUInt64(i.Content)))
            .ToList();

        var effectiveCache = cache ?? ArchiveContentHashCache.Shared;
        var archives = archiveFilePaths
            .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
            .Select(p =>
            {
                using var stream = File.OpenRead(p);
                return new ArchiveHash(Path.GetFileName(p), stream.Length, effectiveCache.GetOrCompute(p));
            })
            .ToList();

        return new RunIdentity(
            modules,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            Vector512.IsHardwareAccelerated,
            Avx2.IsSupported,
            AdvSimd.IsSupported,
            shardLayoutId,
            seed,
            hashedInputs,
            archives);
    }

    public bool Matches(RunIdentity other) => Differences(other).Count == 0;

    /// <summary>Returns one mismatch description per differing field. Empty when the identities match.</summary>
    public IReadOnlyList<string> Differences(RunIdentity other)
    {
        var differences = new List<string>();

        if (!Seed.Equals(other.Seed))
        {
            differences.Add($"Seed: {Seed} vs {other.Seed}");
        }

        if (!string.Equals(ShardLayoutId, other.ShardLayoutId, StringComparison.Ordinal))
        {
            differences.Add($"ShardLayoutId: {ShardLayoutId} vs {other.ShardLayoutId}");
        }

        if (!string.Equals(RuntimeDescription, other.RuntimeDescription, StringComparison.Ordinal))
        {
            differences.Add($"RuntimeDescription: {RuntimeDescription} vs {other.RuntimeDescription}");
        }

        if (!string.Equals(OSArchitecture, other.OSArchitecture, StringComparison.Ordinal))
        {
            differences.Add($"OSArchitecture: {OSArchitecture} vs {other.OSArchitecture}");
        }

        if (!string.Equals(ProcessArchitecture, other.ProcessArchitecture, StringComparison.Ordinal))
        {
            differences.Add($"ProcessArchitecture: {ProcessArchitecture} vs {other.ProcessArchitecture}");
        }

        if (!Vector512Accelerated.Equals(other.Vector512Accelerated))
        {
            differences.Add($"Vector512Accelerated: {Vector512Accelerated} vs {other.Vector512Accelerated}");
        }

        if (!Avx2Supported.Equals(other.Avx2Supported))
        {
            differences.Add($"Avx2Supported: {Avx2Supported} vs {other.Avx2Supported}");
        }

        if (!AdvSimdSupported.Equals(other.AdvSimdSupported))
        {
            differences.Add($"AdvSimdSupported: {AdvSimdSupported} vs {other.AdvSimdSupported}");
        }

        CompareNamed(
            Modules,
            other.Modules,
            m => m.Name,
            (a, b) => string.Equals(a.Version, b.Version, StringComparison.Ordinal),
            (name, a, b) => differences.Add($"Module {name}: {a.Version} vs {b.Version}"),
            name => differences.Add($"Module {name}: missing on one side"));

        CompareNamed(
            Inputs,
            other.Inputs,
            i => i.Name,
            (a, b) => a.Hash.Equals(b.Hash),
            (name, a, b) => differences.Add($"Input {name}: {a.Hash:X16} vs {b.Hash:X16}"),
            name => differences.Add($"Input {name}: missing on one side"));

        CompareNamed(
            Archives,
            other.Archives,
            a => a.Name,
            (a, b) => a.Length.Equals(b.Length) && a.ContentHash.Equals(b.ContentHash),
            (name, a, b) => differences.Add($"Archive {name}: {a.Length}/{a.ContentHash:X16} vs {b.Length}/{b.ContentHash:X16}"),
            name => differences.Add($"Archive {name}: missing on one side"));

        return differences;
    }

    /// <summary>Returns the XxHash3 hex string of the canonical byte serialization, for checkpoint headers and manifests.</summary>
    public string StableHash()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Modules.Count);
        foreach (var module in Modules)
        {
            WriteString(writer, module.Name);
            WriteString(writer, module.Version);
        }

        WriteString(writer, RuntimeDescription);
        WriteString(writer, OSArchitecture);
        WriteString(writer, ProcessArchitecture);
        writer.Write(Vector512Accelerated);
        writer.Write(Avx2Supported);
        writer.Write(AdvSimdSupported);
        WriteString(writer, ShardLayoutId);
        writer.Write(Seed);
        writer.Write(Inputs.Count);
        foreach (var input in Inputs)
        {
            WriteString(writer, input.Name);
            writer.Write(input.Hash);
        }

        writer.Write(Archives.Count);
        foreach (var archive in Archives)
        {
            WriteString(writer, archive.Name);
            writer.Write(archive.Length);
            writer.Write(archive.ContentHash);
        }

        writer.Flush();
        return $"{XxHash3.HashToUInt64(stream.GetBuffer().AsSpan(0, (int)stream.Length)):X16}";
    }

    public override bool Equals(object? obj) => obj is RunIdentity other && StableHash() == other.StableHash();

    public override int GetHashCode() => StableHash().GetHashCode(StringComparison.Ordinal);

    public override string ToString() => StableHash();

    private static void CompareNamed<T>(
        IReadOnlyList<T> mine,
        IReadOnlyList<T> theirs,
        Func<T, string> name,
        Func<T, T, bool> equal,
        Action<string, T, T> reportDifference,
        Action<string> reportMissing)
    {
        var mineNames = new HashSet<string>(mine.Select(name), StringComparer.Ordinal);
        var theirsByName = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in theirs)
        {
            theirsByName[name(item)] = item;
        }

        foreach (var item in mine)
        {
            var itemName = name(item);
            if (theirsByName.TryGetValue(itemName, out var other))
            {
                if (!equal(item, other))
                {
                    reportDifference(itemName, item, other);
                }
            }
            else
            {
                reportMissing(itemName);
            }
        }

        foreach (var item in theirs)
        {
            var itemName = name(item);
            if (!mineNames.Contains(itemName))
            {
                reportMissing(itemName);
            }
        }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}

public sealed record ModuleVersion(string Name, string Version);

public sealed record InputContent(string Name, byte[] Content);

public sealed record InputHash(string Name, ulong Hash);

public sealed record ArchiveHash(string Name, long Length, ulong ContentHash);
