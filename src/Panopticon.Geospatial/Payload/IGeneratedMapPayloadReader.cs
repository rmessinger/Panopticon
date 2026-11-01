using System;
using System.Threading;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Payload.Models;

namespace Panopticon.Geospatial.Payload;

/// <summary>
/// Read access to a validated standalone payload directory (B4b deep dive section 6.1):
/// immutable metadata and report, lazy per-chunk reads, and game-space height and CPU
/// RGBA imagery sampling. All chunk types are CPU-only and immutable.
/// </summary>
public interface IGeneratedMapPayloadReader : IDisposable
{
    /// <summary>Immutable payload metadata; never null on a successfully opened reader.</summary>
    GeneratedMapPayloadMetadata Metadata { get; }

    /// <summary>Immutable import report; never null on a successfully opened reader.</summary>
    GeneratedMapPayloadReport Report { get; }

    /// <summary>
    /// Reads one terrain chunk, rechecking its hash to detect later mutation. Returns
    /// null with a typed diagnostic for recoverable failures, including bad coordinates.
    /// </summary>
    TerrainChunk? ReadTerrain(int chunkX, int chunkY, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken = default);

    /// <summary>Reads one material chunk; the null and diagnostic contract matches <see cref="ReadTerrain"/>.</summary>
    MaterialChunk? ReadMaterial(int chunkX, int chunkY, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken = default);

    /// <summary>Reads one mask chunk; the null and diagnostic contract matches <see cref="ReadTerrain"/>.</summary>
    TerrainMaskChunk? ReadMask(int chunkX, int chunkY, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one imagery chunk. Returns null when the payload has no imagery layer or
    /// the coordinates are out of range; the diagnostic contract matches <see cref="ReadTerrain"/>.
    /// </summary>
    ImageryChunk? ReadImagery(int chunkX, int chunkY, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bilinearly samples the terrain height at game XY across chunk seams with inclusive
    /// outer edges. Returns false with a typed diagnostic when the read is not possible.
    /// </summary>
    bool TrySampleHeight(double gameX, double gameY, out double gameZ, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bilinearly samples the generated global pixel grid at pixel centers across chunk
    /// seams with outer-boundary clamping. Returns false with a typed diagnostic when
    /// the payload has no imagery, the position is outside the map, or the read fails.
    /// </summary>
    bool TrySampleImagery(double gameX, double gameY, out RgbaValue value, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken = default);
}
