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
    int MaxChunkCount = 65536,
    long MaxVectorWorkPerChunk = 64L * 1024 * 1024)
{
    /// <summary>
    /// Whole-map budget for FeatureMapper rule-feature comparisons, charged once per
    /// (rule, feature) pair. Distinct from MaxVectorWorkPerChunk, the per-chunk material
    /// polygon work budget.
    /// </summary>
    public long MaxFeatureRuleComparisons { get; init; } = 64L * 1024 * 1024;

    /// <summary>Byte bound applied separately to each payload JSON file (payload.json, report.json).</summary>
    public long MaxPayloadMetadataBytes { get; init; } = 128L * 1024 * 1024;

    /// <summary>
    /// Per-input-source encoded file byte cap, independent of decoded and metadata caps;
    /// charged before readers' own limits so padded, device, or unbounded sources are
    /// rejected while hashing. KML additionally stays within MaxKmlEntryBytes.
    /// </summary>
    public long MaxSourceFileBytes { get; init; } = 4L * 1024 * 1024 * 1024;
}
