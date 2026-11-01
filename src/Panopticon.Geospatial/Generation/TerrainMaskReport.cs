using System;

namespace Panopticon.Geospatial.Generation;

/// <summary>
/// Per-import terrain mask report: the slope thresholds and the vertical exaggeration
/// the masks were generated against (masks are regenerated whenever the exaggeration
/// changes), plus cell counters recorded while chunks stream.
/// </summary>
public sealed class TerrainMaskReport
{
    /// <summary>Impassable slope threshold the recorded chunks were generated against, in degrees.</summary>
    public double ImpassableThresholdDegrees { get; private set; }

    /// <summary>Unbuildable slope threshold the recorded chunks were generated against, in degrees.</summary>
    public double UnbuildableThresholdDegrees { get; private set; }

    /// <summary>Grid vertical exaggeration the recorded chunks were generated against.</summary>
    public double VerticalExaggeration { get; private set; }

    /// <summary>Number of mask chunks recorded so far.</summary>
    public long ChunkCount { get; private set; }

    /// <summary>Cells whose final mask carries <see cref="TerrainMaskFlags.Impassable"/>.</summary>
    public long ImpassableCellCount { get; private set; }

    /// <summary>Cells whose final mask carries <see cref="TerrainMaskFlags.Unbuildable"/>.</summary>
    public long UnbuildableCellCount { get; private set; }

    /// <summary>Cells the slope stage flagged impassable, before any region or playable rule.</summary>
    public long SlopeImpassableCellCount { get; private set; }

    /// <summary>Cells the slope stage flagged unbuildable, before any region or playable rule.</summary>
    public long SlopeUnbuildableCellCount { get; private set; }

    /// <summary>Water rule applications; a cell covered by two water regions counts twice.</summary>
    public long WaterRuleApplicationCount { get; private set; }

    /// <summary>Passability rule applications; a cell covered twice counts twice.</summary>
    public long PassabilityRuleApplicationCount { get; private set; }

    /// <summary>Buildability rule applications; a cell covered twice counts twice.</summary>
    public long BuildabilityRuleApplicationCount { get; private set; }

    /// <summary>Cells the playable-area exterior and holes forced both bits on; each cell counts once.</summary>
    public long PlayableForcedCellCount { get; private set; }

    internal void Record(
        TerrainMaskChunk chunk,
        in TerrainMaskChunkStats stats,
        SlopeThresholds thresholds,
        double verticalExaggeration)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(thresholds);

        ImpassableThresholdDegrees = thresholds.ImpassableDegrees;
        UnbuildableThresholdDegrees = thresholds.UnbuildableDegrees;
        VerticalExaggeration = verticalExaggeration;
        ChunkCount++;
        ImpassableCellCount += stats.ImpassableCells;
        UnbuildableCellCount += stats.UnbuildableCells;
        SlopeImpassableCellCount += stats.SlopeImpassableCells;
        SlopeUnbuildableCellCount += stats.SlopeUnbuildableCells;
        WaterRuleApplicationCount += stats.WaterRuleApplications;
        PassabilityRuleApplicationCount += stats.PassabilityRuleApplications;
        BuildabilityRuleApplicationCount += stats.BuildabilityRuleApplications;
        PlayableForcedCellCount += stats.PlayableForcedCells;
    }
}

/// <summary>Per-chunk cell counters handed to <see cref="TerrainMaskReport.Record"/>.</summary>
internal readonly record struct TerrainMaskChunkStats(
    long SlopeImpassableCells,
    long SlopeUnbuildableCells,
    long WaterRuleApplications,
    long PassabilityRuleApplications,
    long BuildabilityRuleApplications,
    long PlayableForcedCells,
    long ImpassableCells,
    long UnbuildableCells);
