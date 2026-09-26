using System.Buffers.Binary;
using System.Globalization;

namespace Panopticon.FileFormats.Casc;

public readonly struct CascKey : IEquatable<CascKey>
{
    public const int Length = 16;

    private readonly ulong _upper;
    private readonly ulong _lower;

    public CascKey(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Length)
        {
            throw new ArgumentException($"CASC keys must be {Length} bytes.", nameof(bytes));
        }

        _upper = BinaryPrimitives.ReadUInt64BigEndian(bytes[..8]);
        _lower = BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]);
    }

    private CascKey(ulong upper, ulong lower)
    {
        _upper = upper;
        _lower = lower;
    }

    public CascKeyPrefix Prefix => new(_upper, (byte)(_lower >> 56));

    public static CascKey Parse(string hex)
    {
        if (!TryParse(hex, out var key))
        {
            throw new FormatException($"Invalid CASC key '{hex}'.");
        }

        return key;
    }

    public static bool TryParse(string hex, out CascKey key)
    {
        key = default;
        if (hex.Length != Length * 2)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[Length];
        for (var i = 0; i < Length; i++)
        {
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
            {
                return false;
            }
        }

        key = new CascKey(bytes);
        return true;
    }

    public byte[] ToArray()
    {
        var result = new byte[Length];
        WriteTo(result);
        return result;
    }

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Length)
        {
            throw new ArgumentException("Destination is too small.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt64BigEndian(destination[..8], _upper);
        BinaryPrimitives.WriteUInt64BigEndian(destination[8..], _lower);
    }

    public override string ToString()
    {
        Span<byte> bytes = stackalloc byte[Length];
        WriteTo(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public bool Equals(CascKey other)
    {
        return _upper == other._upper && _lower == other._lower;
    }

    public override bool Equals(object? obj)
    {
        return obj is CascKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_upper, _lower);
    }

    public static bool operator ==(CascKey left, CascKey right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(CascKey left, CascKey right)
    {
        return !left.Equals(right);
    }
}
