using System;
using System.IO;
using System.Numerics;
using Panopticon.FileFormats.Nif;
using Xunit;
using NifTestBlocks = Panopticon.Tests.Data.Nif.NifTestBlocks;
using NifTestGeometry = Panopticon.Tests.Data.Nif.NifTestGeometry;
using NifTestKeyframe = Panopticon.Tests.Data.Nif.NifTestKeyframe;

namespace Panopticon.Tests.Data.Nif;

public sealed class NifParticleTests
{
    internal static NifFile Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return NifFile.Read(stream);
    }

    internal static void WriteGeometryDataBase(
        NifBodyWriter body,
        int numVertices)
    {
        body.WriteUInt32(0);
        body.WriteUInt16((ushort)numVertices);
        body.WriteByte(0);
        body.WriteByte(0);
        body.WriteByte(1);
        for (var i = 0; i < numVertices; i++)
        {
            NifTestBlocks.WriteVector3(body, (i * 1.0f, i * 2.0f, i * 3.0f));
        }

        body.WriteUInt16(0);
        body.WriteByte(0);
        NifTestBlocks.WriteVector3(body, (0, 0, 0));
        body.WriteSingle(1.0f);
        body.WriteByte(0);
        body.WriteUInt16(0);
    }

    internal static byte[] ParticlesData(
        int numVertices = 3,
        bool hasRadii = true,
        ushort activeCount = 3,
        bool hasSizes = true,
        bool hasRotations = true)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        WriteGeometryDataBase(body, numVertices);
        body.WriteByte((byte)(hasRadii ? 1 : 0));
        if (hasRadii)
        {
            for (var i = 0; i < numVertices; i++)
            {
                body.WriteSingle(0.5f);
            }
        }

        body.WriteUInt16(activeCount);
        body.WriteByte((byte)(hasSizes ? 1 : 0));
        if (hasSizes)
        {
            for (var i = 0; i < numVertices; i++)
            {
                body.WriteSingle(1.0f + i);
            }
        }

        body.WriteByte((byte)(hasRotations ? 1 : 0));
        if (hasRotations)
        {
            for (var i = 0; i < numVertices; i++)
            {
                body.WriteSingle(1.0f);
                body.WriteSingle(0.0f);
                body.WriteSingle(0.0f);
                body.WriteSingle(0.0f);
            }
        }

        return stream.ToArray();
    }

    internal static byte[] ParticleSystemController(
        int target,
        int emitter,
        int particleModifier = -1,
        int emitterModifier = -1,
        int collider = -1,
        ushort particleSlotCount = 2,
        float stopTime = 1.5f,
        float speed = 120.0f,
        float birthRate = 300.0f,
        float lifetime = 0.5f,
        float speedVariation = 1.0f,
        float declination = (float)Math.PI,
        float declinationVariation = (float)Math.PI,
        float planarAngleVariation = (float)Math.PI,
        float initialSize = 5.0f,
        (float X, float Y, float Z) emitterDimensions = default)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);
        NifTestKeyframe.WriteTimeControllerPrefix(body, -1, 0x2C, 1.0f, 0.0f, 0.0f, stopTime, target);
        body.WriteSingle(speed);
        body.WriteSingle(speedVariation);
        body.WriteSingle(declination);
        body.WriteSingle(declinationVariation);
        body.WriteSingle((float)Math.PI);
        body.WriteSingle(planarAngleVariation);
        NifTestBlocks.WriteVector3(body, (1, 0, 0));
        body.WriteSingle(1.0f);
        body.WriteSingle(1.0f);
        body.WriteSingle(1.0f);
        body.WriteSingle(1.0f);
        body.WriteSingle(initialSize);
        body.WriteSingle(0.0f);
        body.WriteSingle(0.15f);
        body.WriteByte(0);
        body.WriteSingle(birthRate);
        body.WriteSingle(lifetime);
        body.WriteSingle(0.0f);
        body.WriteByte(1);
        body.WriteByte(0);
        NifTestBlocks.WriteVector3(body, emitterDimensions);
        body.WriteInt32(emitter);
        body.WriteUInt16(0);
        body.WriteSingle(1.0f);
        body.WriteByte(1);
        body.WriteInt32(0);
        body.WriteInt32(0);
        body.WriteByte(0);
        body.WriteUInt16(particleSlotCount);
        body.WriteByte((byte)particleSlotCount);
        body.WriteByte(0);
        body.WriteBytes(new byte[particleSlotCount * NifParticleSystemController.ParticleInfoSize]);
        body.WriteInt32(emitterModifier);
        body.WriteInt32(particleModifier);
        body.WriteInt32(collider);
        body.WriteByte(0);
        return stream.ToArray();
    }

    internal static byte[] ParticleColorModifier(
        int controller,
        int colorData,
        int next = -1)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);
        body.WriteInt32(next);
        body.WriteInt32(controller);
        body.WriteInt32(colorData);
        return stream.ToArray();
    }

    internal static byte[] ParticleGrowFade(
        int controller,
        float growTime,
        float fadeTime,
        int next = -1)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);
        body.WriteInt32(next);
        body.WriteInt32(controller);
        body.WriteSingle(growTime);
        body.WriteSingle(fadeTime);
        return stream.ToArray();
    }

    internal static byte[] Gravity(
        int controller,
        float force,
        uint forceType = 0,
        int next = -1)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);
        body.WriteInt32(next);
        body.WriteInt32(controller);
        body.WriteSingle(0.0f);
        body.WriteSingle(force);
        body.WriteUInt32(forceType);
        NifTestBlocks.WriteVector3(body, (0, 0, 0));
        NifTestBlocks.WriteVector3(body, (0, 0, -1));
        return stream.ToArray();
    }

    internal static byte[] ColorData(params (float Time, float R, float G, float B, float A)[] keys)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);
        body.WriteUInt32((uint)keys.Length);
        if (keys.Length > 0)
        {
            body.WriteUInt32((uint)NifKeyType.Linear);
            foreach (var (time, r, g, b, a) in keys)
            {
                body.WriteSingle(time);
                body.WriteSingle(r);
                body.WriteSingle(g);
                body.WriteSingle(b);
                body.WriteSingle(a);
            }
        }

        return stream.ToArray();
    }

    internal static byte[] StencilProperty(
        string name = "stencil",
        ushort flags = 3,
        byte enabled = 1)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);
        body.WriteSizedString(name);
        NifTestBlocks.WriteRefList(body, []);
        body.WriteInt32(-1);
        body.WriteUInt16(flags);
        body.WriteByte(enabled);
        body.WriteBytes(new byte[26]);
        return stream.ToArray();
    }

    internal static byte[] TriStripsData(
        ushort numTriangles,
        int[][] strips,
        int numVertices = 4)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        WriteGeometryDataBase(body, numVertices);
        body.WriteUInt16(numTriangles);
        body.WriteUInt16((ushort)strips.Length);
        foreach (var strip in strips)
        {
            body.WriteUInt16((ushort)strip.Length);
        }

        body.WriteByte(1);
        foreach (var strip in strips)
        {
            foreach (var point in strip)
            {
                body.WriteUInt16((ushort)point);
            }
        }

        return stream.ToArray();
    }

    internal static byte[] CookedGeometryWithoutExtras(
        string name,
        int data)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);
        body.WriteSizedString(name);
        body.WriteUInt32(0xFFFFFFFF);
        body.WriteUInt16(0);
        NifTestBlocks.WriteVector3(body, (0, 0, 0));
        NifTestBlocks.WriteMatrix33Identity(body);
        body.WriteSingle(1.0f);
        NifTestBlocks.WriteRefList(body, []);
        body.WriteInt32(-1);
        body.WriteInt32(data);
        body.WriteInt32(-1);
        body.WriteByte(0);
        return stream.ToArray();
    }

    internal static byte[] ParticleFileBytes(
        byte[]? controller = null,
        byte[]? particlesData = null,
        byte[]? colorModifier = null,
        byte[]? growFade = null,
        byte[]? colorData = null,
        byte[]? gravity = null,
        byte[]? stencil = null,
        byte[]? strips = null,
        byte[]? stripsData = null,
        bool cookedExtras = false)
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var particlesType = builder.AddBlockType("NiParticles");
        var particlesDataType = builder.AddBlockType("NiParticlesData");
        var controllerType = builder.AddBlockType("NiParticleSystemController");
        var colorModifierType = builder.AddBlockType("NiParticleColorModifier");
        var growFadeType = builder.AddBlockType("NiParticleGrowFade");
        var colorDataType = builder.AddBlockType("NiColorData");
        var gravityType = builder.AddBlockType("NiGravity");
        var stencilType = builder.AddBlockType("NiStencilProperty");
        var stripsType = builder.AddBlockType("NiTriStrips");
        var stripsDataType = builder.AddBlockType("NiTriStripsData");

        var children = new[] { 1, 9 };

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: children));
        builder.AddBlock(particlesType, cookedExtras
            ? CookedGeometryWithoutExtras("sparks", 2)
            : NifTestGeometry.Shape(name: "sparks", data: 2, controller: 3));
        builder.AddBlock(particlesDataType, particlesData ?? ParticlesData());
        builder.AddBlock(controllerType, controller ?? ParticleSystemController(target: 1, emitter: 0, particleModifier: 4));
        builder.AddBlock(colorModifierType, colorModifier ?? ParticleColorModifier(controller: 3, colorData: 6, next: 5));
        builder.AddBlock(growFadeType, growFade ?? ParticleGrowFade(controller: 3, growTime: 0.1f, fadeTime: 0.2f));
        builder.AddBlock(colorDataType, colorData ?? ColorData((0.0f, 1, 1, 1, 1), (1.0f, 1, 0.5f, 0.1f, 0.0f)));
        builder.AddBlock(gravityType, gravity ?? Gravity(controller: 3, force: 0.0f));
        builder.AddBlock(stencilType, stencil ?? StencilProperty());
        builder.AddBlock(stripsType, strips ?? NifTestGeometry.Shape(name: "mesh", data: 10));
        builder.AddBlock(stripsDataType, stripsData ?? TriStripsData(2, [[0, 1, 2, 3]]));
        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    [Fact]
    public void ParticleSystemParsesWithControllerAndModifierChain()
    {
        var file = Read(ParticleFileBytes());

        var particles = Assert.IsType<NifParticles>(file.Blocks[1]);
        Assert.Equal("sparks", particles.Name);
        Assert.Equal(2, particles.DataReference);
        Assert.Equal(3, particles.ControllerReference);

        var data = Assert.IsType<NifParticlesData>(file.Blocks[2]);
        Assert.Equal(3, data.NumVertices);
        Assert.Equal((ushort)3, data.ActiveCount);
        Assert.NotNull(data.Radii);
        Assert.NotNull(data.Sizes);
        Assert.NotNull(data.Rotations);
        Assert.Equal(1.0f, data.Sizes![0]);
        Assert.Equal(Quaternion.Identity, data.Rotations![0]);

        var controller = Assert.IsType<NifParticleSystemController>(file.Blocks[3]);
        Assert.Equal(1, controller.TargetReference);
        Assert.Equal(0, controller.EmitterReference);
        Assert.Equal(4, controller.ParticleModifierReference);
        Assert.Equal(-1, controller.EmitterModifierReference);
        Assert.Equal(-1, controller.ColliderReference);
        Assert.Equal(120.0f, controller.Speed);
        Assert.Equal(300.0f, controller.BirthRate);
        Assert.Equal(0.5f, controller.Lifetime);
        Assert.True(controller.UseBirthRate);
        Assert.False(controller.Reset);
        Assert.False(controller.StaticTargetBound);
        Assert.Equal((ushort)2, controller.ParticleSlotCount);
        Assert.Equal(80, controller.ParticleInfoPayload.Length);

        var colorModifier = Assert.IsType<NifParticleColorModifier>(file.Blocks[4]);
        Assert.Equal(3, colorModifier.ControllerReference);
        Assert.Equal(6, colorModifier.ColorDataReference);
        Assert.Equal(5, colorModifier.NextModifierReference);

        var growFade = Assert.IsType<NifParticleGrowFade>(file.Blocks[5]);
        Assert.Equal(3, growFade.ControllerReference);
        Assert.Equal(0.1f, growFade.GrowTime);
        Assert.Equal(0.2f, growFade.FadeTime);
        Assert.Equal(-1, growFade.NextModifierReference);

        var colorData = Assert.IsType<NifColorData>(file.Blocks[6]);
        Assert.NotNull(colorData.Keys);
        Assert.Equal(2, colorData.Keys!.Keys.Count);
        Assert.Equal(NifKeyType.Linear, colorData.Keys.Interpolation);
        Assert.Equal(1.0f, colorData.Keys.Keys[0].Value.R);
        Assert.Equal(0.1f, colorData.Keys.Keys[1].Value.B);
        Assert.Equal(0.0f, colorData.Keys.Keys[1].Value.A);

        Assert.Equal([0], file.Roots);
    }

    [Fact]
    public void GravityParsesDirectionalConstantForce()
    {
        var file = Read(ParticleFileBytes(gravity: Gravity(controller: 3, force: -9.8f)));

        var gravity = Assert.IsType<NifGravity>(file.Blocks[7]);
        Assert.Equal(-9.8f, gravity.Force);
        Assert.Equal(0u, gravity.ForceType);
        Assert.Equal((0, 0, -1), (gravity.Direction.X, gravity.Direction.Y, gravity.Direction.Z));
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    public void GravityRejectsNonDirectionalForceTypes(uint forceType)
    {
        var exception = Assert.Throws<NotSupportedException>(
            () => Read(ParticleFileBytes(gravity: Gravity(controller: 3, force: 1.0f, forceType: forceType))));

        Assert.Contains($"uses force type {forceType}", exception.Message);
        Assert.Contains("directional constant force (0) only", exception.Message);
    }

    [Fact]
    public void StencilPropertyParses()
    {
        var file = Read(ParticleFileBytes(stencil: StencilProperty(flags: 7, enabled: 0)));

        var stencil = Assert.IsType<NifStencilProperty>(file.Blocks[8]);
        Assert.Equal((ushort)7, stencil.Flags);
        Assert.Equal((byte)0, stencil.StencilEnabled);
    }

    [Fact]
    public void TriStripsParseWithStripInvariant()
    {
        var file = Read(ParticleFileBytes(stripsData: TriStripsData(3, [[0, 1, 2, 3, 4]], 5)));

        var strips = Assert.IsType<NifTriStrips>(file.Blocks[9]);
        Assert.Equal(10, strips.DataReference);
        var data = Assert.IsType<NifTriStripsData>(file.Blocks[10]);
        Assert.Equal(3, data.NumTriangles);
        Assert.Equal(5, Assert.Single(data.StripLengths));
        Assert.Equal<ushort>([0, 1, 2, 3, 4], data.Points);

        var valid = Read(ParticleFileBytes(
            stripsData: TriStripsData(4, [[0, 1, 2, 3], [3, 2, 0, 1]])));
        var multiStrip = Assert.IsType<NifTriStripsData>(valid.Blocks[10]);
        Assert.Equal(2, multiStrip.StripLengths.Count);
    }

    [Fact]
    public void TriStripsRejectsTriangleCountMismatch()
    {
        var exception = Assert.Throws<InvalidDataException>(
            () => Read(ParticleFileBytes(stripsData: TriStripsData(3, [[0, 1, 2, 3]]))));

        Assert.Contains("strips encode 2 triangles, but the block declares 3", exception.Message);
    }

    [Fact]
    public void CookedGeometryWithoutExtraDataListParses()
    {
        var file = Read(ParticleFileBytes(cookedExtras: true));

        var particles = Assert.IsType<NifParticles>(file.Blocks[1]);
        Assert.Empty(particles.ExtraDataReferences);
        Assert.Equal(-1, particles.ControllerReference);
        Assert.Equal(2, particles.DataReference);
    }

    [Theory]
    [InlineData("NiPSysData")]
    [InlineData("NiPSysBoxEmitter")]
    [InlineData("NiPSysColliderManager")]
    public void NiPSysBlocksAreRejectedByName(string typeName)
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var psysType = builder.AddBlockType(typeName);
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root"));
        builder.AddBlock(psysType, [0, 0, 0, 0]);
        builder.AddTrailing(NifTestBlocks.Footer());

        var exception = Assert.Throws<NotSupportedException>(() => Read(builder.Build()));

        Assert.Contains($"block 1 has unsupported type '{typeName}'", exception.Message);
        Assert.Contains("legacy NiParticles effects only, not NiPSys blocks", exception.Message);
    }

    [Fact]
    public void NiParticleBombIsRejectedByName()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var bombType = builder.AddBlockType("NiParticleBomb");
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root"));
        builder.AddBlock(bombType, [0, 0, 0, 0]);
        builder.AddTrailing(NifTestBlocks.Footer());

        var exception = Assert.Throws<NotSupportedException>(() => Read(builder.Build()));

        Assert.Contains("block 1 has unsupported type 'NiParticleBomb'", exception.Message);
        Assert.Contains("bomb-force particle modifier", exception.Message);
    }

    [Fact]
    public void NiFlipControllerIsRejectedByName()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var flipType = builder.AddBlockType("NiFlipController");
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root"));
        builder.AddBlock(flipType, [0, 0, 0, 0]);
        builder.AddTrailing(NifTestBlocks.Footer());

        var exception = Assert.Throws<NotSupportedException>(() => Read(builder.Build()));

        Assert.Contains("block 1 has unsupported type 'NiFlipController'", exception.Message);
        Assert.Contains("flipbook textures", exception.Message);
    }

    [Fact]
    public void ParticlesRejectsTriShapeDataReference()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var particlesType = builder.AddBlockType("NiParticles");
        var shapeDataType = builder.AddBlockType("NiTriShapeData");
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1]));
        builder.AddBlock(particlesType, NifTestGeometry.Shape(name: "sparks", data: 2));
        builder.AddBlock(shapeDataType, NifTestGeometry.GeometryData(numVertices: 3));
        builder.AddTrailing(NifTestBlocks.Footer());

        var exception = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));

        Assert.Contains("NIF geometry block 1 references block 2 as data", exception.Message);
        Assert.Contains("NifTriShapeData", exception.Message);
    }

    [Fact]
    public void ControllerRejectsWrongModifierChainType()
    {
        var exception = Assert.Throws<InvalidDataException>(
            () => Read(ParticleFileBytes(
                controller: ParticleSystemController(target: 1, emitter: 0, particleModifier: 0))));

        Assert.Contains("NIF block 3 references block 0 as ParticleModifier", exception.Message);
        Assert.Contains("NifNode", exception.Message);
    }

    [Fact]
    public void ColorModifierRejectsWrongColorDataReference()
    {
        var exception = Assert.Throws<InvalidDataException>(
            () => Read(ParticleFileBytes(
                colorModifier: ParticleColorModifier(controller: 3, colorData: 2))));

        Assert.Contains("NIF NiParticleColorModifier block 4 references block 2 as color data", exception.Message);
        Assert.Contains("NifParticlesData", exception.Message);
    }

    [Fact]
    public void ControllerRejectsOutOfRangeEmitterReference()
    {
        var exception = Assert.Throws<InvalidDataException>(
            () => Read(ParticleFileBytes(
                controller: ParticleSystemController(target: 1, emitter: 99))));

        Assert.Contains("NIF block 3 has Emitter reference 99", exception.Message);
        Assert.Contains("outside the valid range", exception.Message);
    }
}
