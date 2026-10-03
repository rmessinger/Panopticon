using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Nif;
using Panopticon.FileFormats.Nif;
using Panopticon.IO;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

/// <summary>
/// Locks the installed EE2 legacy particle corpus to the inventory recorded by
/// WI-C4h-01 in docs/roadmap/deep-dives/c4h-ee2-particle-effects.md.
/// Interactive: requires EMPIRE_EARTH_II_PATH with zips/graphics.zip and zips/db.zip.
/// </summary>
[Trait("Category", "Interactive")]
public sealed class Ee2ParticleEffectInventoryTests
{
    private const string GraphicsArchiveRelativePath = "zips/graphics.zip";
    private const string DbArchiveRelativePath = "zips/db.zip";

    private static readonly (string Name, int Blocks, long Bytes)[] ProofEffects =
    {
        ("fx_explosion_1", 68, 22041),
        ("fx_muzzle_tank", 88, 12815),
        ("fx_watersplash_1", 89, 31478),
    };

    private static readonly (string DdfPath, string EffectName)[] ProofDdfBindings =
    {
        ("db/Visuals/visual_expl_ammo.ddf", "fx_explosion_1"),
        ("db/Visuals/visual_explosion.ddf", "fx_explosion_1"),
        ("db/Visuals/visual_fx_weapon.ddf", "fx_muzzle_tank"),
        ("db/Units/bridge.ddf", "fx_watersplash_1"),
    };

    private static readonly (string TypeName, int Count)[] LegacyBlockCounts =
    {
        ("NiParticles", 259),
        ("NiParticleSystemController", 259),
        ("NiParticlesData", 259),
        ("NiParticleColorModifier", 246),
        ("NiColorData", 246),
        ("NiParticleGrowFade", 211),
        ("NiGravity", 11),
        ("NiParticleBomb", 1),
    };

    private static string InstallRoot()
    {
        var root = Environment.GetEnvironmentVariable("EMPIRE_EARTH_II_PATH");
        if (string.IsNullOrEmpty(root))
        {
            throw new InvalidOperationException(
                "Interactive test requires EMPIRE_EARTH_II_PATH pointing at an Empire Earth II install.");
        }

        return root;
    }

    private static ZipArchive OpenArchive(string relativePath)
    {
        var archivePath = Path.Combine(InstallRoot(), relativePath);
        if (!File.Exists(archivePath))
        {
            throw new InvalidOperationException(
                $"Interactive test requires the EE2 archive at '{archivePath}'.");
        }

        return ZipFile.OpenRead(archivePath);
    }

