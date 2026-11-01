using System;
using Panopticon.Geospatial;

namespace Panopticon.Geospatial.Pipeline.GenerationPlanning;

/// <summary>
/// Typed diagnostic codes of plan building, distinct from the codes the grid, generators,
/// and loaders add themselves. Every code is a recoverable error: planning stops and
/// returns null.
/// </summary>
internal static class ImportGenerationPlanDiagnostics
{
    public const string PaletteInvalidCode = "GEO-PLN-001";

    public const string SamplingOptionsInvalidCode = "GEO-PLN-002";

    public const string SlopeThresholdsInvalidCode = "GEO-PLN-003";

    public const string ProvenanceInvalidCode = "GEO-PLN-004";

    public const string PayloadGridInvalidCode = "GEO-PLN-005";

    public static void AddError(GeoDiagnosticBag diagnostics, string code, string message, Exception? detail = null)
    {
        diagnostics.Add(new GeoDiagnostic(code, GeoStage.Generate, GeoSeverity.Error, message, detail: detail));
    }
}
