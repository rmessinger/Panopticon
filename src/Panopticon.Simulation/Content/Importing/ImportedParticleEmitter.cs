#nullable enable

using System;
using System.Numerics;

namespace Panopticon.Content.Importing;

/// <summary>
/// The emission volume of an imported particle system, in source units.
/// See docs/roadmap/deep-dives/c4h-ee2-particle-effects.md.
/// </summary>
public enum ImportedParticleEmitterKind
{
    Point,
    Line,
    Box,
    Sphere,
    Cone,
}

/// <summary>
/// Immutable description of where new particles spawn. Legacy NiParticleSystemController
/// emitter dimensions map to Point, Line, or Box only; any other volume is rejected by
/// the converter instead of being guessed.
/// </summary>
public sealed class ImportedParticleEmitter
{
    public ImportedParticleEmitterKind Kind { get; }

    /// <summary>Emitter extents along the local axes, in source units (meters).</summary>
    public Vector3 Dimensions { get; }

    public ImportedParticleEmitter(ImportedParticleEmitterKind kind, Vector3 dimensions)
    {
        if (!float.IsFinite(dimensions.X) || !float.IsFinite(dimensions.Y) || !float.IsFinite(dimensions.Z))
        {
            throw new ArgumentException("Emitter dimensions must be finite.", nameof(dimensions));
        }

        if (dimensions.X < 0 || dimensions.Y < 0 || dimensions.Z < 0)
        {
            throw new ArgumentException("Emitter dimensions must be non-negative.", nameof(dimensions));
        }

        var nonZeroAxes =
            (dimensions.X > 0 ? 1 : 0) +
            (dimensions.Y > 0 ? 1 : 0) +
            (dimensions.Z > 0 ? 1 : 0);
        switch (kind)
        {
            case ImportedParticleEmitterKind.Point when nonZeroAxes != 0:
            case ImportedParticleEmitterKind.Line when nonZeroAxes != 1:
            case ImportedParticleEmitterKind.Box when nonZeroAxes < 2:
                throw new ArgumentException(
                    $"Emitter kind {kind} is ambiguous with {nonZeroAxes} non-zero dimension axes: {dimensions}.",
                    nameof(dimensions));
        }

        Kind = kind;
        Dimensions = dimensions;
    }

    public static ImportedParticleEmitter Point() => new(ImportedParticleEmitterKind.Point, Vector3.Zero);
}
