using System;
using System.IO;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Nif;
using Panopticon.FileFormats.Nif;
using Panopticon.Tests.Data.Nif;
using Xunit;
using NifParticleTests = Panopticon.Tests.Data.Nif.NifParticleTests;
using NifTestBlocks = Panopticon.Tests.Data.Nif.NifTestBlocks;
using NifTestGeometry = Panopticon.Tests.Data.Nif.NifTestGeometry;
using NifTestProperties = Panopticon.Tests.Data.Nif.NifTestProperties;
using NifTestPropertyAnimation = Panopticon.Tests.Data.Nif.NifTestPropertyAnimation;

namespace Panopticon.Tests.Content.Importing;

public sealed class NifParticleEffectConverterTests
{
    private static NifFile Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return NifFile.Read(stream);
    }

    private static ImportedParticleEffect Convert(NifFile file, string effectName = "proof")
    {
        return NifParticleEffectConverter.Convert(file, effectName)
            ?? throw new InvalidOperationException("Expected a converted effect.");
    }

    [Fact]
    public void ConvertsSingleParticleSystem()
    {
        var effect = Convert(Read(NifParticleTests.ParticleFileBytes()));

        Assert.Equal("proof", effect.Name);
        var system = Assert.Single(effect.Systems);
        Assert.Equal("sparks", system.Name);
        Assert.Equal("mesh", Assert.Single(effect.HelperSubObjectNames));
        Assert.Equal(300.0f, system.BirthRatePerSecond, 5);
        Assert.True(system.UseBirthRate);
        Assert.Equal(0.0f, system.EmissionStartSeconds, 5);
        Assert.Equal(0.15f, system.EmissionStopSeconds, 5);
        Assert.Equal(0.5f, system.ParticleLifetimeSeconds, 5);
        Assert.Equal(0.0f, system.ParticleLifetimeVariationSeconds, 5);
        Assert.Equal(new Vector3(1, 1, 1), system.InitialColor);
        Assert.Equal(1.0f, system.InitialAlpha, 5);
        Assert.Equal(5.0f, system.InitialSize, 5);

        // Declination and planar variation both cover pi: full sphere sampling.
        Assert.Equal(ImportedParticleVelocityKind.Spherical, system.Velocity.Kind);
        Assert.Equal(119.0f, system.Velocity.SpeedLow, 5);
        Assert.Equal(121.0f, system.Velocity.SpeedHigh, 5);

        Assert.Equal(ImportedParticleEmitterKind.Point, system.Emitter.Kind);
        Assert.Equal(Vector3.Zero, system.Emitter.Dimensions);

        Assert.Equal(2, system.Modifiers.Count);
        var colorOverLife = system.Modifiers[0];
        Assert.Equal(ImportedParticleModifierKind.ColorOverLife, colorOverLife.Kind);
        Assert.Equal(2, colorOverLife.ColorKeys.Count);
        Assert.Equal(new ImportedParticleColorKey(0.0f, 1, 1, 1, 1), colorOverLife.ColorKeys[0]);
        Assert.Equal(new ImportedParticleColorKey(1.0f, 1, 0.5f, 0.1f, 0.0f), colorOverLife.ColorKeys[1]);
        var growFade = system.Modifiers[1];
        Assert.Equal(ImportedParticleModifierKind.GrowFade, growFade.Kind);
        Assert.Equal(0.1f, growFade.GrowSeconds, 5);
        Assert.Equal(0.2f, growFade.FadeSeconds, 5);

        Assert.Null(system.TextureName);
        Assert.Null(system.MaterialName);
        Assert.False(system.AlphaBlended);
        Assert.Empty(system.AlphaKeys);
        Assert.Equal(0.0f, system.LocalTransform.Translation.X, 5);
        Assert.Equal(0.0f, system.LocalTransform.Translation.Y, 5);
        Assert.Equal(0.0f, system.LocalTransform.Translation.Z, 5);
    }

    [Fact]
    public void ReturnsNullWhenFileHasNoParticles()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var stripsType = builder.AddBlockType("NiTriStrips");
        var stripsDataType = builder.AddBlockType("NiTriStripsData");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1]));
        builder.AddBlock(stripsType, NifTestGeometry.Shape(name: "mesh", data: 2));
        builder.AddBlock(stripsDataType, NifParticleTests.TriStripsData(2, [[0, 1, 2, 3]]));
        builder.AddTrailing(NifTestBlocks.Footer());

        Assert.Null(NifParticleEffectConverter.Convert(Read(builder.Build()), "proof"));
    }

    [Fact]
    public void ConvertsLocalTransformToEngineAxesInSourceUnits()
    {
        var file = BuildSingleSystemFile(particlesBody: NifTestGeometry.Shape(
            name: "sparks", data: 2, controller: 3, translation: (0, 10, 0)));
        var system = Convert(file).Systems[0];

        // Source +Y maps to engine -X; the translation stays in source units.
        Assert.Equal(-10.0f, system.LocalTransform.Translation.X, 5);
        Assert.Equal(0.0f, system.LocalTransform.Translation.Y, 5);
        Assert.Equal(0.0f, system.LocalTransform.Translation.Z, 5);
    }

    [Fact]
    public void OrdersSystemsByBlockIndexAndFallsBackToIndexedNames()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var particlesType = builder.AddBlockType("NiParticles");
        var particlesDataType = builder.AddBlockType("NiParticlesData");
        var controllerType = builder.AddBlockType("NiParticleSystemController");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1, 4]));
        builder.AddBlock(particlesType, NifTestGeometry.Shape(name: "alpha", data: 2, controller: 3));
        builder.AddBlock(particlesDataType, NifParticleTests.ParticlesData());
        builder.AddBlock(controllerType, NifParticleTests.ParticleSystemController(target: 1, emitter: 0));
        builder.AddBlock(particlesType, NifTestGeometry.Shape(name: "", data: 5, controller: 6));
        builder.AddBlock(particlesDataType, NifParticleTests.ParticlesData());
        builder.AddBlock(controllerType, NifParticleTests.ParticleSystemController(target: 4, emitter: 0));
        builder.AddTrailing(NifTestBlocks.Footer());

        var effect = Convert(Read(builder.Build()));

        Assert.Equal(2, effect.Systems.Count);
        Assert.Equal("alpha", effect.Systems[0].Name);
        Assert.Equal("Particles4", effect.Systems[1].Name);
    }

    [Fact]
    public void DuplicateSystemNamesGetBlockIndexSuffix()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var particlesType = builder.AddBlockType("NiParticles");
        var particlesDataType = builder.AddBlockType("NiParticlesData");
        var controllerType = builder.AddBlockType("NiParticleSystemController");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1, 4]));
        builder.AddBlock(particlesType, NifTestGeometry.Shape(name: "SuperSpray", data: 2, controller: 3));
        builder.AddBlock(particlesDataType, NifParticleTests.ParticlesData());
        builder.AddBlock(controllerType, NifParticleTests.ParticleSystemController(target: 1, emitter: 0));
        builder.AddBlock(particlesType, NifTestGeometry.Shape(name: "SuperSpray", data: 5, controller: 6));
        builder.AddBlock(particlesDataType, NifParticleTests.ParticlesData());
        builder.AddBlock(controllerType, NifParticleTests.ParticleSystemController(target: 4, emitter: 0));
        builder.AddTrailing(NifTestBlocks.Footer());

        var effect = Convert(Read(builder.Build()));

        Assert.Equal(2, effect.Systems.Count);
        Assert.Equal("SuperSpray", effect.Systems[0].Name);
        Assert.Equal("SuperSpray#4", effect.Systems[1].Name);
    }

    [Fact]
    public void NarrowDeclinationConvertsToConeVelocity()
    {
        var file = BuildSingleSystemFile(controllerBody: NifParticleTests.ParticleSystemController(
            target: 1, emitter: 0, speed: 7.5f, speedVariation: 1.875f,
            declination: 0.0f, declinationVariation: 0.349f, planarAngleVariation: 0.785f));
        var velocity = Convert(file).Systems[0].Velocity;

        Assert.Equal(ImportedParticleVelocityKind.Cone, velocity.Kind);
        // Initial normal (1,0,0) maps to engine +Y.
        Assert.Equal(0.0f, velocity.Axis.X, 5);
        Assert.Equal(1.0f, velocity.Axis.Y, 5);
        Assert.Equal(0.0f, velocity.Axis.Z, 5);
        Assert.Equal(5.625f, velocity.SpeedLow, 4);
        Assert.Equal(9.375f, velocity.SpeedHigh, 4);
        Assert.Equal(0.349f, velocity.ConeAngle, 4);
    }

    [Theory]
    [InlineData(2, 0, 0, ImportedParticleEmitterKind.Line)]
    [InlineData(2, 3, 0, ImportedParticleEmitterKind.Box)]
    [InlineData(2, 3, 4, ImportedParticleEmitterKind.Box)]
    public void ClassifiesEmitterDimensions(int x, int y, int z, ImportedParticleEmitterKind expected)
    {
        var file = BuildSingleSystemFile(controllerBody: NifParticleTests.ParticleSystemController(
            target: 1, emitter: 0, emitterDimensions: (x, y, z)));
        var emitter = Convert(file).Systems[0].Emitter;

        Assert.Equal(expected, emitter.Kind);
        Assert.Equal(new Vector3(x, y, z), emitter.Dimensions);
    }

    [Fact]
    public void ConvertsGravityModifierToSourceMarker()
    {
        var file = BuildSingleSystemFile(
            controllerBody: NifParticleTests.ParticleSystemController(target: 1, emitter: 0, particleModifier: 4),
            gravityBody: NifParticleTests.Gravity(controller: 3, force: 9.8f));
        var modifier = Convert(file).Systems[0].Modifiers[0];

        Assert.Equal(ImportedParticleModifierKind.Gravity, modifier.Kind);
        Assert.False(modifier.IsMaterializable);
        Assert.Equal(9.8f, modifier.GravityForce, 5);
        Assert.Equal(0.0f, modifier.GravityDirection.X, 5);
        Assert.Equal(0.0f, modifier.GravityDirection.Y, 5);
        Assert.Equal(-1.0f, modifier.GravityDirection.Z, 5);
    }

    [Fact]
    public void FoldsAlphaControllerKeysFromAlphaProperty()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var particlesType = builder.AddBlockType("NiParticles");
        var particlesDataType = builder.AddBlockType("NiParticlesData");
        var controllerType = builder.AddBlockType("NiParticleSystemController");
        var alphaPropertyType = builder.AddBlockType("NiAlphaProperty");
        var alphaControllerType = builder.AddBlockType("NiAlphaController");
        var floatDataType = builder.AddBlockType("NiFloatData");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1]));
        builder.AddBlock(particlesType, NifTestGeometry.Shape(
            name: "sparks", data: 2, controller: 3, properties: [4]));
        builder.AddBlock(particlesDataType, NifParticleTests.ParticlesData());
        builder.AddBlock(controllerType, NifParticleTests.ParticleSystemController(target: 1, emitter: 0));
        builder.AddBlock(alphaPropertyType, AlphaPropertyWithController(controller: 5));
        builder.AddBlock(alphaControllerType, NifTestPropertyAnimation.AlphaController(
            data: 6, stopTime: 2.0f, target: 4));
        builder.AddBlock(floatDataType, NifTestPropertyAnimation.FloatData(
            [(0.0f, 1.0f, null, null), (2.0f, 0.0f, null, null)]));
        builder.AddTrailing(NifTestBlocks.Footer());

        var system = Convert(Read(builder.Build())).Systems[0];

        Assert.True(system.AlphaBlended);
        Assert.Equal(2, system.AlphaKeys.Count);
        Assert.Equal(new ImportedParticleAlphaKey(0.0f, 1.0f), system.AlphaKeys[0]);
        Assert.Equal(new ImportedParticleAlphaKey(2.0f, 0.0f), system.AlphaKeys[1]);
    }

    [Fact]
    public void FoldsSingleMaterialColorChannelIntoColorCurve()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var particlesType = builder.AddBlockType("NiParticles");
        var particlesDataType = builder.AddBlockType("NiParticlesData");
        var controllerType = builder.AddBlockType("NiParticleSystemController");
        var materialType = builder.AddBlockType("NiMaterialProperty");
        var materialControllerType = builder.AddBlockType("NiMaterialColorController");
        var posDataType = builder.AddBlockType("NiPosData");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1]));
        builder.AddBlock(particlesType, NifTestGeometry.Shape(
            name: "sparks", data: 2, controller: 3, properties: [4]));
        builder.AddBlock(particlesDataType, NifParticleTests.ParticlesData());
        builder.AddBlock(controllerType, NifParticleTests.ParticleSystemController(target: 1, emitter: 0));
        builder.AddBlock(materialType, MaterialPropertyWithController(name: "mat", controller: 5));
        builder.AddBlock(materialControllerType, NifTestPropertyAnimation.MaterialColorController(
            data: 6, target: 4, startTime: 0.25f));
        builder.AddBlock(posDataType, NifTestPropertyAnimation.PosData(
            [(0.25f, (1, 1, 1), null, null), (1.25f, (0.5f, 0.5f, 0.5f), null, null)]));
        builder.AddTrailing(NifTestBlocks.Footer());

        var effect = Convert(Read(builder.Build()));

        var curve = Assert.Single(effect.Systems[0].Modifiers, m => m.Kind == ImportedParticleModifierKind.ColorOverLife);
        Assert.Equal(
            new ImportedParticleColorKey(0.0f, 1f, 1f, 1f, 1f), curve.ColorKeys[0]);
        Assert.Equal(
            new ImportedParticleColorKey(1.0f, 0.5f, 0.5f, 0.5f, 1f), curve.ColorKeys[1]);
    }

    [Fact]
    public void SkipsSharedMaterialColorCurveWhenSystemHasItsOwnCurve()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var particlesType = builder.AddBlockType("NiParticles");
        var particlesDataType = builder.AddBlockType("NiParticlesData");
        var controllerType = builder.AddBlockType("NiParticleSystemController");
        var materialType = builder.AddBlockType("NiMaterialProperty");
        var materialControllerType = builder.AddBlockType("NiMaterialColorController");
        var posDataType = builder.AddBlockType("NiPosData");
        var colorModifierType = builder.AddBlockType("NiParticleColorModifier");
        var colorDataType = builder.AddBlockType("NiColorData");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1]));
        builder.AddBlock(particlesType, NifTestGeometry.Shape(
            name: "sparks", data: 2, controller: 3, properties: [4]));
        builder.AddBlock(particlesDataType, NifParticleTests.ParticlesData());
        builder.AddBlock(controllerType, NifParticleTests.ParticleSystemController(
            target: 1, emitter: 0, particleModifier: 7));
        builder.AddBlock(materialType, MaterialPropertyWithController(name: "mat", controller: 5));
        builder.AddBlock(materialControllerType, NifTestPropertyAnimation.MaterialColorController(
            data: 6, target: 4));
        builder.AddBlock(posDataType, NifTestPropertyAnimation.PosData(
            [(0.0f, (1, 1, 1), null, null)]));
        builder.AddBlock(colorModifierType, NifParticleTests.ParticleColorModifier(controller: 3, colorData: 8));
        builder.AddBlock(colorDataType, NifParticleTests.ColorData(
            (0.0f, 0.25f, 0.5f, 0.75f, 1f), (1.0f, 0.25f, 0.5f, 0.75f, 1f)));
        builder.AddTrailing(NifTestBlocks.Footer());

        var effect = Convert(Read(builder.Build()));

        // The system's own NiParticleColorModifier curve owns the color; the shared
        // material channel belongs to the helper mesh and is not folded.
        var curve = Assert.Single(effect.Systems[0].Modifiers, m => m.Kind == ImportedParticleModifierKind.ColorOverLife);
        Assert.Equal(2, curve.ColorKeys.Count);
        Assert.All(curve.ColorKeys, key => Assert.Equal(0.25f, key.R));
    }

    [Fact]
    public void RejectsMultipleAnimatedMaterialChannelsWithoutOwnCurve()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var particlesType = builder.AddBlockType("NiParticles");
        var particlesDataType = builder.AddBlockType("NiParticlesData");
        var controllerType = builder.AddBlockType("NiParticleSystemController");
        var materialType = builder.AddBlockType("NiMaterialProperty");
        var materialControllerType = builder.AddBlockType("NiMaterialColorController");
        var posDataType = builder.AddBlockType("NiPosData");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1]));
        builder.AddBlock(particlesType, NifTestGeometry.Shape(
            name: "sparks", data: 2, controller: 3, properties: [4]));
        builder.AddBlock(particlesDataType, NifParticleTests.ParticlesData());
        builder.AddBlock(controllerType, NifParticleTests.ParticleSystemController(target: 1, emitter: 0));
        builder.AddBlock(materialType, MaterialPropertyWithController(name: "mat", controller: 5));
        builder.AddBlock(materialControllerType, NifTestPropertyAnimation.MaterialColorController(
            data: 7, target: 4, next: 6));
        builder.AddBlock(materialControllerType, NifTestPropertyAnimation.MaterialColorController(
            data: 7, target: 4));
        builder.AddBlock(posDataType, NifTestPropertyAnimation.PosData(
            [(0.0f, (1, 1, 1), null, null)]));
        builder.AddTrailing(NifTestBlocks.Footer());

        var exception = Assert.Throws<NotSupportedException>(
            () => Convert(Read(builder.Build())));

        Assert.Contains("2 animated color channels", exception.Message);
        Assert.Contains("block 1", exception.Message);
    }

    [Fact]
    public void ForeignControllersSplicedIntoTheChainAreSkipped()
    {
        // fx_explosion_1 shape: the particle controller's Next chain runs into a
        // NiMaterialColorController animating the helper mesh's material; the
        // walk must skip foreign targets instead of rejecting the system.
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var particlesType = builder.AddBlockType("NiParticles");
        var particlesDataType = builder.AddBlockType("NiParticlesData");
        var controllerType = builder.AddBlockType("NiParticleSystemController");
        var materialControllerType = builder.AddBlockType("NiMaterialColorController");
        var foreignMaterialType = builder.AddBlockType("NiMaterialProperty");
        var posDataType = builder.AddBlockType("NiPosData");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1]));
        builder.AddBlock(particlesType, NifTestGeometry.Shape(
            name: "sparks", data: 2, controller: 3));
        builder.AddBlock(particlesDataType, NifParticleTests.ParticlesData());
        builder.AddBlock(controllerType, NifParticleTests.ParticleSystemController(
            target: 1, emitter: 0, next: 4));
        builder.AddBlock(materialControllerType, NifTestPropertyAnimation.MaterialColorController(
            data: 6, target: 5));
        builder.AddBlock(foreignMaterialType, NifTestProperties.Material(name: "meshMaterial"));
        builder.AddBlock(posDataType, NifTestPropertyAnimation.PosData(
            [(0.0f, (1, 1, 1), null, null)]));
        builder.AddTrailing(NifTestBlocks.Footer());

        var system = Convert(Read(builder.Build())).Systems[0];

        Assert.Empty(system.AlphaKeys);
    }

    [Fact]
    public void RejectsParticlesWithoutParticleSystemController()
    {
        var file = BuildSingleSystemFile(particlesBody: NifTestGeometry.Shape(name: "sparks", data: 2));

        var exception = Assert.Throws<NotSupportedException>(() => Convert(file));

        Assert.Contains("no NiParticleSystemController", exception.Message);
    }

    [Fact]
    public void RejectsMeshEmission()
    {
        // The emitter reference targets the NiParticles block itself, which
        // the converter must reject as mesh emission.
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var particlesType = builder.AddBlockType("NiParticles");
        var particlesDataType = builder.AddBlockType("NiParticlesData");
        var controllerType = builder.AddBlockType("NiParticleSystemController");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1]));
        builder.AddBlock(particlesType, NifTestGeometry.Shape(name: "sparks", data: 2, controller: 3));
        builder.AddBlock(particlesDataType, NifParticleTests.ParticlesData());
        builder.AddBlock(controllerType, NifParticleTests.ParticleSystemController(target: 1, emitter: 1));
        builder.AddTrailing(NifTestBlocks.Footer());

        var exception = Assert.Throws<NotSupportedException>(() => Convert(Read(builder.Build())));

        Assert.Contains("mesh emission", exception.Message);
    }

    [Fact]
    public void InterchangeRejectsDuplicateSystemNames()
    {
        var system = BuildInterchangeSystem("sparks");

        Assert.Throws<ArgumentException>(() => new ImportedParticleEffect("proof", [system, system], []));
    }

    [Fact]
    public void InterchangeRejectsUnsortedColorKeys()
    {
        Assert.Throws<ArgumentException>(() => ImportedParticleModifier.CreateColorOverLife(
        [
            new ImportedParticleColorKey(1.0f, 1, 1, 1, 1),
            new ImportedParticleColorKey(0.0f, 0, 0, 0, 0),
        ]));
    }

    [Fact]
    public void InterchangeRejectsInvertedSpeedRange()
    {
        Assert.Throws<ArgumentException>(() => new ImportedParticleVelocity(
            ImportedParticleVelocityKind.Cone, Vector3.UnitY, 10.0f, 5.0f, 0.1f));
    }

    [Fact]
    public void InterchangeRejectsAmbiguousPointEmitter()
    {
        Assert.Throws<ArgumentException>(() => new ImportedParticleEmitter(
            ImportedParticleEmitterKind.Point, new Vector3(1, 0, 0)));
    }

    [Fact]
    public void InterchangeRejectsFollowAttachmentWithoutBone()
    {
        Assert.Throws<ArgumentException>(() => new ImportedParticleAttachment(
            null, true, Vector3.Zero, 1.0f, false));
    }

    [Fact]
    public void InterchangeRejectsNegativeGrowFade()
    {
        Assert.Throws<ArgumentException>(() => ImportedParticleModifier.CreateGrowFade(-1.0f, 0.0f));
    }

    private static NifFile BuildSingleSystemFile(
        byte[]? particlesBody = null,
        byte[]? particlesDataBody = null,
        byte[]? controllerBody = null,
        byte[]? gravityBody = null)
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var particlesType = builder.AddBlockType("NiParticles");
        var particlesDataType = builder.AddBlockType("NiParticlesData");
        var controllerType = builder.AddBlockType("NiParticleSystemController");
        var gravityType = builder.AddBlockType("NiGravity");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1]));
        builder.AddBlock(particlesType, particlesBody ?? NifTestGeometry.Shape(
            name: "sparks", data: 2, controller: 3));
        builder.AddBlock(particlesDataType, particlesDataBody ?? NifParticleTests.ParticlesData());
        builder.AddBlock(controllerType, controllerBody ?? NifParticleTests.ParticleSystemController(
            target: 1, emitter: 0, particleModifier: gravityBody is null ? -1 : 4));
        builder.AddBlock(gravityType, gravityBody ?? NifParticleTests.Gravity(controller: 3, force: 0.0f));
        builder.AddTrailing(NifTestBlocks.Footer());
        return Read(builder.Build());
    }

    private static ImportedParticleSystem BuildInterchangeSystem(string name)
    {
        return new ImportedParticleSystem(
            name,
            Matrix4x4.Identity,
            10.0f,
            true,
            0.0f,
            1.0f,
            0.5f,
            0.0f,
            Vector3.One,
            1.0f,
            1.0f,
            new ImportedParticleVelocity(ImportedParticleVelocityKind.Cone, Vector3.UnitY, 1.0f, 2.0f, 0.1f),
            ImportedParticleEmitter.Point(),
            [ImportedParticleModifier.CreateGrowFade(0.1f, 0.2f)],
            [],
            null,
            null,
            false);
    }

    private static byte[] AlphaPropertyWithController(int controller, ushort flags = 0x12ED, byte threshold = 100)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);
        body.WriteSizedString("");
        NifTestBlocks.WriteRefList(body, []);
        body.WriteInt32(controller);
        body.WriteUInt16(flags);
        body.WriteByte(threshold);
        return stream.ToArray();
    }

    private static byte[] MaterialPropertyWithController(string name, int controller)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);
        body.WriteSizedString(name);
        NifTestBlocks.WriteRefList(body, []);
        body.WriteInt32(controller);
        for (var i = 0; i < 4; i++)
        {
            body.WriteSingle(0.5f);
            body.WriteSingle(0.5f);
            body.WriteSingle(0.5f);
        }

        body.WriteSingle(10.0f);
        body.WriteSingle(1.0f);
        return stream.ToArray();
    }
}
