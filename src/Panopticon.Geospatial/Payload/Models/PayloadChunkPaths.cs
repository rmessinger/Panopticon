using System.Globalization;

namespace Panopticon.Geospatial.Payload.Models;

/// <summary>
/// Canonical payload file names and chunk paths (B4b deep dive section 6.1). Chunk
/// coordinates use invariant non-padded decimals and the list order is southwest
/// row-major: index <c>cy * chunksX + cx</c>.
/// </summary>
public static class PayloadChunkPaths
{
    public const string MetadataFileName = "payload.json";

    public const string ReportFileName = "report.json";

    public static string Terrain(int chunkX, int chunkY)
    {
        return string.Create(CultureInfo.InvariantCulture, $"terrain/{chunkX}_{chunkY}.bin");
    }

    public static string Materials(int chunkX, int chunkY)
    {
        return string.Create(CultureInfo.InvariantCulture, $"materials/{chunkX}_{chunkY}.bin");
    }

    public static string Masks(int chunkX, int chunkY)
    {
        return string.Create(CultureInfo.InvariantCulture, $"masks/{chunkX}_{chunkY}.bin");
    }

    public static string Imagery(int chunkX, int chunkY)
    {
        return string.Create(CultureInfo.InvariantCulture, $"imagery/{chunkX}_{chunkY}.png");
    }
}
