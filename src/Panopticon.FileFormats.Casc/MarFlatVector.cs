using System.Buffers.Binary;

namespace Panopticon.FileFormats.Casc;

internal sealed class MarFlatVector
{
    private readonly uint[] _units;
    private readonly int _bitsPerEntry;
    private readonly uint _mask;

    public MarFlatVector(BinaryReader reader)
    {
        var unitBytes = MarReader.ReadVector(reader);
        if (unitBytes.Length % 4 != 0)
        {
            throw new InvalidDataException("MAR flat vector word data is not u32-aligned.");
        }

        _units = new uint[unitBytes.Length / 4];
        for (var i = 0; i < _units.Length; i++)
        {
            _units[i] = BinaryPrimitives.ReadUInt32LittleEndian(unitBytes.AsSpan(i * 4, 4));
        }

        _bitsPerEntry = checked((int)reader.ReadUInt32());
        _mask = reader.ReadUInt32();
        Size = checked((int)reader.ReadUInt64());
    }

    public int Size { get; }

    public int Get(int index)
    {
        if ((uint)index >= Size)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (_bitsPerEntry == 0)
        {
            return 0;
        }

        var bitPosition = index * _bitsPerEntry;
        var wordIndex = bitPosition / 32;
        var bitIndex = bitPosition % 32;
        var value = _units[wordIndex] >> bitIndex;
        if (bitIndex + _bitsPerEntry > 32)
        {
            value |= _units[wordIndex + 1] << (32 - bitIndex);
        }

        return (int)(value & _mask);
    }
}
