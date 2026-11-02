using System.Collections.Generic;

namespace Panopticon.Geospatial.Interchange;

/// <summary>Reads vector features from one local source file.</summary>
public interface IVectorSourceReader
{
    /// <summary>
    /// Reads the source at <paramref name="path"/>. Malformed or over-limit content is
    /// reported to <paramref name="diagnostics"/> instead of thrown; a partial feature list
    /// may be returned alongside errors.
    /// </summary>
    IReadOnlyList<GeospatialFeature> Read(string path, GeospatialImportLimits limits, GeoDiagnosticBag diagnostics);
}
