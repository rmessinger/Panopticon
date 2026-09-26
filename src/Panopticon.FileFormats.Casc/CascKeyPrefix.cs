namespace Panopticon.FileFormats.Casc;

public readonly struct CascKeyPrefix : IEquatable<CascKeyPrefix>
{
    private readonly ulong _upper;
    private readonly byte _ninth;

    public CascKeyPrefix(ulong upper, byte ninth)
    {
        _upper = upper;
        _ninth = ninth;
    }

    public CascKeyPrefix(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 9)
        {
            throw new ArgumentException("CASC key prefixes must be 9 bytes.", nameof(bytes));
        }

        _upper = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes[..8]);
        _ninth = bytes[8];
    }

    public bool Equals(CascKeyPrefix other)
    {
        return _upper == other._upper && _ninth == other._ninth;
    }

    public override bool Equals(object? obj)
    {
        return obj is CascKeyPrefix other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_upper, _ninth);
    }

    public static bool operator ==(CascKeyPrefix left, CascKeyPrefix right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(CascKeyPrefix left, CascKeyPrefix right)
    {
        return !left.Equals(right);
    }
}
