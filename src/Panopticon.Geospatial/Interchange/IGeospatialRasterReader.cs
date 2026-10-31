namespace Panopticon.Geospatial.Interchange;

/// <summary>
/// Reads one local raster source, normalizing numeric samples to double while retaining
/// source sample-format and vertical-datum metadata.
/// </summary>
public interface IGeospatialRasterReader
{
    /// <summary>
    /// Reads the raster at <paramref name="path"/>. Returns null when reading fails, with
    /// the failure reported to <paramref name="diagnostics"/>; content or resource failures
    /// are not thrown.
    /// </summary>
    GeospatialRaster<double>? Read(string path, GeospatialImportLimits limits, GeoDiagnosticBag diagnostics);
}
