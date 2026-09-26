using System;
using System.IO;
using Panopticon.FileFormats.Casc;
using Xunit;

namespace Panopticon.Tests.Data.Casc;

public sealed class MarTrieTests
{
    [Fact]
    public void RankAndSelectMatchNaiveScan()
    {
        var random = new Random(12345);
        var bits = new bool[10_000];
        for (var i = 0; i < bits.Length; i++)
        {
            bits[i] = random.Next(4) == 0;
        }

        using var stream = new MemoryStream(MarTestTrieBuilder.BuildBitVector(bits));
        using var reader = new BinaryReader(stream);
        var vector = new MarBitVector(reader);
        var onesSeen = 0;
        var zerosSeen = 0;
        for (var i = 0; i < bits.Length; i++)
        {
            Assert.Equal(onesSeen, vector.Rank1(i));
            if (bits[i])
            {
                Assert.Equal(i, vector.Select1(onesSeen));
                onesSeen++;
            }
            else
            {
                Assert.Equal(i, vector.Select0(zerosSeen));
                zerosSeen++;
            }
        }

        Assert.Equal(onesSeen, vector.Rank1(bits.Length));
    }

    [Fact]
    public void TailTrieRestoresSharedPrefixPaths()
    {
        var keys = new[]
        {
            "mods/liberty.sc2mod/base.sc2assets/assets/units/marine.m3",
            "mods/liberty.sc2mod/base.sc2assets/assets/units/medic.m3",
            "mods/liberty.sc2mod/base.sc2assets/assets/textures/marine.dds"
        };

        var trie = Load(MarTestTrieBuilder.BuildTailTrie(keys));

        for (var i = 0; i < keys.Length; i++)
        {
            Assert.Equal(keys[i], trie.Restore(i));
        }
    }

    [Fact]
    public void NestedTrieLinksRestoreKeys()
    {
        var keys = new[]
        {
            "nested/alpha",
            "nested/beta",
            "nested/gamma"
        };

        var trie = Load(MarTestTrieBuilder.BuildNestedTrie(keys));

        for (var i = 0; i < keys.Length; i++)
        {
            Assert.Equal(keys[i], trie.Restore(i));
        }
    }

    private static MarTrie Load(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        return MarTrie.Load(reader, isTopLevel: true);
    }
}
