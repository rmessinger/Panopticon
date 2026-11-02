using System;
using Panopticon.Geospatial;

namespace Panopticon.Geospatial.Payload.Reading;

/// <summary>Shared error-diagnostic helper for the payload reader.</summary>
internal static class PayloadReaderDiagnostics
{
    internal static void Error(GeoDiagnosticBag diagnostics, string code, string message, Exception? detail = null)
    {
        diagnostics.Add(new GeoDiagnostic(code, GeoStage.Validate, GeoSeverity.Error, message, detail: detail));
    }
}
