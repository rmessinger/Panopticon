namespace Panopticon.Tools.GeoImport;

/// <summary>
/// Diagnostic codes of the GeoImport CLI's own recoverable failures (card 24); this class
/// owns GEO-CLI-001..009 exclusively. Component-owned codes stay with their components.
/// </summary>
internal static class GeoImportCliDiagnostics
{
    /// <summary>The textures file could not be opened or read.</summary>
    internal const string TexturesUnreadable = "GEO-CLI-001";

    /// <summary>The textures file exceeds the CLI-private 1 MiB encoded UTF-8 bound.</summary>
    internal const string TexturesOversized = "GEO-CLI-002";

    /// <summary>The textures file is not valid UTF-8.</summary>
    internal const string TexturesNotUtf8 = "GEO-CLI-003";

    /// <summary>The payload directory is missing, not a directory, a link, or has an unsafe output basename.</summary>
    internal const string PayloadInvalid = "GEO-CLI-004";

    /// <summary>The terrain texture catalog of the installed Generals could not be loaded.</summary>
    internal const string CatalogUnavailable = "GEO-CLI-005";

    /// <summary>The output .map already exists and force is disabled; the existing file is preserved.</summary>
    internal const string TargetExists = "GEO-CLI-006";

    /// <summary>The output location is rejected: inside the payload root, aliasing the textures file, or a link.</summary>
    internal const string OutputRejected = "GEO-CLI-007";

    /// <summary>Writing the staged .map failed; the staged temp file was removed.</summary>
    internal const string WriteFailed = "GEO-CLI-008";

    /// <summary>Publishing the .map failed and the previous .map was restored unchanged.</summary>
    internal const string PublishFailedRestored = "GEO-CLI-009";

    /// <summary>Publishing failed and restoring the previous .map failed too; both are preserved for manual recovery.</summary>
    internal const string PublishRecoveryRequired = "GEO-CLI-010";
}
