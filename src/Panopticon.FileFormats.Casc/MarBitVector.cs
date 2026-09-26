using System.Buffers.Binary;
using System.Numerics;

namespace Panopticon.FileFormats.Casc;

public sealed class MarBitVector
{
    private const int WordsPerBlock = 16;
    private const int BitsPerWord = 32;
    private const int BitsPerBlock = WordsPerBlock * BitsPerWord;

    private readonly uint[] _words;
    private readonly int[] _blockOnes;

    public MarBitVector(BinaryReader reader)
    {
        var wordBytes = MarReader.ReadVector(reader);
        if (wordBytes.Length % 4 != 0)
        {
            throw new InvalidDataException("MAR bit vector word data is not u32-aligned.");
        }

        _words = new uint[wordBytes.Length / 4];
        for (var i = 0; i < _words.Length; i++)
        {
            _words[i] = BinaryPrimitives.ReadUInt32LittleEndian(wordBytes.AsSpan(i * 4, 4));
        }

        Size = checked((int)reader.ReadUInt32());
        OnesCount = checked((int)reader.ReadUInt32());
        MarReader.ReadVector(reader);
        MarReader.ReadVector(reader);
        MarReader.ReadVector(reader);

        var blockCount = (_words.Length + WordsPerBlock - 1) / WordsPerBlock;
        _blockOnes = new int[blockCount + 1];
        var count = 0;
        for (var block = 0; block < blockCount; block++)
        {
            _blockOnes[block] = count;
            var wordEnd = Math.Min(_words.Length, (block + 1) * WordsPerBlock);
            for (var word = block * WordsPerBlock; word < wordEnd; word++)
            {
                count += BitOperations.PopCount(_words[word]);
            }
        }

        _blockOnes[blockCount] = count;
        if (Size != 0 && count != OnesCount)
        {
            throw new InvalidDataException("MAR bit vector one count does not match the serialized words.");
        }
    }

    public int Size { get; }

    public int OnesCount { get; }

    public bool IsEmpty => Size == 0;

    public bool Get(int index)
    {
        if ((uint)index >= Size)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return (_words[index / BitsPerWord] & (1u << (index % BitsPerWord))) != 0;
    }

    public int Rank1(int index)
    {
        if ((uint)index > Size)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (index == Size)
        {
            return OnesCount;
        }

        var wordIndex = index / BitsPerWord;
        var bitIndex = index % BitsPerWord;
        var blockIndex = wordIndex / WordsPerBlock;
        var count = _blockOnes[blockIndex];
        for (var word = blockIndex * WordsPerBlock; word < wordIndex; word++)
        {
            count += BitOperations.PopCount(_words[word]);
        }

        if (bitIndex != 0)
        {
            count += BitOperations.PopCount(_words[wordIndex] & ((1u << bitIndex) - 1));
        }

        return count;
    }

    public int Select1(int index)
    {
        if ((uint)index >= OnesCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return Select(index, selectOnes: true);
    }

    public int Select0(int index)
    {
        var zerosCount = Size - OnesCount;
        if ((uint)index >= zerosCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return Select(index, selectOnes: false);
    }

    private int Select(int index, bool selectOnes)
    {
        var low = 0;
        var high = _blockOnes.Length - 1;
        while (low + 1 < high)
        {
            var mid = (low + high) / 2;
            var count = CountThroughBlock(mid, selectOnes);
            if (count <= index)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        var remaining = index - CountThroughBlock(low, selectOnes);
        var wordStart = low * WordsPerBlock;
        var wordEnd = Math.Min(_words.Length, wordStart + WordsPerBlock);
        for (var word = wordStart; word < wordEnd; word++)
        {
            var bits = selectOnes ? _words[word] : ~_words[word];
            for (var bit = 0; bit < BitsPerWord; bit++)
            {
                var position = word * BitsPerWord + bit;
                if (position >= Size)
                {
                    break;
                }

                if ((bits & (1u << bit)) == 0)
                {
                    continue;
                }

                if (remaining == 0)
                {
                    return position;
                }

                remaining--;
            }
        }

        throw new InvalidDataException("MAR bit vector select table is inconsistent.");
    }

    private int CountThroughBlock(int block, bool countOnes)
    {
        var ones = _blockOnes[block];
        if (countOnes)
        {
            return ones;
        }

        var bits = Math.Min(Size, block * BitsPerBlock);
        return bits - ones;
    }
}