    private static byte[] ReadEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName)
            ?? throw new InvalidOperationException($"Missing archive entry '{entryName}'.");
        using var stream = entry.Open();
        using var buffered = new MemoryStream();
        stream.CopyTo(buffered);
        return buffered.ToArray();
    }

    /// <summary>Header-only census over every cache/nifcache/fx_*.nifcache entry.</summary>
    private sealed class Corpus
    {
        public required Dictionary<string, long> EntryLengths { get; init; }

        public required Dictionary<string, int> EntryBlockCounts { get; init; }

        public required Dictionary<string, int> TypeCounts { get; init; }

        public required Dictionary<string, bool> HasParticles { get; init; }

        public required int FileCount { get; init; }
    }

    private static readonly Lazy<Corpus> CorpusCache = new(ScanCorpus);

    private static Corpus ScanCorpus()
    {
        using var archive = OpenArchive(GraphicsArchiveRelativePath);
        var entryLengths = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var entryBlockCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var typeCounts = new Dictionary<string, int>();
        var hasParticles = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith("cache/nifcache/fx_", StringComparison.OrdinalIgnoreCase)
                || !entry.FullName.EndsWith(".nifcache", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = entry.FullName["cache/nifcache/".Length..][..^".nifcache".Length];
            NifHeader header;
            using (var stream = entry.Open())
            {
                using var buffered = new MemoryStream();
                stream.CopyTo(buffered);
                buffered.Position = 0;
                header = NifHeader.Parse(new NifBinaryReader(buffered));
            }

            entryLengths[name] = entry.Length;
            entryBlockCounts[name] = header.NumBlocks;
            var particle = false;
            foreach (var block in header.Blocks)
            {
                typeCounts.TryGetValue(block.TypeName, out var count);
                typeCounts[block.TypeName] = count + 1;
                particle |= block.TypeName
                    is "NiParticles"
                    or "NiParticleSystemController"
                    or "NiParticlesData";
            }

            hasParticles[name] = particle;
        }

        return new Corpus
        {
            EntryLengths = entryLengths,
            EntryBlockCounts = entryBlockCounts,
            TypeCounts = typeCounts,
            HasParticles = hasParticles,
            FileCount = entryLengths.Count,
        };
    }

    [Fact]
    public void ProofEffectsMatchLockedInventory()
    {
        var corpus = CorpusCache.Value;

        foreach (var (name, blocks, bytes) in ProofEffects)
        {
            Assert.True(corpus.EntryLengths.TryGetValue(name, out var length), $"missing fx entry '{name}'");
            Assert.Equal(bytes, length);
            Assert.Equal(blocks, corpus.EntryBlockCounts[name]);
            Assert.True(corpus.HasParticles[name], $"'{name}' has no legacy particle blocks");
        }
    }

    [Fact]
    public void LegacyParticleCensusHasNoNiPSysBlocks()
    {
        var corpus = CorpusCache.Value;

        Assert.Equal(613, corpus.FileCount);
        Assert.Equal(112, corpus.HasParticles.Values.Count(hasParticles => hasParticles));
        Assert.DoesNotContain(corpus.TypeCounts.Keys, typeName => typeName.StartsWith("NiPSys", StringComparison.Ordinal));

        foreach (var (typeName, count) in LegacyBlockCounts)
        {
            Assert.True(
                corpus.TypeCounts.TryGetValue(typeName, out var observed),
                $"census is missing block type '{typeName}'");
            Assert.Equal(count, observed);
        }
    }

    [Fact]
    public void DdfStackBindsProofEffectsToParticleNifs()
    {
        var corpus = CorpusCache.Value;
        using var dbArchive = OpenArchive(DbArchiveRelativePath);

        var ddfEntries = dbArchive.Entries
            .Where(entry => entry.FullName.EndsWith(".ddf", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(228, ddfEntries.Count);

        var tokenPattern = new Regex(@"fx_[A-Za-z0-9_]+\.nif", RegexOptions.IgnoreCase);
        var referencingFiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ddfEntries)
        {
            string text;
            using (var stream = entry.Open())
            {
                using var reader = new StreamReader(stream);
                text = reader.ReadToEnd();
            }

            foreach (var match in tokenPattern.Matches(text))
            {
                var token = match.ToString()!;
                if (!referencingFiles.TryGetValue(token, out var files))
                {
                    files = new List<string>();
                    referencingFiles[token] = files;
                }

                files.Add(entry.FullName);
            }
        }

        Assert.Equal(492, referencingFiles.Count);

        var unresolved = referencingFiles.Keys
            .Where(token => !corpus.EntryLengths.ContainsKey(token[..^".nif".Length]))
            .ToList();
        Assert.Empty(unresolved);

        var particleBound = referencingFiles.Keys
            .Count(token => corpus.HasParticles[token[..^".nif".Length]]);
        Assert.Equal(74, particleBound);

        foreach (var (ddfPath, effectName) in ProofDdfBindings)
        {
            var token = effectName + ".nif";
            Assert.True(
                referencingFiles.TryGetValue(token, out var files),
                $"no DDF references '{token}'");
            Assert.Contains(ddfPath, files, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ProofEffectsParseToExactEndOfFile()
    {
        using var archive = OpenArchive(GraphicsArchiveRelativePath);
        foreach (var (name, expectedBlocks, _) in ProofEffects)
        {
            var bytes = ReadEntry(archive, $"cache/nifcache/{name}.nifcache");
            using var stream = new MemoryStream(bytes);
            var file = NifFile.Read(stream);

            Assert.Equal(expectedBlocks, file.Blocks.Count);
            Assert.NotEmpty(file.Roots);
            Assert.Contains(
                file.Blocks,
                block => block is NifParticles);
            Assert.Contains(
                file.Blocks,
                block => block is NifParticleSystemController);
        }
    }

    [Fact]
    public void ProofEffectsPreservePerSystemBlendModes()
    {
        using var archive = OpenArchive(GraphicsArchiveRelativePath);
        foreach (var (name, _, _) in ProofEffects)
        {
            using var stream = new MemoryStream(ReadEntry(archive, $"cache/nifcache/{name}.nifcache"));
            var effect = NifParticleEffectConverter.Convert(NifFile.Read(stream), name)!;

            Assert.NotEmpty(effect.Systems);
            if (name == "fx_watersplash_1")
            {
                Assert.All(effect.Systems, system => Assert.Equal(ImportedParticleBlendMode.Alpha, system.BlendMode));
            }
            if (name == "fx_muzzle_tank")
            {
                Assert.Contains(effect.Systems, system => system.BlendMode == ImportedParticleBlendMode.Alpha);
                Assert.Contains(effect.Systems, system => system.BlendMode == ImportedParticleBlendMode.SourceColorAdditive);
            }
        }
    }

    [Fact]
    public void MuzzleBindingTargetsFiringTagNotGeometry()
    {
        var root = InstallRoot();
        using var graphics = new ZipFileSystem(Path.Combine(root, GraphicsArchiveRelativePath));
        using var textures = new ZipFileSystem(Path.Combine(root, "zips/textures.zip"));
        using var fileSystem = new CompositeFileSystem(graphics, textures);

        var imported = new NifModelImporter().ImportModel(
            "lhm13_mainbattletank",
            new AssetImportContext(fileSystem, language: null))
            ?? throw new InvalidOperationException("proof model import returned null");

        var binding = NifParticleEffectBinding.GetProofBindings()
            .Single(b => b.EffectName == "fx_muzzle_tank");

        // The firing tag comes from the turret or unit DDF tag banks (tag_muzzle_N); the
        // model's bare muzzle node is the barrel geometry pivot, not a firing tag.
        Assert.Equal("tag_muzzle_1", binding.TargetBoneName);

        var matches = imported.Skeleton.Bones
            .Select((bone, index) => (bone, index))
            .Where(entry => string.Equals(entry.bone.Name, binding.TargetBoneName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var tag = Assert.Single(matches);

        Assert.True(
            imported.SubObjects.All(subObject => subObject.ParentBoneIndex != tag.index),
            "the firing tag must parent no geometry");

        Assert.Contains(
            imported.Skeleton.Bones,
            bone => string.Equals(bone.Name, "muzzle", StringComparison.OrdinalIgnoreCase));
    }
}
