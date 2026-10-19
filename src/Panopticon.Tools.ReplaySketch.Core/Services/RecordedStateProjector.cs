#nullable enable

using System;
using System.Collections.Generic;
using Panopticon;
using Panopticon.Recording;
using Panopticon.Simulation;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// A projected world point paired with the entity identity it came from, so view hit-testing
/// survives re-projection across frames.
/// </summary>
public sealed record FramePointMarker(uint ObjectId, DomainVisualPoint Point);

/// <summary>Primitives for one recorded frame plus the identity map for its points.</summary>
public sealed record RecordedFrameProjection(
    IReadOnlyList<DomainVisualPrimitive> Primitives,
    IReadOnlyList<FramePointMarker> Points)
{
    public static readonly RecordedFrameProjection Empty = new([], []);
}

/// <summary>
/// Card WI-A5-18: projects a recorded <see cref="WorldStateSummary"/> into the card-12 visual
/// primitives. Bucket keys mirror <see cref="RtsSimulationDomainModel.TopologyKey"/> at the
/// run's configured cell size, so rendered buckets align with T1 signature buckets. The null
/// configuration case uses the same default the recording pipeline does.
/// </summary>
public static class RecordedStateProjector
{
    /// <summary>Cell bucket key for a world position; shared with the streaming service (WI-A5-24).</summary>
    internal static string BucketKey(float x, float y, float cellSize)
    {
        return $"cell:{MathF.Floor(x / cellSize)},{MathF.Floor(y / cellSize)}";
    }

    public static RecordedFrameProjection Project(WorldStateSummary? state, RunSignatureConfigurationRecord? configuration)
    {
        if (state is null || state.Objects.Count == 0)
        {
            return RecordedFrameProjection.Empty;
        }

        var cellSize = configuration is { CellSize: > 0 } ? configuration.CellSize : SignatureTreeBuilder.DefaultCellSize;
        var primitives = new List<DomainVisualPrimitive>(state.Objects.Count);
        var points = new List<FramePointMarker>(state.Objects.Count);

        foreach (var obj in state.Objects)
        {
            var point = new DomainVisualPoint(BucketKey(obj.Position.X, obj.Position.Y, cellSize), obj.Position, obj.OwnerIndex, obj.DefinitionName);
            primitives.Add(point);
            points.Add(new FramePointMarker(obj.ObjectId, point));
        }

        return new RecordedFrameProjection(primitives, points);
    }
}
