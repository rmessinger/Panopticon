using System.Text;

namespace Panopticon.FileFormats.Casc;

/// <summary>
/// Minimal read-only MAR trie reader for StarCraft II MNDX roots. The restore
/// algorithm follows the marisa-trie layout as documented by CascLib
/// (MIT, Ladislav Zezula) without using CascLib code.
/// </summary>
public sealed class MarTrie
{
    private const uint Signature = 0x0052414D;

    private readonly MarBitVector _louds;
    private readonly MarBitVector _terminal;
    private readonly MarBitVector _links;
    private readonly byte[] _bases;
    private readonly MarFlatVector _extras;
    private readonly byte[] _tail;
    private readonly MarBitVector _tailEnds;
    private readonly MarTrie? _next;
    private readonly int _l1NodeCount;

    private MarTrie(
        MarBitVector louds,
        MarBitVector terminal,
        MarBitVector links,
        byte[] bases,
        MarFlatVector extras,
        byte[] tail,
        MarBitVector tailEnds,
        MarTrie? next,
        int l1NodeCount)
    {
        _louds = louds;
        _terminal = terminal;
        _links = links;
        _bases = bases;
        _extras = extras;
        _tail = tail;
        _tailEnds = tailEnds;
        _next = next;
        _l1NodeCount = l1NodeCount;
    }

    public int Count => _terminal.OnesCount;

    public static MarTrie Load(BinaryReader reader, bool isTopLevel)
    {
        if (isTopLevel)
        {
            var signature = reader.ReadUInt32();
            if (signature != Signature)
            {
                throw new InvalidDataException("MAR trie has an invalid signature.");
            }
        }

        var louds = new MarBitVector(reader);
        var terminal = new MarBitVector(reader);
        var links = new MarBitVector(reader);
        var bases = MarReader.ReadVector(reader);
        var extras = new MarFlatVector(reader);
        var tail = MarReader.ReadVector(reader);
        var tailEnds = new MarBitVector(reader);
        MarTrie? next = null;
        if (links.OnesCount != 0 && tail.Length == 0)
        {
            next = Load(reader, isTopLevel: false);
        }

        MarReader.ReadVector(reader);
        var l1NodeCount = checked((int)reader.ReadUInt32());
        reader.ReadUInt32();
        return new MarTrie(louds, terminal, links, bases, extras, tail, tailEnds, next, l1NodeCount);
    }

    public string Restore(int keyId)
    {
        if ((uint)keyId >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(keyId));
        }

        var node = _terminal.Select1(keyId);
        if (node == 0)
        {
            return string.Empty;
        }

        var bytes = new List<byte>();
        RestoreKey(bytes, node);
        bytes.Reverse();
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private void RestoreKey(List<byte> bytes, int node)
    {
        while (true)
        {
            AppendNode(bytes, node, reverseLinkedText: true);
            if (node <= _l1NodeCount)
            {
                return;
            }

            node = _louds.Select1(node) - node - 1;
        }
    }

    private void RestoreLink(List<byte> bytes, int link)
    {
        if (_next is not null)
        {
            _next.RestoreLinkedKey(bytes, link);
            return;
        }

        if (_tailEnds.IsEmpty)
        {
            while (link < _tail.Length && _tail[link] != 0)
            {
                bytes.Add(_tail[link]);
                link++;
            }

            return;
        }

        while (link < _tail.Length)
        {
            bytes.Add(_tail[link]);
            if (_tailEnds.Get(link))
            {
                return;
            }

            link++;
        }
    }

    private void RestoreLinkedKey(List<byte> bytes, int node)
    {
        while (true)
        {
            AppendNode(bytes, node, reverseLinkedText: false);
            if (node <= _l1NodeCount)
            {
                return;
            }

            node = _louds.Select1(node) - node - 1;
        }
    }

    private void AppendNode(List<byte> bytes, int node, bool reverseLinkedText)
    {
        if (_links.Get(node))
        {
            var start = bytes.Count;
            RestoreLink(bytes, GetLink(node));
            if (reverseLinkedText)
            {
                bytes.Reverse(start, bytes.Count - start);
            }
        }
        else
        {
            bytes.Add(_bases[node]);
        }
    }

    private int GetLink(int node)
    {
        return _bases[node] | (_extras.Get(_links.Rank1(node)) << 8);
    }
}
