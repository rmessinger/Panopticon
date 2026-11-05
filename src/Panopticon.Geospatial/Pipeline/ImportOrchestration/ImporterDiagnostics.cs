namespace Panopticon.Geospatial.Pipeline.ImportOrchestration;

/// <summary>
/// Diagnostic codes for import orchestration failures (WI-B4b-18); this class owns
/// GEO-IMP-001..009 exclusively. Recoverable orchestration failures add one of these
/// and fail the import; component-owned codes stay with their components.
/// </summary>
internal static class ImporterDiagnostics
{
    /// <summary>The aggregated staged validation checks exceeded the maxFeatureCount bound before they were copied.</summary>
    internal const string CheckBudgetExceeded = "GEO-IMP-001";

    /// <summary>A staged validator returned a failed check; failed checks block the commit even when the validator added no error diagnostic.</summary>
    internal const string ValidatorCheckFailed = "GEO-IMP-002";
}
