using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Content.Importing;
using Panopticon.Content.Loaders;
using Panopticon.Graphics.ParticleSystems;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

public class ImportedParticleEffectMaterializerTests
{
    private const float FeetPerMeter = 1f / 0.3048f;

    private static readonly ImportedParticleColorKey[] ExplosionColorKeys =
    {
        new(0.00f, 1f, 0.9f, 0.6f, 1f),
        new(0.25f, 1f, 0.4f, 0.1f, 1f),
        new(0.50f, 0.2f, 0.2f, 0.2f, 1f),
    };

    [Fact]
    public void MaterializesExplosionLikeSphericalSystem()
    {
        var effect = CreateEffect(CreateSystem(
            "explosion",
            velocity: new ImportedParticleVelocity(ImportedParticleVelocityKind.Spherical, Vector3.UnitZ, 120, 120, 0),
            birthRate: 300,
            lifetime: 0.5f,
            lifetimeVariation: 0,
            initialSize: 5,
            emissionStop: 0.15f,
            modifiers: new[]
            {
                ImportedParticleModifier.CreateGrowFade(0.05f, 0.2f),
                ImportedParticleModifier.CreateColorOverLife(ExplosionColorKeys),
            }));

        var materialized = ImportedParticleEffectMaterializer.Materialize(effect);

        var entry = Assert.Single(materialized.Systems);
        var template = Assert.IsType<FXParticleSystemTemplate>(entry.Template);

        Assert.Equal("fx_proof.explosion", template.Name);
        Assert.True(template.IsOneShot);
        Assert.Equal(ParticleSystemShader.Additive, template.Shader);

        // 300/s = 10 particles per tick; emission window 0.15 s = 5 frames.
        Assert.Equal(0, template.BurstDelay.Low);
        Assert.Equal(10, template.BurstCount.High);
        Assert.Equal(0, template.InitialDelay.Low);
        Assert.Equal(5, template.SystemLifetime);
        Assert.Equal(15, template.Lifetime.High);

        var velocity = Assert.IsType<FXParticleEmissionVelocitySphere>(template.EmissionVelocity);
        Assert.Equal(120 * FeetPerMeter / 30, velocity.Speed.Low, 4);
        Assert.Equal(120 * FeetPerMeter / 30, velocity.Speed.High, 4);

        Assert.IsType<FXParticleEmissionVolumePoint>(template.EmissionVolume);
        var physics = Assert.IsType<FXParticleDefaultPhysics>(template.Physics);
        Assert.Equal(1, physics.VelocityDamping.Low);

        // Grow ramps size from zero with geometric damping so growth converges on the full
        // size instead of continuing linearly for the whole lifetime.
        Assert.Equal(0, template.Size.Low);
        var update = Assert.IsType<FXParticleUpdateDefault>(template.Update);
        var growFrames = MathF.Round(0.05f * 30, MidpointRounding.AwayFromZero);
        var expectedDamping = MathF.Pow(0.05f, 1f / growFrames);
        Assert.Equal(expectedDamping, update.SizeRateDamping.Low, 6);
        Assert.Equal(5 * FeetPerMeter * (1 - expectedDamping) / (1 - MathF.Pow(expectedDamping, growFrames)), update.SizeRate.Low, 4);

        Assert.Equal(new ColorRgbF(1, 1, 1), template.Colors.ColorKeyframes[0].Color);
        Assert.Equal(0u, template.Colors.ColorKeyframes[0].Time);
        Assert.Equal(new ColorRgbF(1, 0.9f, 0.6f), template.Colors.ColorKeyframes[1].Color);
        Assert.Equal(1u, template.Colors.ColorKeyframes[1].Time);
        Assert.Equal(8u, template.Colors.ColorKeyframes[2].Time);
        Assert.Equal(new ColorRgbF(0.2f, 0.2f, 0.2f), template.Colors.ColorKeyframes[3].Color);

        // Alpha: initial 1 at 0, fade 1 at 15-6=9 and 0 at 15.
        Assert.Equal(1, template.Alpha.AlphaKeyframes[0].Value.Low);
        Assert.Equal(0u, template.Alpha.AlphaKeyframes[0].Time);
        Assert.Equal(9u, template.Alpha.AlphaKeyframes[1].Time);
        Assert.Equal(1, template.Alpha.AlphaKeyframes[1].Value.Low);
        Assert.Equal(15u, template.Alpha.AlphaKeyframes[2].Time);
        Assert.Equal(0, template.Alpha.AlphaKeyframes[2].Value.Low);
    }

