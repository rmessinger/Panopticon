#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Nif;
using Panopticon.Graphics;
using Panopticon.Graphics.ParticleSystems;
using Panopticon.Mathematics;

namespace Panopticon.Content.Loaders;

/// <summary>
/// Engine-owned materialization of imported particle interchange values into existing
/// <see cref="FXParticleSystemTemplate"/> modules. Lengths and speeds arrive in source units
/// (meters) and are converted to engine units (feet) here once; the per-frame runtime stays
/// format-agnostic. Cone emission is expressed as a cylindrical velocity around local +Z with
/// the axis rotation folded into <see cref="MaterializedParticleSystem.EmitterTransform"/>, so
/// spawn matrices orient the cone without runtime changes. An optional world scale (from DDF
/// binding data) multiplies particle sizes and the spawn matrix; the matrix then scales spawn
/// positions and velocities through the runtime's transform path, while sizes bake the scale
/// directly because the runtime does not scale per-particle quad sizes by the world matrix.
/// </summary>
internal static class ImportedParticleEffectMaterializer
{
    private const float FramesPerSecond = 30f;

    public static MaterializedParticleEffect Materialize(
        ImportedParticleEffect effect,
        Func<string, LazyAssetReference<TextureAsset>>? textureResolver = null,
        float worldScale = 1f)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        if (!float.IsFinite(worldScale) || worldScale <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(worldScale), worldScale, "World scale must be finite and positive.");
        }

        var systems = new List<MaterializedParticleSystem>(effect.Systems.Count);
        for (var i = 0; i < effect.Systems.Count; i++)
        {
            systems.Add(MaterializeSystem(effect, effect.Systems[i], textureResolver, worldScale));
        }

        return new MaterializedParticleEffect(effect.Name, systems);
    }

    private static MaterializedParticleSystem MaterializeSystem(
        ImportedParticleEffect effect,
        ImportedParticleSystem system,
        Func<string, LazyAssetReference<TextureAsset>>? textureResolver,
        float worldScale)
    {
        var systemName = $"{effect.Name}.{system.Name}";

        RejectUnmaterializableSemantics(system, systemName);

        if (system.TextureName is null)
        {
            throw new NotSupportedException(
                $"Particle system '{systemName}' has no particle texture; C4h requires a texture name per system.");
        }

        var template = new FXParticleSystemTemplate(systemName)
        {
            IsOneShot = true,

            Shader = system.BlendMode switch
            {
                ImportedParticleBlendMode.Alpha => ParticleSystemShader.Alpha,
                ImportedParticleBlendMode.SourceColorAdditive => ParticleSystemShader.SourceColorAdditive,
                _ => ParticleSystemShader.Additive,
            },
        };

        template.ParticleTexture = textureResolver is not null
            ? textureResolver(system.TextureName)
            : new LazyAssetReference<TextureAsset>(system.TextureName, static () => null!);

        // Emission window: burst k particles every n ticks with k/n ~= rate/30, choosing the
        // smallest n so the cadence stays as fine as the rate allows.
        var burstPeriod = 0;
        var burstCount = 0;
        for (var n = 1; n <= 30; n++)
        {
            var k = (int)MathF.Round(system.BirthRatePerSecond * n / FramesPerSecond, MidpointRounding.AwayFromZero);
            if (k >= 1)
            {
                burstPeriod = n;
                burstCount = k;
                break;
            }
        }

        var initialDelayFrames = (int)MathF.Round(system.EmissionStartSeconds * FramesPerSecond, MidpointRounding.AwayFromZero);
        var systemLifetimeFrames = Math.Max(1, (int)MathF.Round(system.EmissionStopSeconds * FramesPerSecond, MidpointRounding.AwayFromZero));

        template.BurstDelay = new RandomVariable(burstPeriod - 1, burstPeriod - 1, DistributionType.Uniform);
        template.BurstCount = new RandomVariable(burstCount, burstCount, DistributionType.Uniform);
        template.InitialDelay = new RandomVariable(initialDelayFrames, initialDelayFrames, DistributionType.Uniform);
        template.SystemLifetime = systemLifetimeFrames;

        var lifetimeLow = MathF.Round(
            (system.ParticleLifetimeSeconds - system.ParticleLifetimeVariationSeconds) * FramesPerSecond,
            MidpointRounding.AwayFromZero);
        var lifetimeHigh = MathF.Round(
            (system.ParticleLifetimeSeconds + system.ParticleLifetimeVariationSeconds) * FramesPerSecond,
            MidpointRounding.AwayFromZero);
        template.Lifetime = new RandomVariable(lifetimeLow, lifetimeHigh, DistributionType.Uniform);

        template.Physics = new FXParticleDefaultPhysics
        {
            VelocityDamping = new RandomVariable(1, 1, DistributionType.Uniform),
        };
        var update = new FXParticleUpdateDefault();
        template.Update = update;
        template.Draw = new FXParticleDrawDefault();

        MaterializeSize(template, update, system, worldScale);
        MaterializeColors(template, system);
        MaterializeAlpha(template, system, lifetimeHigh);

        var emitterTransform = MaterializeEmission(template, system, systemName);

        // The spawn matrix carries the world scale: the runtime transforms both spawn
        // positions and velocities through it, so speeds and volumes stay in unscaled
        // source units while quad sizes (matrix-independent) bake the scale directly.
        emitterTransform = Matrix4x4.Multiply(Matrix4x4.CreateScale(worldScale), emitterTransform);

        return new MaterializedParticleSystem(system.Name, template, emitterTransform);
    }

    private static void RejectUnmaterializableSemantics(ImportedParticleSystem system, string systemName)
    {
        if (!system.UseBirthRate)
        {
            throw new NotSupportedException(
                $"Particle system '{systemName}' emits without a birth rate; C4h materializes birth-rate schedules only.");
        }

        if (system.BirthRatePerSecond <= 0)
        {
            throw new NotSupportedException(
                $"Particle system '{systemName}' has birth rate {system.BirthRatePerSecond} particles/s; it would never emit.");
        }

        var growFadeCount = 0;
        var colorCurveCount = 0;
        foreach (var modifier in system.Modifiers)
        {
            switch (modifier.Kind)
            {
                case ImportedParticleModifierKind.GrowFade:
                    growFadeCount++;
                    break;

                case ImportedParticleModifierKind.ColorOverLife:
                    colorCurveCount++;
                    break;

                case ImportedParticleModifierKind.Gravity:
                    throw new NotSupportedException(
                        $"Particle system '{systemName}' uses a gravity modifier; gravity import is deferred past the C4h proof effects.");

                case ImportedParticleModifierKind.Unsupported:
                    throw new NotSupportedException(
                        $"Particle system '{systemName}' uses '{modifier.UnsupportedFeature}'; it has no runtime translation.");
            }
        }

        if (growFadeCount > 1)
        {
            throw new NotSupportedException(
                $"Particle system '{systemName}' carries multiple grow-fade curves; the runtime template has one slot.");
        }

        if (colorCurveCount > 1)
        {
            throw new NotSupportedException(
                $"Particle system '{systemName}' carries multiple color curves; the runtime template has one slot.");
        }
    }

    private static void MaterializeSize(
        FXParticleSystemTemplate template,
        FXParticleUpdateDefault update,
        ImportedParticleSystem system,
        float worldScale)
    {
        var sizeEngine = system.InitialSize * NifTransformConverter.FeetPerMeter * worldScale;
        var growSeconds = 0f;
        foreach (var modifier in system.Modifiers)
        {
            if (modifier.Kind == ImportedParticleModifierKind.GrowFade)
            {
                growSeconds = modifier.GrowSeconds;
            }
        }

        var growFrames = MathF.Round(growSeconds * FramesPerSecond, MidpointRounding.AwayFromZero);
        if (growFrames > 0)
        {
            // Geometric SizeRate damping: the rate decays each tick so growth converges on the
            // full size instead of continuing linearly for the whole particle lifetime (the
            // legacy grow window is always far shorter than the lifetime). Damping is chosen so
            // the residual tail past the grow window adds ~5% (1 / 0.95) of the final size.
            var damping = MathF.Pow(0.05f, 1f / growFrames);
            var rate = sizeEngine * (1f - damping) / (1f - MathF.Pow(damping, growFrames));
            template.Size = new RandomVariable(0, 0, DistributionType.Uniform);
            update.SizeRate = new RandomVariable(rate, rate, DistributionType.Uniform);
            update.SizeRateDamping = new RandomVariable(damping, damping, DistributionType.Uniform);
        }
        else
        {
            template.Size = new RandomVariable(sizeEngine, sizeEngine, DistributionType.Uniform);
        }
    }

    private static void MaterializeColors(FXParticleSystemTemplate template, ImportedParticleSystem system)
    {
        var times = new List<uint> { 0 };
        var colors = new List<ColorRgbF> { new(system.InitialColor.X, system.InitialColor.Y, system.InitialColor.Z) };

        foreach (var modifier in system.Modifiers)
        {
            if (modifier.Kind != ImportedParticleModifierKind.ColorOverLife)
            {
                continue;
            }

            foreach (var key in modifier.ColorKeys)
            {
                AppendColorKey(times, colors, new ColorRgbF(key.R, key.G, key.B), key.TimeSeconds, system.Name);
            }
        }

        template.Colors = new FXParticleColor
        {
            ColorScale = new RandomVariable(0, 0, DistributionType.Uniform),
        };

        for (var i = 0; i < colors.Count; i++)
        {
            template.Colors.ColorKeyframes[i] = new RgbColorKeyframe(colors[i], times[i]);
        }
    }

    private static void MaterializeAlpha(FXParticleSystemTemplate template, ImportedParticleSystem system, float lifetimeHighFrames)
    {
        var times = new List<uint> { 0 };
        var values = new List<RandomVariable> { new(system.InitialAlpha, system.InitialAlpha, DistributionType.Uniform) };

        foreach (var key in system.AlphaKeys)
        {
            AppendAlphaKey(times, values, key.Alpha, key.TimeSeconds, system.Name);
        }

        var fadeSeconds = 0f;
        foreach (var modifier in system.Modifiers)
        {
            if (modifier.Kind == ImportedParticleModifierKind.GrowFade)
            {
                fadeSeconds = modifier.FadeSeconds;
            }
        }

        if (fadeSeconds * FramesPerSecond > 0)
        {
            // The legacy fade shrinks each particle near its death; the closest existing
            // expression is an alpha ramp over the tail of the maximum lifetime.
            var fadeStart = Math.Max(1u, (uint)MathF.Round(lifetimeHighFrames - fadeSeconds * FramesPerSecond, MidpointRounding.AwayFromZero));
            var fadeEnd = Math.Max(fadeStart + 1, (uint)MathF.Round(lifetimeHighFrames, MidpointRounding.AwayFromZero));
            AppendAlphaKey(times, values, 1, fadeStart / FramesPerSecond, system.Name);
            AppendAlphaKey(times, values, 0, fadeEnd / FramesPerSecond, system.Name);
        }

        template.Alpha = new FXParticleAlpha();
        for (var i = 0; i < values.Count; i++)
        {
            template.Alpha.AlphaKeyframes[i] = new RandomAlphaKeyframe(values[i], times[i]);
        }
    }

    private static void AppendColorKey(List<uint> times, List<ColorRgbF> colors, ColorRgbF color, float timeSeconds, string systemName)
    {
        var frame = KeyframeFrame(timeSeconds);
        var last = times.Count > 0 ? times[^1] : uint.MaxValue;

        // Skip keys that round onto an occupied frame or before it; the runtime keyframe walk
        // requires strictly increasing times and treats later zero times as empty slots.
        if (frame <= last)
        {
            return;
        }

        if (times.Count == ParticleSystem.KeyframeCount)
        {
            throw NewKeySlotOverflow(systemName, "color");
        }

        times.Add(frame);
        colors.Add(color);
    }

    private static void AppendAlphaKey(List<uint> times, List<RandomVariable> values, float alpha, float timeSeconds, string systemName)
    {
        var frame = KeyframeFrame(timeSeconds);
        var last = times.Count > 0 ? times[^1] : uint.MaxValue;

        if (frame <= last)
        {
            return;
        }

        if (times.Count == ParticleSystem.KeyframeCount)
        {
            throw NewKeySlotOverflow(systemName, "alpha");
        }

        times.Add(frame);
        values.Add(new RandomVariable(alpha, alpha, DistributionType.Uniform));
    }

    private static NotSupportedException NewKeySlotOverflow(string systemName, string curveName)
    {
        return new NotSupportedException(
            $"Particle system '{systemName}' needs more than {ParticleSystem.KeyframeCount} {curveName} key slots; " +
            "the runtime template provides eight.");
    }

    private static uint KeyframeFrame(float seconds)
    {
        // Appended keys at frame 0 would read as unused-slot sentinels in the runtime.
        return Math.Max(1u, (uint)MathF.Round(seconds * FramesPerSecond, MidpointRounding.AwayFromZero));
    }

    private static Matrix4x4 MaterializeEmission(
        FXParticleSystemTemplate template,
        ImportedParticleSystem system,
        string systemName)
    {
        // Cone velocities emit around local +Z; rotating +Z onto the source axis orients the
        // cone through the spawn matrix instead of new runtime modules.
        var axisRotation = Matrix4x4.Identity;
        if (system.Velocity.Kind == ImportedParticleVelocityKind.Cone)
        {
            var axis = Vector3.Normalize(system.Velocity.Axis);
            if (!Matrix4x4.Invert(system.LocalTransform, out var inverseLocal))
            {
                throw new NotSupportedException(
                    $"Particle system '{systemName}' has a singular local transform; its cone axis cannot be oriented.");
            }

            var localAxis = Vector3.Normalize(Vector3.TransformNormal(axis, inverseLocal));
            axisRotation = CreateRotationFromUnitZTo(localAxis);

            // The runtime integrates Position += Velocity once per 30 Hz tick, so speeds
            // convert to per-tick displacements here, not per-second values.
            var angle = system.Velocity.ConeAngle;
            var radialLow = system.Velocity.SpeedLow * MathF.Sin(angle) * NifTransformConverter.FeetPerMeter / FramesPerSecond;
            var radialHigh = system.Velocity.SpeedHigh * MathF.Sin(angle) * NifTransformConverter.FeetPerMeter / FramesPerSecond;
            var normalLow = system.Velocity.SpeedLow * MathF.Cos(angle) * NifTransformConverter.FeetPerMeter / FramesPerSecond;
            var normalHigh = system.Velocity.SpeedHigh * MathF.Cos(angle) * NifTransformConverter.FeetPerMeter / FramesPerSecond;

            template.EmissionVelocity = new FXParticleEmissionVelocityCylinder
            {
                Radial = new RandomVariable(radialLow, radialHigh, DistributionType.Uniform),
                Normal = new RandomVariable(normalLow, normalHigh, DistributionType.Uniform),
            };
        }
        else
        {
            var speedLow = system.Velocity.SpeedLow * NifTransformConverter.FeetPerMeter / FramesPerSecond;
            var speedHigh = system.Velocity.SpeedHigh * NifTransformConverter.FeetPerMeter / FramesPerSecond;
            template.EmissionVelocity = new FXParticleEmissionVelocitySphere
            {
                Speed = new RandomVariable(speedLow, speedHigh, DistributionType.Uniform),
            };
        }

        // Volume coordinates live in template space; with a cone they must be pre-rotated by the
        // inverse axis rotation so the spawn matrix places them unchanged.
        Matrix4x4.Invert(axisRotation, out var inverseAxisRotation);

        switch (system.Emitter.Kind)
        {
            case ImportedParticleEmitterKind.Point:
                template.EmissionVolume = new FXParticleEmissionVolumePoint();
                break;

            case ImportedParticleEmitterKind.Line:
                {
                    var half = LineAxis(system.Emitter.Dimensions) * (system.Emitter.Dimensions * 0.5f * NifTransformConverter.FeetPerMeter);
                    template.EmissionVolume = new FXParticleEmissionVolumeLine
                    {
                        StartPoint = Vector3.Transform(-half, inverseAxisRotation),
                        EndPoint = Vector3.Transform(half, inverseAxisRotation),
                    };
                    break;
                }

            case ImportedParticleEmitterKind.Box:
                {
                    if (axisRotation != Matrix4x4.Identity)
                    {
                        throw new NotSupportedException(
                            $"Particle system '{systemName}' combines a box emitter with a rotated cone axis; " +
                            "the runtime box volume is axis-aligned.");
                    }

                    template.EmissionVolume = new FXParticleEmissionVolumeBox
                    {
                        HalfSize = system.Emitter.Dimensions * (0.5f * NifTransformConverter.FeetPerMeter),
                    };
                    break;
                }

            default:
                throw new NotSupportedException(
                    $"Particle system '{systemName}' uses emitter kind '{system.Emitter.Kind}'; " +
                    "C4h materializes point, line, and box emitters.");
        }

        return axisRotation * system.LocalTransform;
    }

    private static Vector3 LineAxis(Vector3 dimensions)
    {
        if (dimensions.X != 0)
        {
            return Vector3.UnitX * MathF.Sign(dimensions.X);
        }

        if (dimensions.Y != 0)
        {
            return Vector3.UnitY * MathF.Sign(dimensions.Y);
        }

        return Vector3.UnitZ * MathF.Sign(dimensions.Z);
    }

    private static Matrix4x4 CreateRotationFromUnitZTo(Vector3 target)
    {
        var dot = Vector3.Dot(Vector3.UnitZ, target);
        if (dot > 0.999999f)
        {
            return Matrix4x4.Identity;
        }

        if (dot < -0.999999f)
        {
            return Matrix4x4.CreateRotationX(MathF.PI);
        }

        var axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, target));
        var angle = MathF.Acos(Math.Clamp(dot, -1f, 1f));
        return Matrix4x4.CreateFromAxisAngle(axis, angle);
    }
}

internal sealed record MaterializedParticleEffect(
    string EffectName,
    IReadOnlyList<MaterializedParticleSystem> Systems);

internal sealed record MaterializedParticleSystem(
    string SystemName,
    FXParticleSystemTemplate Template,
    Matrix4x4 EmitterTransform);
