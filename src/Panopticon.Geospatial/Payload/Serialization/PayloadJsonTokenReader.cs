using System;
using System.Buffers;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Panopticon.Geospatial.Payload.Serialization;

/// <summary>
/// Single-pass incremental JSON token reader over a forward-only stream: byte-bounded,
/// cancellation-checked, never seeking, never buffering the whole document.
/// </summary>
internal ref struct PayloadJsonTokenReader
{
    private const int InitialBufferBytes = 16 * 1024;

    private static readonly JsonReaderOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    private static readonly byte[] ProbeBuffer = new byte[1];

    private readonly Stream _stream;
    private readonly long _maxBytes;
    private readonly CancellationToken _cancellationToken;
    private byte[] _buffer;
    private int _filled;
    private long _totalBytesRead;
    private bool _final;
    private Utf8JsonReader _reader;
    private bool _readerInitialized;

    public PayloadJsonTokenReader(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        _stream = stream;
        _maxBytes = maxBytes;
        _cancellationToken = cancellationToken;
        _buffer = new byte[InitialBufferBytes];
        _filled = 0;
        _totalBytesRead = 0;
        _final = false;
        _reader = default;
        _readerInitialized = false;
    }

    /// <summary>Total bytes pulled from the stream so far, probe bytes included.</summary>
    public long TotalBytesRead => _totalBytesRead;

    public JsonTokenType TokenType => _reader.TokenType;

    /// <summary>
    /// Advances to the next token, refilling from the stream as needed. Returns false
    /// only at end of stream; cancellation and limit violations throw.
    /// </summary>
    public bool Read()
    {
        _cancellationToken.ThrowIfCancellationRequested();

        while (true)
        {
            if (_readerInitialized && _reader.Read())
            {
                return true;
            }

            if (_final)
            {
                return false;
            }

            Refill();
        }
    }

    public string GetString()
    {
        return _reader.GetString()!;
    }

    public bool GetBoolean()
    {
        return _reader.GetBoolean();
    }

    public double GetDouble()
    {
        return _reader.GetDouble();
    }

    public float GetSingle()
    {
        return _reader.GetSingle();
    }

    public bool TryGetInt32(out int value)
    {
        return _reader.TryGetInt32(out value);
    }

    public bool TryGetInt64(out long value)
    {
        return _reader.TryGetInt64(out value);
    }

    private void Refill()
    {
        _cancellationToken.ThrowIfCancellationRequested();

        if (_readerInitialized)
        {
            // Retain the unconsumed tail (a possibly partial token) at the window front.
            var consumed = (int)_reader.BytesConsumed;
            var retained = _filled - consumed;
            if (retained > 0 && consumed > 0)
            {
                Buffer.BlockCopy(_buffer, consumed, _buffer, 0, retained);
            }

            _filled = retained;
        }

        if (_filled == _buffer.Length && !TryGrowWindow())
        {
            return;
        }

        // The probe byte matters only near the cap; with a huge remaining budget the
        // whole window space is allowed, so no addition can overflow.
        var available = (long)_buffer.Length - _filled;
        var remaining = _maxBytes - _totalBytesRead;
        var allowed = remaining >= available ? available : remaining + 1;
        var read = _stream.Read(_buffer, _filled, (int)allowed);
        if (read == 0)
        {
            _final = true;
        }
        else
        {
            _totalBytesRead += read;
            if (_totalBytesRead > _maxBytes)
            {
                throw new PayloadJsonLimitException(
                    $"The JSON document exceeds the maxPayloadMetadataBytes limit of {_maxBytes} bytes.");
            }

            _cancellationToken.ThrowIfCancellationRequested();
            _filled += read;
        }

        RebuildReader();
    }

    /// <summary>
    /// Grows the window within the budget remainder and the addressable array ceiling.
    /// When no growth is possible, probes one byte: empty finalizes, else a typed limit.
    /// </summary>
    private bool TryGrowWindow()
    {
        // Clamp the remainder to the addressable headroom before any addition.
        var headroom = (long)Array.MaxLength - _filled;
        var growable = Math.Min(_maxBytes - _totalBytesRead, headroom);
        var desired = Math.Min((long)_buffer.Length * 2, _filled + growable + 1);
        if (desired > Array.MaxLength)
        {
            desired = Array.MaxLength;
        }

        if (desired <= _buffer.Length)
        {
            var probed = _stream.Read(ProbeBuffer, 0, 1);
            if (probed > 0)
            {
                _totalBytesRead += probed;
                throw new PayloadJsonLimitException(
                    _totalBytesRead > _maxBytes
                        ? $"The JSON document exceeds the maxPayloadMetadataBytes limit of {_maxBytes} bytes."
                        : "A single JSON token exceeds the addressable buffer limit.");
            }

            _final = true;
            RebuildReader();
            return false;
        }

        _cancellationToken.ThrowIfCancellationRequested();
        Array.Resize(ref _buffer, (int)desired);

        return true;
    }

    private void RebuildReader()
    {
        _reader = new Utf8JsonReader(
            new ReadOnlySequence<byte>(_buffer, 0, _filled),
            _final,
            _readerInitialized ? _reader.CurrentState : new JsonReaderState(Options));
        _readerInitialized = true;
    }
}