    [Fact]
    public void MaterializesMuzzleLikeConeSystemWithAxisRotation()
    {
        var effect = CreateEffect(CreateSystem(
            "muzzle",
            velocity: new ImportedParticleVelocity(ImportedParticleVelocityKind.Cone, Vector3.UnitY, 5.625f, 9.375f, 0.349f),
            birthRate: 14.4f,
            lifetime: 0.75f,
            lifetimeVariation: 0.5f,
            initialSize: 10,
            emissionStop: 0.5f,
            localTransform: Matrix4x4.CreateTranslation(2, 0, 0)));

        var materialized = ImportedParticleEffectMaterializer.Materialize(effect);
        var template = materialized.Systems[0].Template;

        // 14.4/s resolves to 1 particle every 2 ticks; lifetimes round to whole frames.
        Assert.Equal(1, template.BurstDelay.Low);
        Assert.Equal(1, template.BurstCount.High);
        Assert.Equal(15, template.SystemLifetime);
        Assert.Equal(8, template.Lifetime.Low);
        Assert.Equal(38, template.Lifetime.High);

        var velocity = Assert.IsType<FXParticleEmissionVelocityCylinder>(template.EmissionVelocity);
        Assert.Equal(5.625f * MathF.Sin(0.349f) * FeetPerMeter / 30, velocity.Radial.Low, 4);
        Assert.Equal(9.375f * MathF.Sin(0.349f) * FeetPerMeter / 30, velocity.Radial.High, 4);
        Assert.Equal(9.375f * MathF.Cos(0.349f) * FeetPerMeter / 30, velocity.Normal.High, 4);

        // The spawn matrix maps local +Z onto the engine-frame cone axis (direction only;
        // the local translation applies to positions, not the axis check).
        var spawnedAxis = Vector3.TransformNormal(Vector3.UnitZ, materialized.Systems[0].EmitterTransform);
        Assert.Equal(0, spawnedAxis.X, 3);
        Assert.Equal(1, spawnedAxis.Y, 3);
        Assert.Equal(0, spawnedAxis.Z, 3);
    }

    [Fact]
    public void WorldScaleMultipliesSizesAndTheSpawnMatrix()
    {
        var effect = CreateEffect(CreateSystem(
            "scaled",
            velocity: new ImportedParticleVelocity(ImportedParticleVelocityKind.Spherical, Vector3.UnitZ, 4, 8, 0),
            birthRate: 60,
            lifetime: 1,
            lifetimeVariation: 0,
            initialSize: 2,
            emissionStop: 0.5f,
            emitter: new ImportedParticleEmitter(ImportedParticleEmitterKind.Box, new Vector3(3, 3, 0))));

        var materialized = ImportedParticleEffectMaterializer.Materialize(effect, textureResolver: null, worldScale: 0.005f);
        var template = materialized.Systems[0].Template;

        // Quad sizes bake the world scale directly; the runtime does not scale them by the matrix.
        Assert.Equal(2 * FeetPerMeter * 0.005f, template.Size.High, 6);

        // Speeds and volumes stay in unscaled source units: the spawn matrix carries the
        // scale and multiplies both through the runtime's transform path.
        var volume = Assert.IsType<FXParticleEmissionVolumeBox>(template.EmissionVolume);
        Assert.Equal(1.5f * FeetPerMeter, volume.HalfSize.X, 6);

        var velocity = Assert.IsType<FXParticleEmissionVelocitySphere>(template.EmissionVelocity);
        Assert.Equal(4 * FeetPerMeter / 30, velocity.Speed.Low, 6);
        Assert.Equal(8 * FeetPerMeter / 30, velocity.Speed.High, 6);

        Assert.Equal(0.005f, materialized.Systems[0].EmitterTransform.M11, 6);

        // Non-positive or non-finite scales are rejected before any template is built.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ImportedParticleEffectMaterializer.Materialize(effect, textureResolver: null, worldScale: 0f));
    }

