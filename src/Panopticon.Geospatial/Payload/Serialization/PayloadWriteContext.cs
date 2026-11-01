using System;
using System.Text.Json;
using System.Threading;

namespace Panopticon.Geospatial.Payload.Serialization;

/// <summary>
/// Write-side bounds and cancellation for one JSON document: string values are checked
/// against their actual escaped UTF-8 size before encoding, and record boundaries flush
/// the writer incrementally so buffered bytes stay bounded throughout.
/// </summary>
internal sealed class PayloadWriteContext
{
    private const int EncodeChunkChars = 4096;

    public PayloadWriteContext(long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        MaxBytes = maxBytes;
        CancellationToken = cancellationToken;
    }

    public long MaxBytes { get; }

    public CancellationToken CancellationToken { get; }

    /// <summary>Cancellation check plus an incremental flush at a record boundary.</summary>
    public void Checkpoint(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        CancellationToken.ThrowIfCancellationRequested();
        writer.Flush();
    }

    /// <summary>
    /// Rejects a string whose actual escaped UTF-8 encoding, plus quotes, would pass the
    /// budget. The size is counted in bounded chunks with the writer's default escaping;
    /// the bounded stream accounts for names, punctuation, and the remaining document.
    /// </summary>
    public void EnsureStringWithinBudget(Utf8JsonWriter writer, string value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var encodedBytes = CountEncodedBytes(value);

        if (writer.BytesCommitted + writer.BytesPending + encodedBytes + 2 > MaxBytes)
        {
            throw new PayloadJsonLimitException(
                $"A string value would exceed the maxPayloadMetadataBytes limit of {MaxBytes} bytes.");
        }
    }

    private long CountEncodedBytes(string value)
    {
        var encodedBytes = 0L;
        var start = 0;

        while (start < value.Length)
        {
            CancellationToken.ThrowIfCancellationRequested();

            var end = Math.Min(start + EncodeChunkChars, value.Length);

            // Keep a UTF-16 surrogate pair within one chunk.
            if (end < value.Length && char.IsHighSurrogate(value[end - 1]))
            {
                end++;
            }

            encodedBytes += JsonEncodedText.Encode(value.AsSpan(start, end - start)).EncodedUtf8Bytes.Length;
            start = end;
        }

        return encodedBytes;
    }
}
