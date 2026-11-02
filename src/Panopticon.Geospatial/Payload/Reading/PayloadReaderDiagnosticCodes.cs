namespace Panopticon.Geospatial.Payload.Reading;

/// <summary>
/// Diagnostic codes for recoverable payload reader failures. Serialization owns
/// GEO-PAY-001..004 and the chunk codec owns GEO-PAY-010..019; the reader owns
/// GEO-PAY-030..049 exclusively.
/// </summary>
internal static class PayloadReaderDiagnosticCodes
{
    /// <summary>The payload root path is missing or not a directory.</summary>
    internal const string RootInvalid = "GEO-PAY-030";

    /// <summary>The payload root or one of its ancestors is a symbolic link; links are never followed, not even at the root.</summary>
    internal const string RootIsLink = "GEO-PAY-031";

    /// <summary>The directory inventory holds an extra or missing entry against the canonical payload layout.</summary>
    internal const string InventoryMismatch = "GEO-PAY-032";

    /// <summary>An inventory entry is a symbolic link; links are rejected before descent and on every lazy read.</summary>
    internal const string InventoryLink = "GEO-PAY-033";

    /// <summary>A payload file could not be opened or read; the reader fails closed on I/O, permission, and attribute errors.</summary>
    internal const string FileUnreadable = "GEO-PAY-034";

    /// <summary>The actual file length or hash differs from the descriptor, at open or after mutation before a lazy read.</summary>
    internal const string FileMutated = "GEO-PAY-035";

    /// <summary>The report's payloadSha256 does not match the actual payload.json bytes.</summary>
    internal const string ReportHashMismatch = "GEO-PAY-036";

    /// <summary>The report carries error diagnostics or failed validation checks.</summary>
    internal const string ReportNotClean = "GEO-PAY-037";

    /// <summary>An imagery read or sample was requested from a payload with no imagery layer.</summary>
    internal const string ImageryAbsent = "GEO-PAY-038";

    /// <summary>Chunk coordinates lie outside the grid.</summary>
    internal const string ChunkCoordinatesOutOfRange = "GEO-PAY-039";

    /// <summary>The game position lies outside the map rectangle or is not a finite number.</summary>
    internal const string SampleOutsideMap = "GEO-PAY-040";

    /// <summary>Imagery dimensions exceed the decode budget, raster limits, array capacity, or encoded-byte cap before any file read or allocation.</summary>
    internal const string ImageryBudgetExceeded = "GEO-PAY-041";
}
