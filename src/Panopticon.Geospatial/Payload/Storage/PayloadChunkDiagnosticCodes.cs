namespace Panopticon.Geospatial.Payload.Storage;

/// <summary>
/// Diagnostic codes for recoverable chunk codec failures. Serialization owns
/// GEO-PAY-001..004; this codec owns GEO-PAY-010..019 exclusively.
/// </summary>
internal static class PayloadChunkDiagnosticCodes
{
    /// <summary>An encoded terrain, material, or mask buffer does not hold the fixed chunk byte length.</summary>
    internal const string ChunkLengthInvalid = "GEO-PAY-010";

    /// <summary>A decoded terrain height is not a finite float32 value.</summary>
    internal const string TerrainHeightNotFinite = "GEO-PAY-011";

    /// <summary>A decoded material index does not address an entry of the payload palette.</summary>
    internal const string MaterialIndexOutOfPalette = "GEO-PAY-012";

    /// <summary>A decoded mask flag byte carries bits outside the defined TerrainMaskFlags combinations.</summary>
    internal const string MaskFlagsUndefined = "GEO-PAY-013";

    /// <summary>PNG structure is invalid: framing, unknown or forbidden chunks, missing or misplaced IHDR/IDAT/IEND, trailing bytes, or extra frames.</summary>
    internal const string PngStructureInvalid = "GEO-PAY-014";

    /// <summary>A PNG chunk's stored CRC-32 does not match its type and data bytes.</summary>
    internal const string PngChecksumInvalid = "GEO-PAY-015";

    /// <summary>PNG IHDR contradicts the expected imagery format: dimensions, bit depth, color type, palette, interlace, or the chunk's declared pixel size.</summary>
    internal const string PngFormatMismatch = "GEO-PAY-016";

    /// <summary>PNG chunk count, ancillary byte total, dimension limits, decoded-byte budget, or array capacity was exceeded before any decode allocation.</summary>
    internal const string PngBudgetExceeded = "GEO-PAY-017";

    /// <summary>The PNG pixel data could not be decoded, for example a corrupt or truncated image stream behind a structurally valid file.</summary>
    internal const string PngDecodeFailed = "GEO-PAY-018";

    /// <summary>Adjacent chunks disagree on shared terrain overlap samples or material and mask core edges.</summary>
    internal const string ChunkSeamMismatch = "GEO-PAY-019";
}
