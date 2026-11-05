using Panopticon.Geospatial.Interchange;

namespace Panopticon.Geospatial.Pipeline.SourceLoading;

/// <summary>
/// One raster source plus its provenance. Raster samples are the reader's validated meter
/// heights; no grid, projection, or generation happens at this stage.
/// </summary>
internal sealed record LoadedRaster(LoadedSource Source, GeospatialRaster<double> Raster);
