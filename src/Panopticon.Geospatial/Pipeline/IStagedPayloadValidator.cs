using System;
using System.Collections.Generic;
using System.Threading;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;

namespace Panopticon.Geospatial.Pipeline;

/// <summary>
/// One staged-payload validator: inspects the staged payload through the reader
/// interface before commit and returns ordered check records. The manifest is the same
/// validated instance generation used. Any failed check blocks commit even when the
/// validator adds no error diagnostic.
/// </summary>
public interface IStagedPayloadValidator
{
    IReadOnlyList<PayloadValidationCheck> Validate(
        IGeneratedMapPayloadReader reader,
        GeospatialImportManifest manifest,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken);
}