    [Fact]
    public void MaterializesWatersplashLikeBoxSystemWithAlphaCurve()
    {
        var effect = CreateEffect(CreateSystem(
            "splash",
            velocity: new ImportedParticleVelocity(ImportedParticleVelocityKind.Spherical, Vector3.UnitZ, 4, 8, 0),
            birthRate: 60,
            lifetime: 1,
            lifetimeVariation: 0,
            initialSize: 2,
            emissionStop: 0.5f,
            emitter: new ImportedParticleEmitter(ImportedParticleEmitterKind.Box, new Vector3(3, 3, 0)),
            alphaKeys: new[] { new ImportedParticleAlphaKey(0.25f, 0.8f), new ImportedParticleAlphaKey(1f, 0f) },
            alphaBlended: true));

        var materialized = ImportedParticleEffectMaterializer.Materialize(effect);
        var template = materialized.Systems[0].Template;

        // Alpha flags fold into the alpha curve; the shader stays additive (EE2 fx textures
        // carry no usable source alpha for SrcAlpha blending).
        Assert.Equal(ParticleSystemShader.Additive, template.Shader);

        var volume = Assert.IsType<FXParticleEmissionVolumeBox>(template.EmissionVolume);
        Assert.Equal(1.5f * FeetPerMeter, volume.HalfSize.X, 4);

        // 60/s = 2 particles per tick.
        Assert.Equal(2, template.BurstCount.High);
        Assert.Equal(0, template.BurstDelay.Low);

        // Alpha: initial 1@0, curve keys 0.8@8 and 0@30.
        Assert.Equal(0.8f, template.Alpha.AlphaKeyframes[1].Value.Low, 3);
        Assert.Equal(8u, template.Alpha.AlphaKeyframes[1].Time);
        Assert.Equal(0, template.Alpha.AlphaKeyframes[2].Value.Low);
        Assert.Equal(30u, template.Alpha.AlphaKeyframes[2].Time);
    }

    [Fact]
    public void MaterializationIsDeterministic()
    {
        var effect = CreateEffect(CreateSystem("determinism"));

        var first = ImportedParticleEffectMaterializer.Materialize(effect);
        var second = ImportedParticleEffectMaterializer.Materialize(effect);

        var a = first.Systems[0].Template;
        var b = second.Systems[0].Template;

        Assert.Equal(a.BurstCount.High, b.BurstCount.High);
        Assert.Equal(a.Lifetime.High, b.Lifetime.High);
        Assert.Equal(a.Colors.ColorKeyframes[0], b.Colors.ColorKeyframes[0]);
        Assert.Equal(a.Alpha.AlphaKeyframes[0], b.Alpha.AlphaKeyframes[0]);
        Assert.Equal(first.Systems[0].EmitterTransform, second.Systems[0].EmitterTransform);
    }

    [Fact]
    public void ResolverSuppliesTheTextureReference()
    {
        var effect = CreateEffect(CreateSystem("textured"));
        var resolved = false;

        var materialized = ImportedParticleEffectMaterializer.Materialize(
            effect,
            name =>
            {
                resolved = true;
                return new LazyAssetReference<Panopticon.Graphics.TextureAsset>(name, static () => null!);
            });

        Assert.True(resolved);
        Assert.Equal("smoke.tga", materialized.Systems[0].Template.ParticleTexture.Name);
    }

    [Fact]
    public void MissingTextureFailsBeforeLiveSystemCreation()
    {
        var effect = CreateEffect(CreateSystem("untextured", textureName: null));

        var exception = Assert.Throws<NotSupportedException>(
            () => ImportedParticleEffectMaterializer.Materialize(effect));

        Assert.Contains("fx_proof.untextured", exception.Message);
        Assert.Contains("texture", exception.Message);
    }

    [Fact]
    public void GravityModifierIsDeferred()
    {
        var effect = CreateEffect(CreateSystem(
            "gravity",
            modifiers: new[] { ImportedParticleModifier.CreateGravity(9.8f, Vector3.UnitZ, 0) }));

        var exception = Assert.Throws<NotSupportedException>(
            () => ImportedParticleEffectMaterializer.Materialize(effect));

        Assert.Contains("gravity", exception.Message);
    }

