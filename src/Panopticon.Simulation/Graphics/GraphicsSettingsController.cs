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

    public GraphicsSettings Requested { get; private set; } = GraphicsSettings.Legacy();

    public GraphicsSettings Effective { get; private set; } = GraphicsSettings.Legacy();

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
    public void Apply(GraphicsSettings requested, int sessionSampleCount = 0, bool? sessionPrefilteredTerrainTextures = null)
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

        var effectiveAnisotropy = requested.Anisotropy;
        var clampedAnisotropy = ClampAnisotropy(_graphicsDevice, effectiveAnisotropy);
        if (effectiveAnisotropy != clampedAnisotropy)
        {
            _fallbacks.Add($"Anisotropy {effectiveAnisotropy} -> {clampedAnisotropy} (accepted set 1/2/4/8/16, device ceiling).");
            effectiveAnisotropy = clampedAnisotropy;
        }

        var effectiveMipLodBias = requested.MipLodBias;
        if (!float.IsFinite(effectiveMipLodBias))
        {
            _fallbacks.Add($"MipLodBias {effectiveMipLodBias} -> 0 (not finite).");
            effectiveMipLodBias = 0f;
        }
        else if (effectiveMipLodBias < -1f || effectiveMipLodBias > 1f)
        {
            var clampedBias = Math.Clamp(effectiveMipLodBias, -1f, 1f);
            _fallbacks.Add($"MipLodBias {effectiveMipLodBias} -> {clampedBias} (initial public override range).");
            effectiveMipLodBias = clampedBias;
        }

        var effectivePrefilteredTerrainTextures = requested.PrefilteredTerrainTextures;
        if (sessionPrefilteredTerrainTextures.HasValue && sessionPrefilteredTerrainTextures.Value != effectivePrefilteredTerrainTextures)
        {
            _fallbacks.Add("PrefilteredTerrainTextures is fixed for the session; restart to change terrain data selection.");
            effectivePrefilteredTerrainTextures = sessionPrefilteredTerrainTextures.Value;
        }

        var effectiveDecalLimit = Math.Clamp(requested.TerrainDecalLimit, 0, 2000);
        if (effectiveDecalLimit != requested.TerrainDecalLimit)
        {
            _fallbacks.Add($"TerrainDecalLimit {requested.TerrainDecalLimit} -> {effectiveDecalLimit} (supported range 0..2000).");
        }

        Effective = GraphicsSettings.Create(
            effectiveSampleCount,
            requested.AlphaTestMode,
            requested.ColourSpaceMode,
            effectiveAnisotropy,
            effectiveMipLodBias,
            requested.ShadowTier,
            requested.UiMode,
            requested.RealisticProportions,
            effectivePrefilteredTerrainTextures,
            effectiveDecalLimit,
            requested.TerrainDecalLighting);
    }

    /// <summary>
    /// Clamps an anisotropy request into the accepted set {1, 2, 4, 8, 16}, rounding down and
    /// honouring the device anisotropic filtering feature: without it only 1x is reportable.
    /// </summary>
    internal static int ClampAnisotropy(GraphicsDevice graphicsDevice, int anisotropy)
    {
        if (!graphicsDevice.Features.SamplerAnisotropy)
        {
            return 1;
        }

        if (anisotropy < 1)
        {
            return 1;
        }

        if (anisotropy >= 16)
        {
            return 16;
        }

        if (anisotropy >= 8)
        {
            return 8;
        }

        if (anisotropy >= 4)
        {
            return 4;
        }

        return anisotropy >= 2 ? 2 : 1;
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
