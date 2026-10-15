namespace Panopticon.Graphics.Rendering.Instancing;

/// <summary>
/// Mutable render-path options for B5 (the PhysicsBackendPolicy.Current pattern): instancing
/// and the small-feature LOD gate are presentation-only, measurement-relevant switches that
/// must stay reachable without graphics-settings plumbing, so the render-scale probe and
/// tools can sweep them per run.
/// </summary>
public static class RenderScaleOptions
{
    /// <summary>Draw eligible repeated mesh parts as GPU-instanced batches (B5-2).</summary>
    public static bool Instancing { get; set; } = true;

    /// <summary>Skip meshes whose projected screen radius falls below the threshold (B5-3).</summary>
    public static bool SmallFeatureCulling { get; set; } = true;

    /// <summary>Screen radius in pixels below which a mesh is skipped entirely.</summary>
    public static float SmallFeatureCullingPx { get; set; } = 1.0f;

    /// <summary>Record per-item debug groups in render passes (B5-4); off by default.</summary>
    public static bool DebugItemGroups { get; set; }

    /// <summary>
    /// Meshes skipped by the LOD gate in the frame being built; reset by the render
    /// pipeline at frame start, read by statistics and the probe.
    /// </summary>
    public static int LodSkippedMeshes { get; internal set; }
}
