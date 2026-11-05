using System;
using Panopticon.Geospatial;

namespace Panopticon.Geospatial.Pipeline.Generation;

/// <summary>
/// Typed diagnostic codes of chunk generation orchestration, reserved GEO-RUN-001
/// through GEO-RUN-009. Generator, codec, and staged-write failures carry their own
/// codes; these cover the failures only orchestration can detect. Every code is a
/// recoverable Generate-stage error: the run stops and returns null.
/// </summary>
internal static class ImportChunkGenerationDiagnostics
{
    public const string ChunkMetadataBudgetExceededCode = "GEO-RUN-001";

    public const string StagedTerrainCacheInvalidCode = "GEO-RUN-002";

    public const string StagedTerrainCacheMutatedCode = "GEO-RUN-003";

    public const string SummaryInvalidCode = "GEO-RUN-004";

    public const string MetadataInvalidCode = "GEO-RUN-005";

    public const string PlanImageryInvalidCode = "GEO-RUN-006";

    public static void AddError(GeoDiagnosticBag diagnostics, string code, string message, string? sourcePath = null, Exception? detail = null)
    {
        diagnostics.Add(new GeoDiagnostic(code, GeoStage.Generate, GeoSeverity.Error, message, sourcePath, detail));
    }
}
