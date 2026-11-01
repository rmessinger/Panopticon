namespace Panopticon.Geospatial.Payload.Storage;

internal static class PayloadStorageDiagnostics
{
    internal const string OutputPathInvalid = "GEO-STO-001";

    internal const string ProtectedInputInvalid = "GEO-STO-002";

    internal const string OutputOverlapsProtectedInput = "GEO-STO-003";

    internal const string OutputPathIsFile = "GEO-STO-004";

    internal const string OutputDirectoryExists = "GEO-STO-005";

    internal const string StageCreateFailed = "GEO-STO-006";

    internal const string CommitFailed = "GEO-STO-007";

    internal const string RecoveryRequired = "GEO-STO-008";

    internal const string CleanupFailed = "GEO-STO-009";
}
