using System;

namespace Panopticon.Geospatial.Payload.Models;

/// <summary>
/// Terrain mask generation summary: the thresholds and vertical exaggeration the masks
/// were generated against, plus the per-cell counters recorded while chunks streamed.
/// </summary>
public sealed record PayloadMaskGenerationSummary
{
    public PayloadMaskGenerationSummary(
        double impassableThresholdDegrees,
        double unbuildableThresholdDegrees,
        double verticalExaggeration,
        long chunkCount,
        long impassableCellCount,
        long unbuildableCellCount,
        long slopeImpassableCellCount,
        long slopeUnbuildableCellCount,
        long waterRuleApplicationCount,
        long passabilityRuleApplicationCount,
        long buildabilityRuleApplicationCount,
        long playableForcedCellCount)
    {
        ValidateThreshold(impassableThresholdDegrees, nameof(impassableThresholdDegrees));
        ValidateThreshold(unbuildableThresholdDegrees, nameof(unbuildableThresholdDegrees));

        if (!double.IsFinite(verticalExaggeration) || verticalExaggeration <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(verticalExaggeration),
                verticalExaggeration,
                "Vertical exaggeration must be finite and positive.");
        }

        ValidateCount(chunkCount, nameof(chunkCount));
        ValidateCount(impassableCellCount, nameof(impassableCellCount));
        ValidateCount(unbuildableCellCount, nameof(unbuildableCellCount));
        ValidateCount(slopeImpassableCellCount, nameof(slopeImpassableCellCount));
        ValidateCount(slopeUnbuildableCellCount, nameof(slopeUnbuildableCellCount));
        ValidateCount(waterRuleApplicationCount, nameof(waterRuleApplicationCount));
        ValidateCount(passabilityRuleApplicationCount, nameof(passabilityRuleApplicationCount));
        ValidateCount(buildabilityRuleApplicationCount, nameof(buildabilityRuleApplicationCount));
        ValidateCount(playableForcedCellCount, nameof(playableForcedCellCount));

        ImpassableThresholdDegrees = impassableThresholdDegrees;
        UnbuildableThresholdDegrees = unbuildableThresholdDegrees;
        VerticalExaggeration = verticalExaggeration;
        ChunkCount = chunkCount;
        ImpassableCellCount = impassableCellCount;
        UnbuildableCellCount = unbuildableCellCount;
        SlopeImpassableCellCount = slopeImpassableCellCount;
        SlopeUnbuildableCellCount = slopeUnbuildableCellCount;
        WaterRuleApplicationCount = waterRuleApplicationCount;
        PassabilityRuleApplicationCount = passabilityRuleApplicationCount;
        BuildabilityRuleApplicationCount = buildabilityRuleApplicationCount;
        PlayableForcedCellCount = playableForcedCellCount;
    }

    public double ImpassableThresholdDegrees { get; }

    public double UnbuildableThresholdDegrees { get; }

    public double VerticalExaggeration { get; }

    public long ChunkCount { get; }

    /// <summary>Cells whose final mask carries the impassable bit.</summary>
    public long ImpassableCellCount { get; }

    /// <summary>Cells whose final mask carries the unbuildable bit.</summary>
    public long UnbuildableCellCount { get; }

    /// <summary>Cells the slope stage flagged impassable, before region or playable rules.</summary>
    public long SlopeImpassableCellCount { get; }

    /// <summary>Cells the slope stage flagged unbuildable, before region or playable rules.</summary>
    public long SlopeUnbuildableCellCount { get; }

    /// <summary>Water rule applications; a cell covered by two water regions counts twice.</summary>
    public long WaterRuleApplicationCount { get; }

    /// <summary>Passability rule applications; a cell covered twice counts twice.</summary>
    public long PassabilityRuleApplicationCount { get; }

    /// <summary>Buildability rule applications; a cell covered twice counts twice.</summary>
    public long BuildabilityRuleApplicationCount { get; }

    /// <summary>Cells the playable-area exterior and holes forced both bits on.</summary>
    public long PlayableForcedCellCount { get; }

    private static void ValidateThreshold(double degrees, string paramName)
    {
        if (!double.IsFinite(degrees) || degrees < 0d || degrees > 90d)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                degrees,
                "Slope thresholds must be finite and within [0, 90] degrees.");
        }
    }

    private static void ValidateCount(long value, string paramName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, paramName);
    }
}