    [Fact]
    public void UnsupportedModifierFailsWithFeatureName()
    {
        var effect = CreateEffect(CreateSystem(
            "flipbook",
            modifiers: new[] { ImportedParticleModifier.CreateUnsupported("NiFlipController flipbook") }));

        var exception = Assert.Throws<NotSupportedException>(
            () => ImportedParticleEffectMaterializer.Materialize(effect));

        Assert.Contains("NiFlipController flipbook", exception.Message);
    }

    [Fact]
    public void MissingBirthRateFails()
    {
        var effect = CreateEffect(CreateSystem("slots", useBirthRate: false, birthRate: 0));

        var exception = Assert.Throws<NotSupportedException>(
            () => ImportedParticleEffectMaterializer.Materialize(effect));

        Assert.Contains("birth rate", exception.Message);
    }

    [Fact]
    public void ColorCurveBeyondEightKeySlotsFails()
    {
        var keys = new List<ImportedParticleColorKey>();
        for (var i = 0; i < 8; i++)
        {
            keys.Add(new ImportedParticleColorKey(i * 0.1f, 1, 1, 1, 1));
        }

        var effect = CreateEffect(CreateSystem(
            "wide-curve",
            modifiers: new[] { ImportedParticleModifier.CreateColorOverLife(keys) }));

        var exception = Assert.Throws<NotSupportedException>(
            () => ImportedParticleEffectMaterializer.Materialize(effect));

        Assert.Contains("eight", exception.Message);
    }

    [Fact]
    public void BoxEmitterWithRotatedConeAxisFails()
    {
        var effect = CreateEffect(CreateSystem(
            "twisted-box",
            velocity: new ImportedParticleVelocity(ImportedParticleVelocityKind.Cone, Vector3.UnitY, 5, 5, 0.3f),
            emitter: new ImportedParticleEmitter(ImportedParticleEmitterKind.Box, new Vector3(3, 3, 0)),
            localTransform: Matrix4x4.CreateRotationZ(0.7f)));

        var exception = Assert.Throws<NotSupportedException>(
            () => ImportedParticleEffectMaterializer.Materialize(effect));

        Assert.Contains("box emitter", exception.Message);
    }

    private static ImportedParticleEffect CreateEffect(params ImportedParticleSystem[] systems)
    {
        return new ImportedParticleEffect("fx_proof", systems, Array.Empty<string>());
    }

    private static ImportedParticleSystem CreateSystem(
        string name,
        ImportedParticleVelocity? velocity = null,
        float birthRate = 300,
        bool useBirthRate = true,
        float emissionStop = 0.5f,
        float lifetime = 1,
        float lifetimeVariation = 0,
        float initialSize = 5,
        ImportedParticleEmitter? emitter = null,
        IReadOnlyList<ImportedParticleModifier>? modifiers = null,
        IReadOnlyList<ImportedParticleAlphaKey>? alphaKeys = null,
        string? textureName = "smoke.tga",
        bool alphaBlended = false,
        Matrix4x4 localTransform = default)
    {
        localTransform = localTransform == default ? Matrix4x4.Identity : localTransform;

        return new ImportedParticleSystem(
            name,
            localTransform,
            birthRate,
            useBirthRate,
            emissionStartSeconds: 0,
            emissionStopSeconds: emissionStop,
            particleLifetimeSeconds: lifetime,
            particleLifetimeVariationSeconds: lifetimeVariation,
            initialColor: new Vector3(1, 1, 1),
            initialAlpha: 1,
            initialSize: initialSize,
            velocity: velocity ?? new ImportedParticleVelocity(ImportedParticleVelocityKind.Spherical, Vector3.UnitZ, 10, 10, 0),
            emitter: emitter ?? ImportedParticleEmitter.Point(),
            modifiers: modifiers ?? Array.Empty<ImportedParticleModifier>(),
            alphaKeys: alphaKeys ?? Array.Empty<ImportedParticleAlphaKey>(),
            textureName: textureName,
            materialName: "SmokeMaterial",
            alphaBlended: alphaBlended);
    }
}
