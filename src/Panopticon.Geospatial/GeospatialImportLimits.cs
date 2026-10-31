namespace Panopticon.Geospatial;

public sealed record GeospatialImportLimits(
    long MaxManifestBytes = 1024 * 1024,
    long MaxKmlEntryBytes = 64L * 1024 * 1024,
    int MaxKmzEntryCount = 1000,
    long MaxKmzExpandedBytes = 512L * 1024 * 1024,
    int MaxKmzCompressionRatio = 100,
    int MaxRasterWidth = 65536,
    int MaxRasterHeight = 65536,
    long MaxRasterDecodedBytes = 4L * 1024 * 1024 * 1024,
    int MaxFeatureCount = 1_000_000,
    long MaxCoordinateCount = 50_000_000,
    int MaxChunkCount = 65536);
