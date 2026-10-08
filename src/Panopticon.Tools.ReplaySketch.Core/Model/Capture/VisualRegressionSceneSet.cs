using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Panopticon.Tools.ReplaySketch.Model.Capture;

/// <summary>One scene in the visual regression scene set.</summary>
public class VisualRegressionScene
{
    /// <summary>Stable scene name; also the golden and report file name stem.</summary>
    public string Name { get; set; } = "";

    /// <summary>Bare embedded profile filename (e.g. <c>vr-terrain-default.json</c>).</summary>
    public string Profile { get; set; } = "";

    /// <summary>
    /// Capture index (counting from the first captured frame) whose PNG is the scene's
    /// comparison frame. Index 0 is the post-settle warm-up discard.
    /// </summary>
    public int CaptureFrame { get; set; } = 1;

    /// <summary>Creates the game with the full HUD (vr-hud); default hides it.</summary>
    public bool FullHud { get; set; }

    /// <summary>
    /// Capture viewport scale (WI-G9-03): 1 is the nominal harness viewport; larger values
    /// capture at N times native size to verify resolution independence of the HUD.
    /// </summary>
    public int ViewportScale { get; set; } = 1;

    /// <summary>
    /// Normalized [0, 1] screen rectangles over which the supersampled silhouette check runs
    /// (WI-G3-03) and metrics are scored. Empty means the whole frame; texture and HUD scenes
    /// leave it empty.
    /// </summary>
    public List<NormalizedRect> SilhouetteRegions { get; set; } = [];

    // Per-scene comparison tolerance overrides (WI-G3-04); null falls back to the defaults.

    public int? MaxChannelDelta { get; set; } = null;

    public double? MaxDifferingPixelFraction { get; set; } = null;

    public double? MaxMetricDelta { get; set; } = null;
}

/// <summary>A [0, 1] rectangle over the capture viewport: origin top-left.</summary>
public class NormalizedRect
{
    public float X { get; set; }

    public float Y { get; set; }

    public float Width { get; set; }

    public float Height { get; set; }
}

/// <summary>
/// The ordered visual regression scene set: which capture profiles make up a full harness run,
/// which frame of each is compared, and per-scene silhouette regions and tolerances. Embedded
/// as <c>Profiles/visual-regression-scenes.json</c>.
/// </summary>
public class VisualRegressionSceneSet
{
    public List<VisualRegressionScene> Scenes { get; set; } = [];
}

public static class VisualRegressionSceneSetLoader
{
    private const string ResourceName = "visual-regression-scenes.json";

    private static readonly JsonSerializerOptions SerializationOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static VisualRegressionSceneSet LoadEmbedded()
    {
        var assembly = typeof(VisualRegressionSceneSetLoader).Assembly;
        var fullName = $"Panopticon.Tools.ReplaySketch.Core.Profiles.{ResourceName}";

        using var stream = assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException(
                $"Embedded visual regression scene set '{fullName}' not found.");

        return JsonSerializer.Deserialize<VisualRegressionSceneSet>(stream, SerializationOptions)
               ?? throw new InvalidOperationException(
                   $"Deserialization of visual regression scene set '{fullName}' returned null.");
    }
}
