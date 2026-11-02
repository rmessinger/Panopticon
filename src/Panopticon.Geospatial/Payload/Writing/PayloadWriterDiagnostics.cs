namespace Panopticon.Geospatial.Payload.Writing;

/// <summary>
/// Diagnostic codes for payload publication orchestration. Serialization owns
/// GEO-PAY-001..004, the chunk codec owns 010..019, the reader owns 030..041, staged
/// file writing owns 050..062; publication orchestration owns GEO-PAY-063..069.
/// </summary>
internal static class PayloadWriterDiagnostics
{
    /// <summary>Commit is blocked because the job diagnostics bag already carries error diagnostics.</summary>
    internal const string CommitBlockedByJobErrors = "GEO-PAY-063";
}
