namespace Panopticon.Geospatial.Payload.Storage;

/// <summary>
/// Diagnostic codes for recoverable staged payload file failures. Serialization owns
/// GEO-PAY-001..004 and the chunk codec owns GEO-PAY-010..019; staged file writing
/// owns GEO-PAY-050..069 exclusively.
/// </summary>
internal static class StagedFileDiagnostics
{
    /// <summary>The staging directory is missing, exists as a file, or is a symbolic link.</summary>
    internal const string StageRootInvalid = "GEO-PAY-050";

    /// <summary>A staged payload subdirectory is a symbolic link or a file, or could not be created.</summary>
    internal const string StageSubdirectoryInvalid = "GEO-PAY-051";

    /// <summary>A staged payload file already exists; one-time staged files are never overwritten.</summary>
    internal const string StagedFileAlreadyExists = "GEO-PAY-052";

    /// <summary>Creating, writing, or atomically replacing a staged payload file failed on an I/O resource.</summary>
    internal const string StagedWriteFailed = "GEO-PAY-053";

    /// <summary>Removing an owned partial staged file or report temp file failed (warning; the entry remains for the transaction cleanup).</summary>
    internal const string StagedCleanupFailed = "GEO-PAY-054";

    /// <summary>payload.json could not be created or written in the staging directory.</summary>
    internal const string MetadataWriteFailed = "GEO-PAY-055";

    /// <summary>payload.json or report.json exceeded the MaxPayloadMetadataBytes bound while streaming.</summary>
    internal const string MetadataLimitExceeded = "GEO-PAY-056";

    /// <summary>report.json could not be created, written, or atomically replaced in the staging directory.</summary>
    internal const string ReportWriteFailed = "GEO-PAY-057";

    /// <summary>report.json declares a payload SHA-256 that differs from the hash of the actually written payload.json bytes.</summary>
    internal const string ReportHashMismatch = "GEO-PAY-058";

    /// <summary>An imagery chunk's PNG IHDR contradicts the fixed square 8-bit RGBA non-interlaced format or the chunk's declared pixel size.</summary>
    internal const string ImageryHeaderInvalid = "GEO-PAY-059";

    /// <summary>report.json was requested before payload.json; no metadata hash exists to bind the report to.</summary>
    internal const string ReportBeforeMetadata = "GEO-PAY-060";

    /// <summary>An imagery chunk's PNG IHDR declares dimensions beyond the raster dimension limits.</summary>
    internal const string ImageryBudgetExceeded = "GEO-PAY-061";

    /// <summary>A material chunk or payload.json carries a palette whose ordered names differ from the palette already bound to the staged payload.</summary>
    internal const string MaterialPaletteMismatch = "GEO-PAY-062";
}
