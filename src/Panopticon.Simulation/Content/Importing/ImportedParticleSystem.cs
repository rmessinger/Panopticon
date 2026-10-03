#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon;

namespace Panopticon.Content.Importing;

/// <summary>
/// The velocity model of an imported particle system. Legacy NiParticleSystemController
/// speed/declination/planar-angle fields sample either a cone around an axis or a full
/// sphere. See docs/roadmap/deep-dives/c4h-ee2-particle-effects.md.
/// </summary>
public enum ImportedParticleVelocityKind
{
    Cone,
    Spherical,
}

/// <summary>Immutable velocity sampler description, in source units and engine-frame axes.</summary>
public sealed class ImportedParticleVelocity
{
    public ImportedParticleVelocityKind Kind { get; }

    /// <summary>Cone: central axis, normalized, engine frame.</summary>
    public Vector3 Axis { get; }

    /// <summary>Minimum initial speed in source units per second.</summary>
    public float SpeedLow { get; }

    /// <summary>Maximum initial speed in source units per second.</summary>
    public float SpeedHigh { get; }

    /// <summary>Cone: angle between axis and cone edge, in radians.</summary>
    public float ConeAngle { get; }

    public ImportedParticleVelocity(
        ImportedParticleVelocityKind kind,
        Vector3 axis,
        float speedLow,
        float speedHigh,
        float coneAngle)
    {
        if (!float.IsFinite(axis.X) || !float.IsFinite(axis.Y) || !float.IsFinite(axis.Z))
            throw new ArgumentException("Velocity axis must be finite.", nameof(axis));

        if (!float.IsFinite(speedLow) || !float.IsFinite(speedHigh) || speedLow < 0 || speedHigh < speedLow)
            throw new ArgumentException("Velocity speeds must satisfy 0 <= low <= high.", nameof(speedHigh));

        if (kind == ImportedParticleVelocityKind.Cone)
        {
            if (axis.LengthSquared() < 1e-10f)
            {
                throw new ArgumentException("Cone velocity requires a non-zero axis.", nameof(axis));
            }

            if (!float.IsFinite(coneAngle) || coneAngle < 0)
            {
                throw new ArgumentException("Cone angle must be finite and non-negative.", nameof(coneAngle));
            }

            axis = Vector3.Normalize(axis);
        }

        Kind = kind;
        Axis = axis;
        SpeedLow = speedLow;
        SpeedHigh = speedHigh;
        ConeAngle = coneAngle;
    }
}

/// <summary>
/// Immutable CPU description of one particle system inside an imported effect.
/// All values are source units; the materializer (WI-C4h-04) converts to engine units.
/// </summary>
public sealed class ImportedParticleSystem
{
    public string Name { get; }

    /// <summary>Local transform of the emitter relative to the effect root, engine axes, source scale.</summary>
    public Matrix4x4 LocalTransform { get; }

    public float BirthRatePerSecond { get; }

    /// <summary>False when the source relied on a fixed particle count instead of a rate.</summary>
    public bool UseBirthRate { get; }

    public float EmissionStartSeconds { get; }

    public float EmissionStopSeconds { get; }

    public float ParticleLifetimeSeconds { get; }

    public float ParticleLifetimeVariationSeconds { get; }

    /// <summary>Initial particle color, 0..1 RGB.</summary>
    public Vector3 InitialColor { get; }

    /// <summary>Initial particle alpha, 0..1.</summary>
    public float InitialAlpha { get; }

    /// <summary>Initial particle size in source units.</summary>
    public float InitialSize { get; }

    public ImportedParticleVelocity Velocity { get; }

    public ImportedParticleEmitter Emitter { get; }

    public IReadOnlyList<ImportedParticleModifier> Modifiers { get; }

    /// <summary>Alpha keys folded from NiAlphaController, sorted by time. Empty when static.</summary>
    public IReadOnlyList<ImportedParticleAlphaKey> AlphaKeys { get; }

    /// <summary>Texture file name from the NIF source texture, or null when untextured.</summary>
    public string? TextureName { get; }

    /// <summary>Material name from NiMaterialProperty, or null when absent.</summary>
    public string? MaterialName { get; }

    /// <summary>True when the source carried an NiAlphaProperty with blending enabled.</summary>
    public bool AlphaBlended { get; }

    public ImportedParticleBlendMode BlendMode { get; }

