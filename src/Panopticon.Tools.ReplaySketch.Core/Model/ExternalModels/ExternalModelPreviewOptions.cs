using System;
using System.Collections.Generic;
using System.IO;

namespace Panopticon.Tools.ReplaySketch.Model;

public enum ExternalPreviewBackground
{
    Neutral,

    Dark,
}

public enum ExternalPreviewLighting
{
    Neutral,

    Studio,
}

/// <summary>
/// Preview-only corrections: model orientation and translation, camera framing, presets,
/// selected animation and sample time, and gated per-target morph weights. These transform
/// the captured scene only; they never modify the import, source files, or C4 basis
/// conversion. Validation happens through <see cref="Validate"/> before use.
/// </summary>
public sealed record ExternalModelPreviewOptions
{
    public static ExternalModelPreviewOptions Default { get; } = new();

    public float ModelYaw { get; init; }

    public float ModelPitch { get; init; }

    public float ModelRoll { get; init; }

    public System.Numerics.Vector3 ModelTranslation { get; init; }

    public float CameraOrbit { get; init; }

    public float CameraElevation { get; init; }

    public float CameraDistance { get; init; } = 10f;

    public float CameraFieldOfView { get; init; } = float.Pi / 4f;

    public ExternalPreviewBackground Background { get; init; }

    public ExternalPreviewLighting Lighting { get; init; }

    public string? SelectedAnimationKey { get; init; }

    public float AnimationSampleTime { get; init; }

    public IReadOnlyDictionary<string, float> MorphWeights { get; init; } =
        new Dictionary<string, float>();

    /// <summary>
    /// Validates finiteness and positivity, sample time within the selected duration, and
    /// morph weights against the enabled gate's declared ranges. Throws InvalidDataException
    /// on the first violation.
    /// </summary>
    public void Validate(
        ExternalModelImportOptions importOptions,
        float? selectedAnimationDurationSeconds)
    {
        foreach (var (name, value) in new[]
        {
            (nameof(ModelYaw), ModelYaw),
            (nameof(ModelPitch), ModelPitch),
            (nameof(ModelRoll), ModelRoll),
            (nameof(CameraOrbit), CameraOrbit),
            (nameof(CameraElevation), CameraElevation),
            (nameof(CameraDistance), CameraDistance),
            (nameof(CameraFieldOfView), CameraFieldOfView),
            (nameof(AnimationSampleTime), AnimationSampleTime),
        })
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new InvalidDataException($"Preview value '{name}' must be finite.");
            }
        }

        if (float.IsNaN(ModelTranslation.X) || float.IsNaN(ModelTranslation.Y) ||
            float.IsNaN(ModelTranslation.Z) || float.IsInfinity(ModelTranslation.X) ||
            float.IsInfinity(ModelTranslation.Y) || float.IsInfinity(ModelTranslation.Z))
        {
            throw new InvalidDataException("Preview translation must be finite.");
        }

        if (CameraDistance <= 0)
        {
            throw new InvalidDataException("Preview camera distance must be positive.");
        }

        if (CameraFieldOfView <= 0 || CameraFieldOfView > float.Pi)
        {
            throw new InvalidDataException("Preview camera field of view must be in (0, pi].");
        }

        if (AnimationSampleTime < 0)
        {
            throw new InvalidDataException("Preview animation sample time must not be negative.");
        }

        if (SelectedAnimationKey is null)
        {
            if (AnimationSampleTime != 0)
            {
                throw new InvalidDataException(
                    "Preview animation sample time requires a selected animation.");
            }
        }
        else if (selectedAnimationDurationSeconds is { } duration &&
            AnimationSampleTime > duration)
        {
            throw new InvalidDataException(
                $"Preview animation sample time {AnimationSampleTime} exceeds the selected animation duration {duration}.");
        }

        if (MorphWeights.Count > 0 && !importOptions.IsFeatureGateEnabled(ExternalModelImportOptions.MorphTargetsGate))
        {
            throw new InvalidDataException(
                "Preview morph weights require the morph feature gate to be enabled.");
        }

        foreach (var (target, weight) in MorphWeights)
        {
            if (!importOptions.MorphWeightRanges.TryGetValue(target, out var range))
            {
                throw new InvalidDataException($"Preview morph weight target '{target}' has no declared range.");
            }

            if (weight < range.Minimum || weight > range.Maximum)
            {
                throw new InvalidDataException(
                    $"Preview morph weight for '{target}' is outside the allowed range [{range.Minimum}, {range.Maximum}].");
            }
        }
    }
}
