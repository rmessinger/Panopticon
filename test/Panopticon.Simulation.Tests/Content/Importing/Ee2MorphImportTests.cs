using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Nif;
using Panopticon.Content.Loaders;
using Panopticon.Graphics;
using Panopticon.Graphics.Animation;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Tests.Content;
using Panopticon.Utilities;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

[Trait("Category", "Interactive")]
public sealed class Ee2MorphImportTests
{
    private const string GraphicsZipPath = "zips/graphics.zip";
    private const float FeetPerMeter = 3.280839895f;

    private static string InstallationPath()
    {
        var path = Environment.GetEnvironmentVariable("EMPIRE_EARTH_II_PATH");
        if (string.IsNullOrEmpty(path))
        {
            throw new InvalidOperationException(
                "Empire Earth II installation not found for Interactive test.");
        }

        return path!;
    }

    private static (AssetImportContext Context, ZipFileSystem FileSystem, NifModelImporter Importer)
        OpenArchive(bool gate)
    {
        var fileSystem = new ZipFileSystem($"{InstallationPath()}/{GraphicsZipPath}");
        IEnumerable<string>? gates = gate ? [ImportedFeatureGates.MorphTargets] : null;
        var context = new AssetImportContext(fileSystem, language: null, gates);
        return (context, fileSystem, new NifModelImporter());
    }

    [Fact]
    public void FarmYamMorphKfImportsOneTargetSet()
    {
        var (context, fileSystem, importer) = OpenArchive(gate: true);
        using var _ = fileSystem;

        var model = importer.ImportModel(
            "bld_farm_yam#morph-kf=Graphics/Building/bld_farm_yam.kf", context);
        Assert.NotNull(model);

        var mesh = Assert.IsType<ImportedMesh>(model!.SubObjects[0].RenderObject);
        Assert.Equal(720, mesh.Vertices.Count);

        var set = Assert.Single(mesh.MorphTargetSets);
        Assert.Equal("Target1", Assert.Single(set.TargetNames));
        Assert.Equal(model.SubObjects[0].FullName, set.PrimitiveId);
        Assert.Equal(mesh.PrimitiveId, set.PrimitiveId);

        var deltas = Assert.Single(set.Targets).PositionDeltas!;
        Assert.Equal(720, deltas.Count);

        // The deep-dive census counts 600 meaningful deltas; six more carry
        // cooker float noise around 7e-13 and normalize away.
        Assert.Equal(600, deltas.Count(delta => delta.Length() > 1e-6f));

        // The largest delta is 5.4214 source units; the bake is a pure
        // rotation plus meters-to-feet, so lengths scale by FeetPerMeter.
        var maxLength = deltas.Max(delta => delta.Length());
        Assert.InRange(maxLength, 5.4214f * FeetPerMeter - 0.05f, 5.4214f * FeetPerMeter + 0.05f);
    }

    [Fact]
    public void FarmYamAnimationDrivesTarget1WeightsAtRuntime()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        var (context, fileSystem, importer) = OpenArchive(gate: true);
        using var _ = fileSystem;
        using var graphics = new TestGraphicsContext(fileSystem);

        var model = importer.ImportModel(
            "bld_farm_yam#morph-kf=Graphics/Building/bld_farm_yam.kf", context);
        Assert.NotNull(model);
        var animation = new NifAnimationImporter().ImportAnimation(
            "bld_farm_yam#kf=Graphics/Building/bld_farm_yam.kf", context);
        Assert.NotNull(animation);

        var instance = new ModelInstance(
            ImportedModelMaterializer.MaterializeModel(model!, graphics.Context),
            graphics.Context);
        instance.Update(new TimeInterval(TimeSpan.Zero, TimeSpan.Zero));

        var runtimeAnimation = ImportedAnimationMaterializer.Materialize(animation);
        var duration = (float)animation!.Duration.TotalSeconds;

        var start = PlayTo(instance, runtimeAnimation, 0f);
        Assert.Equal(0f, start[0], 5);

        var middle = PlayTo(instance, runtimeAnimation, duration / 2f);
        Assert.Equal(0.5f, middle[0], 5);

        var end = PlayTo(instance, runtimeAnimation, duration);
        Assert.Equal(1f, end[0], 5);
    }

    private static float[] PlayTo(ModelInstance instance, ModelAnimation animation, float seconds)
    {
        var animationInstance = instance.CreateAnimationInstance(
            animation,
            AnimationMode.Once,
            AnimationFlags.StartFrameFirst,
            gameObject: null!,
            random: new ConstantRandom());
        animationInstance.Play();

        var delta = seconds / 8f;
        for (var i = 0; i < 8; i++)
        {
            animationInstance.Update(new TimeInterval(TimeSpan.Zero, TimeSpan.FromSeconds(delta)));
        }

        instance.Update(new TimeInterval(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(delta)));
        return instance.MeshInstances[0].MorphWeights!;
    }

    private sealed class ConstantRandom : IRandom
    {
        public uint Seed => 0;

        public void Initialize(uint seed)
        {
        }

        public int Next(int minValue, int maxValue) => minValue;

        public float NextSingle(float minValue, float maxValue) => minValue;
    }

    [Fact]
    public void FarmYamMorphModelWithoutGateFailsNamingGate()
    {
        var (context, fileSystem, importer) = OpenArchive(gate: false);
        using var _ = fileSystem;

        var ex = Assert.Throws<NotSupportedException>(
            () => importer.ImportModel(
                "bld_farm_yam#morph-kf=Graphics/Building/bld_farm_yam.kf", context));
        Assert.Contains("MorphTargets", ex.Message);
    }

    [Fact]
    public void NoMorphModelsStillImportWithoutTheGate()
    {
        var (context, fileSystem, importer) = OpenArchive(gate: false);
        using var _ = fileSystem;

        var model = importer.ImportModel("nwg1_galley", context);
        Assert.NotNull(model);
        foreach (var subObject in model!.SubObjects)
        {
            var mesh = Assert.IsType<ImportedMesh>(subObject.RenderObject);
            Assert.Empty(mesh.MorphTargetSets);
        }
    }

    [Fact]
    public void EmpFlashEmbeddedMorphImportsTwoTargets()
    {
        var (context, fileSystem, importer) = OpenArchive(gate: true);
        using var _ = fileSystem;

        var model = importer.ImportModel("fx_emp_flash", context);
        Assert.NotNull(model);

        var mesh = Assert.IsType<ImportedMesh>(model!.SubObjects[0].RenderObject);
        Assert.Equal(4, mesh.Vertices.Count);

        var set = Assert.Single(mesh.MorphTargetSets);
        Assert.Equal(mesh.PrimitiveId, set.PrimitiveId);
        Assert.Equal(["Target1", "Target2"], set.TargetNames);
        Assert.All(set.Targets, target => Assert.Equal(4, target.PositionDeltas!.Count));
    }

    [Fact]
    public void MuzzleCannonStillFailsAtTheNiUVControllerParseGuard()
    {
        // fx_muzzle_cannon carries an embedded NiUVController; NIF UV
        // animation is C4e-deferred, so the model fails at parse before its
        // morph blocks are reached. The morph census caveat resolves here.
        var (context, fileSystem, importer) = OpenArchive(gate: true);
        using var _ = fileSystem;

        var ex = Assert.Throws<NotSupportedException>(() => importer.ImportModel("fx_muzzle_cannon", context));
        Assert.Contains("NiUVController", ex.Message);
    }
}
