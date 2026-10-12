using System;

namespace Panopticon.Graphics.Rendering;

/// <summary>
/// Per-frame render-path counters (B5). Reset at the start of every
/// <see cref="RenderPipeline.Execute"/>; read after <see cref="Game.Render"/> by the
/// render-scale probe and diagnostics.
/// </summary>
public sealed class RenderStatistics
{
    public int ItemsOpaque { get; internal set; }

    public int ItemsTransparent { get; internal set; }

    public int ItemsShadow { get; internal set; }

    public int ItemsWater { get; internal set; }

    /// <summary>Visible items in the last opaque pass (after frustum culling).</summary>
    public int VisibleOpaque { get; internal set; }

    /// <summary>Visible items in the last transparent pass.</summary>
    public int VisibleTransparent { get; internal set; }

    /// <summary>Visible shadow items summed across every cascade pass of the frame.</summary>
    public int VisibleShadow { get; internal set; }

    /// <summary>Every indexed draw issued this frame, all buckets and passes.</summary>
    public int DrawCalls { get; internal set; }

    /// <summary>Draws issued as instanced runs (B5-2; zero until instancing lands).</summary>
    public int InstancedDrawCalls { get; internal set; }

    /// <summary>Instanced runs formed this frame; one run draws many instances.</summary>
    public int InstancedBatches { get; internal set; }

    /// <summary>Total instances submitted through instanced runs.</summary>
    public int InstancesDrawn { get; internal set; }

    internal void Reset()
    {
        ItemsOpaque = 0;
        ItemsTransparent = 0;
        ItemsShadow = 0;
        ItemsWater = 0;
        VisibleOpaque = 0;
        VisibleTransparent = 0;
        VisibleShadow = 0;
        DrawCalls = 0;
        InstancedDrawCalls = 0;
        InstancedBatches = 0;
        InstancesDrawn = 0;
    }

    /// <summary>One-line summary for logs and probe reports.</summary>
    public override string ToString() =>
        $"items opaque={ItemsOpaque} transparent={ItemsTransparent} shadow={ItemsShadow} " +
        $"visible opaque={VisibleOpaque} shadow={VisibleShadow} draws={DrawCalls} " +
        $"instanced: draws={InstancedDrawCalls} batches={InstancedBatches} instances={InstancesDrawn}";
}