    public ImportedParticleSystem(
        string name,
        Matrix4x4 localTransform,
        float birthRatePerSecond,
        bool useBirthRate,
        float emissionStartSeconds,
        float emissionStopSeconds,
        float particleLifetimeSeconds,
        float particleLifetimeVariationSeconds,
        Vector3 initialColor,
        float initialAlpha,
        float initialSize,
        ImportedParticleVelocity velocity,
        ImportedParticleEmitter emitter,
        IReadOnlyList<ImportedParticleModifier> modifiers,
        IReadOnlyList<ImportedParticleAlphaKey> alphaKeys,
        string? textureName,
        string? materialName,
        bool alphaBlended,
        ImportedParticleBlendMode? blendMode = null)
    {
        Ensure.NotNullOrEmpty(name, "System name must not be empty.");

        if (!IsFinite(localTransform))
            throw new ArgumentException("Local transform must be finite.", nameof(localTransform));

        if (!float.IsFinite(birthRatePerSecond) || birthRatePerSecond < 0)
            throw new ArgumentException("Birth rate must be finite and non-negative.", nameof(birthRatePerSecond));

        if (!float.IsFinite(emissionStartSeconds) || emissionStartSeconds < 0)
            throw new ArgumentException("Emission start must be finite and non-negative.", nameof(emissionStartSeconds));

        if (!float.IsFinite(emissionStopSeconds) || emissionStopSeconds < emissionStartSeconds)
            throw new ArgumentException("Emission stop must be finite and >= emission start.", nameof(emissionStopSeconds));

        if (!float.IsFinite(particleLifetimeSeconds) || particleLifetimeSeconds <= 0)
            throw new ArgumentException("Particle lifetime must be finite and positive.", nameof(particleLifetimeSeconds));

        if (!float.IsFinite(particleLifetimeVariationSeconds) || particleLifetimeVariationSeconds < 0)
            throw new ArgumentException("Particle lifetime variation must be finite and non-negative.",
                nameof(particleLifetimeVariationSeconds));

        if (particleLifetimeSeconds - particleLifetimeVariationSeconds <= 0)
            throw new ArgumentException("Particle lifetime minus variation must stay positive.",
                nameof(particleLifetimeVariationSeconds));

        if (!float.IsFinite(initialAlpha))
            throw new ArgumentException("Initial alpha must be finite.", nameof(initialAlpha));

        if (!float.IsFinite(initialSize) || initialSize <= 0)
            throw new ArgumentException("Initial size must be finite and positive.", nameof(initialSize));

        ArgumentNullException.ThrowIfNull(velocity);

        ArgumentNullException.ThrowIfNull(emitter);

        ArgumentNullException.ThrowIfNull(modifiers);

        if (modifiers.Any(m => m is null))
            throw new ArgumentException("Modifiers must not contain null entries.", nameof(modifiers));

        ArgumentNullException.ThrowIfNull(alphaKeys);

        for (var i = 0; i < alphaKeys.Count; i++)
        {
            if (!float.IsFinite(alphaKeys[i].TimeSeconds) || !float.IsFinite(alphaKeys[i].Alpha))
            {
                throw new ArgumentException($"Alpha key {i} has non-finite values.", nameof(alphaKeys));
            }

            if (i > 0 && alphaKeys[i].TimeSeconds < alphaKeys[i - 1].TimeSeconds)
            {
                throw new ArgumentException("Alpha keys must be sorted by time.", nameof(alphaKeys));
            }
        }

        Name = name;
        LocalTransform = localTransform;
        BirthRatePerSecond = birthRatePerSecond;
        UseBirthRate = useBirthRate;
        EmissionStartSeconds = emissionStartSeconds;
        EmissionStopSeconds = emissionStopSeconds;
        ParticleLifetimeSeconds = particleLifetimeSeconds;
        ParticleLifetimeVariationSeconds = particleLifetimeVariationSeconds;
        InitialColor = initialColor;
        InitialAlpha = initialAlpha;
        InitialSize = initialSize;
        Velocity = velocity;
        Emitter = emitter;
        Modifiers = modifiers.ToArray();
        AlphaKeys = alphaKeys.ToArray();
        TextureName = textureName;
        MaterialName = materialName;
        AlphaBlended = alphaBlended;
        BlendMode = blendMode ?? (alphaBlended ? ImportedParticleBlendMode.Alpha : ImportedParticleBlendMode.Additive);
    }

    private static bool IsFinite(in Matrix4x4 m) =>
        float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M14) &&
        float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M23) && float.IsFinite(m.M24) &&
        float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33) && float.IsFinite(m.M34) &&
        float.IsFinite(m.M41) && float.IsFinite(m.M42) && float.IsFinite(m.M43) && float.IsFinite(m.M44);
}
