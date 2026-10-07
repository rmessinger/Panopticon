using System;
using System.Collections.Generic;
using Veldrid;

namespace Panopticon.Graphics;

/// <summary>
/// Validates requested graphics settings against device capabilities and produces the
/// effective snapshot. Validation only; resource application stays with the owners
/// recorded in the G2 implementation plan.
/// </summary>
internal sealed class GraphicsSettingsController
{
    private static readonly PixelFormat DepthLimitFormat = PixelFormat.D32_Float_S8_UInt;

    private readonly GraphicsDevice _graphicsDevice;

    public GraphicsSettings Requested { get; private set; } = GraphicsSettings.Faithful();

    public GraphicsSettings Effective { get; private set; } = GraphicsSettings.Faithful();

    /// <summary>Fallback reasons for members where effective differs from requested.</summary>
    public IReadOnlyList<string> Fallbacks => _fallbacks;

    private readonly List<string> _fallbacks = new();

    public GraphicsSettingsController(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
    }

    /// <summary>
    /// Validates <paramref name="requested"/> and produces <see cref="Effective"/>. The optional
    /// <paramref name="sessionSampleCount"/> ceiling reports the count the session's pipelines
    /// were built with: sample-count changes need a new session, later requests clamp back.
    /// </summary>
    public void Apply(GraphicsSettings requested, int sessionSampleCount = 0)
    {
        ArgumentNullException.ThrowIfNull(requested);

        Requested = requested;

        _fallbacks.Clear();

        // The depth attachment is the stricter constraint of the two game output formats.
        var sampleCountLimit = FromTextureSampleCount(_graphicsDevice.GetSampleCountLimit(DepthLimitFormat, depthFormat: true));

        var effectiveSampleCount = requested.SampleCount;
        if (effectiveSampleCount > sampleCountLimit)
        {
            _fallbacks.Add($"SampleCount {effectiveSampleCount} -> {sampleCountLimit} (device format limit).");
            effectiveSampleCount = sampleCountLimit;
        }

        if (sessionSampleCount > 0 && effectiveSampleCount > sessionSampleCount)
        {
            _fallbacks.Add($"SampleCount {effectiveSampleCount} -> {sessionSampleCount} (session was constructed at {sessionSampleCount}; restart to change).");
            effectiveSampleCount = sessionSampleCount;
        }

        Effective = GraphicsSettings.Create(
            effectiveSampleCount,
            requested.AlphaTestMode,
            requested.ColourSpaceMode,
            requested.Anisotropy,
            requested.MipLodBias,
            requested.ShadowTier,
            requested.UiMode,
            requested.RealisticProportions);
    }

    internal static TextureSampleCount ToTextureSampleCount(int sampleCount)
    {
        return sampleCount switch
        {
            <= 1 => TextureSampleCount.Count1,
            <= 2 => TextureSampleCount.Count2,
            <= 4 => TextureSampleCount.Count4,
            <= 8 => TextureSampleCount.Count8,
            <= 16 => TextureSampleCount.Count16,
            _ => TextureSampleCount.Count32,
        };
    }

    internal static int FromTextureSampleCount(TextureSampleCount sampleCount)
    {
        return sampleCount switch
        {
            TextureSampleCount.Count2 => 2,
            TextureSampleCount.Count4 => 4,
            TextureSampleCount.Count8 => 8,
            TextureSampleCount.Count16 => 16,
            TextureSampleCount.Count32 => 32,
            _ => 1,
        };
    }
}
