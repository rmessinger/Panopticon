#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Panopticon.Content.Importing;

/// <summary>
/// The modifier families the particle interchange can carry. See
/// docs/roadmap/deep-dives/c4h-ee2-particle-effects.md.
/// </summary>
public enum ImportedParticleModifierKind
{
    GrowFade,
    ColorOverLife,
    Gravity,
    Unsupported,
}

/// <summary>A color key over a particle's life, in seconds since spawn.</summary>
public readonly record struct ImportedParticleColorKey(float TimeSeconds, float R, float G, float B, float A);

/// <summary>An alpha key over a particle's life, in seconds since spawn.</summary>
public readonly record struct ImportedParticleAlphaKey(float TimeSeconds, float Alpha);

/// <summary>
/// Immutable particle modifier converted from the legacy NiParticleModifier chain.
/// GrowFade and ColorOverLife carry materializable data; Gravity is kept as a
/// source-only value; Unsupported marks features the materializer must reject.
/// </summary>
public sealed class ImportedParticleModifier
{
    public ImportedParticleModifierKind Kind { get; }

    /// <summary>GrowFade: seconds from spawn to full size.</summary>
    public float GrowSeconds { get; }

    /// <summary>GrowFade: seconds from start of fade to death.</summary>
    public float FadeSeconds { get; }

    /// <summary>ColorOverLife keys, sorted by time.</summary>
    public IReadOnlyList<ImportedParticleColorKey> ColorKeys { get; }

    /// <summary>Gravity: constant force magnitude in source units per second squared.</summary>
    public float GravityForce { get; }

    /// <summary>Gravity: force direction in the engine frame.</summary>
    public Vector3 GravityDirection { get; }

    /// <summary>Gravity: raw source decay value (kept verbatim, not interpreted).</summary>
    public float GravityDecay { get; }

    /// <summary>Unsupported: name of the feature that cannot be materialized.</summary>
    public string UnsupportedFeature { get; }

    private ImportedParticleModifier(
        ImportedParticleModifierKind kind,
        float growSeconds,
        float fadeSeconds,
        IReadOnlyList<ImportedParticleColorKey> colorKeys,
        float gravityForce,
        Vector3 gravityDirection,
        float gravityDecay,
        string unsupportedFeature)
    {
        Kind = kind;
        GrowSeconds = growSeconds;
        FadeSeconds = fadeSeconds;
        ColorKeys = colorKeys;
        GravityForce = gravityForce;
        GravityDirection = gravityDirection;
        GravityDecay = gravityDecay;
        UnsupportedFeature = unsupportedFeature;
    }

    /// <summary>True when the materializer knows how to apply this modifier.</summary>
    public bool IsMaterializable => Kind is ImportedParticleModifierKind.GrowFade or ImportedParticleModifierKind.ColorOverLife;

    public static ImportedParticleModifier CreateGrowFade(float growSeconds, float fadeSeconds)
    {
        ValidateNonNegative(growSeconds, nameof(growSeconds));
        ValidateNonNegative(fadeSeconds, nameof(fadeSeconds));

        return new ImportedParticleModifier(
            ImportedParticleModifierKind.GrowFade, growSeconds, fadeSeconds, Array.Empty<ImportedParticleColorKey>(),
            0, Vector3.Zero, 0, string.Empty);
    }

    public static ImportedParticleModifier CreateColorOverLife(IReadOnlyList<ImportedParticleColorKey> keys)
    {
        if (keys is null || keys.Count == 0)
        {
            throw new ArgumentException("Color-over-life modifiers require at least one key.", nameof(keys));
        }

        ValidateColorKeys(keys);

        return new ImportedParticleModifier(
            ImportedParticleModifierKind.ColorOverLife, 0, 0, keys.ToArray(),
            0, Vector3.Zero, 0, string.Empty);
    }

    public static ImportedParticleModifier CreateGravity(float force, Vector3 direction, float decay)
    {
        ValidateNonNegative(force, nameof(force));

        if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y) || !float.IsFinite(direction.Z))
        {
            throw new ArgumentException("Gravity direction must be finite.", nameof(direction));
        }

        ValidateNonNegative(decay, nameof(decay));

        return new ImportedParticleModifier(
            ImportedParticleModifierKind.Gravity, 0, 0, Array.Empty<ImportedParticleColorKey>(),
            force, direction, decay, string.Empty);
    }

    public static ImportedParticleModifier CreateUnsupported(string feature)
    {
        if (string.IsNullOrEmpty(feature))
        {
            throw new ArgumentException("Unsupported modifiers must name the feature.", nameof(feature));
        }

        return new ImportedParticleModifier(
            ImportedParticleModifierKind.Unsupported, 0, 0, Array.Empty<ImportedParticleColorKey>(),
            0, Vector3.Zero, 0, feature);
    }

    private static void ValidateNonNegative(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0)
        {
            throw new ArgumentException("Value must be finite and non-negative.", name);
        }
    }

    private static void ValidateColorKeys(IReadOnlyList<ImportedParticleColorKey> keys)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];

            if (!float.IsFinite(key.TimeSeconds) || !float.IsFinite(key.R) || !float.IsFinite(key.G) ||
                !float.IsFinite(key.B) || !float.IsFinite(key.A))
            {
                throw new ArgumentException($"Key {i} has non-finite values.", nameof(keys));
            }

            if (i > 0 && key.TimeSeconds < keys[i - 1].TimeSeconds)
            {
                throw new ArgumentException("Color keys must be sorted by time.", nameof(keys));
            }
        }
    }
}
