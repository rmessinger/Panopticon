namespace Panopticon.Tools.H5Migrate;

/// <summary>
/// Diagnostic codes of the H5Migrate CLI's own recoverable failures; this class owns
/// H5-CLI-001..008 exclusively. Component-owned codes (manifest, importer, reader) stay
/// with their components and are printed verbatim.
/// </summary>
internal static class H5MigrateCliDiagnostics
{
    /// <summary>The input .map file does not exist.</summary>
    internal const string InputMissing = "H5-CLI-001";

    /// <summary>The input .map file could not be opened or read.</summary>
    internal const string InputUnreadable = "H5-CLI-002";

    /// <summary>The input .map file could not be decompressed or parsed.</summary>
    internal const string InputUnparseable = "H5-CLI-003";

    /// <summary>A source section present on the parsed map is neither a typed home nor a retained manifest entry.</summary>
    internal const string SourceSectionGap = "H5-CLI-004";

    /// <summary>The written package failed to re-read through the package reader.</summary>
    internal const string PackageReadFailed = "H5-CLI-005";

    /// <summary>A typed section differs semantically between the source interchange and the package re-read.</summary>
    internal const string TypedMismatch = "H5-CLI-006";

    /// <summary>A retained section differs in version or SHA-256 between the source interchange and the package re-read.</summary>
    internal const string RetainedDigestMismatch = "H5-CLI-007";

    /// <summary>Removing the failed package after a verification failure failed; the residual directory is reported.</summary>
    internal const string OutputCleanupFailed = "H5-CLI-008";
}
