using Panopticon.Geospatial;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Payload.Models;

namespace Panopticon.Geospatial.Payload.Reading;

/// <summary>
/// Imagery decode-budget arithmetic, evaluated in long form before any PNG file is read
/// or buffer is allocated. The encoded cap bounds encoded PNG bytes as an overhead
/// allowance over the decoded budget; it is not a whole-working-memory promise.
/// </summary>
internal static class PayloadImageryBudget
{
    /// <summary>Saturating overhead allowance added to MaxRasterDecodedBytes before clamping to int.MaxValue.</summary>
    internal const long EncodedOverheadAllowanceBytes = 16L * 1024 * 1024;

    /// <summary>
    /// Validates the grid-level imagery shape: the expected side is
    /// intervalsPerChunk * imageryPixelsPerSample and the decoded RGBA volume must fit
    /// the largest allocatable array and the maxRasterDecodedBytes budget. The squared
    /// side is compared against budget / 4 before any multiplication by 4, so an
    /// extreme-but-legal pixels-per-sample value fails typed instead of overflowing.
    /// </summary>
    internal static bool TryValidateGrid(PayloadGridDefinition grid, GeospatialImportLimits limits, GeoDiagnosticBag diagnostics)
    {
        if (grid.ImageryPixelsPerSample is not { } pixelsPerSample)
        {
            return true;
        }

        var side = (long)ChunkLayout.IntervalsPerChunk * pixelsPerSample;
        var squared = side * side;

        if (squared > int.MaxValue / 4L)
        {
            PayloadReaderDiagnostics.Error(
                diagnostics,
                PayloadReaderDiagnosticCodes.ImageryBudgetExceeded,
                $"Imagery of {pixelsPerSample} pixels per sample produces {side} pixels per axis; the decoded RGBA volume exceeds the largest allocatable array.");

            return false;
        }

        if (side > limits.MaxRasterWidth || side > limits.MaxRasterHeight)
        {
            PayloadReaderDiagnostics.Error(
                diagnostics,
                PayloadReaderDiagnosticCodes.ImageryBudgetExceeded,
                $"Imagery of {pixelsPerSample} pixels per sample produces {side} pixels per axis, beyond the {limits.MaxRasterWidth} x {limits.MaxRasterHeight} raster dimension limits.");

            return false;
        }

        if (squared > limits.MaxRasterDecodedBytes / 4L)
        {
            PayloadReaderDiagnostics.Error(
                diagnostics,
                PayloadReaderDiagnosticCodes.ImageryBudgetExceeded,
                $"Imagery of {pixelsPerSample} pixels per sample decodes to more than the maxRasterDecodedBytes budget of {limits.MaxRasterDecodedBytes} bytes.");

            return false;
        }

        return true;
    }

    /// <summary>Encoded PNG byte cap: min(int.MaxValue, MaxRasterDecodedBytes + 16 MiB, saturating).</summary>
    internal static long EncodedPngCapBytes(GeospatialImportLimits limits)
    {
        if (limits.MaxRasterDecodedBytes > int.MaxValue - EncodedOverheadAllowanceBytes)
        {
            return int.MaxValue;
        }

        return limits.MaxRasterDecodedBytes + EncodedOverheadAllowanceBytes;
    }
}
