using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Panopticon.Content;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Nif;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Content.Importing.W3d;
using Panopticon.Data;
using Panopticon.Graphics;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Tests.Data;
using Panopticon.Tools.TestHarness;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.Units;

/// <summary>
/// Card WI-M3-01 (no install required): the authored tiles-to-world factor and model-unit scale
/// stay consistent, the reference-roster profiles record them, and materialization bakes the
/// model scale into the imported skeleton's root binds once, at install. Install-dependent
/// bounds checks live in <see cref="Ee2ModelScaleInstalledTests"/>.
/// </summary>
public sealed class Ee2ModelScaleTests
{
    internal static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    internal static Ee2ModelScaleManifest LoadManifest()
    {
        var path = Path.Combine(Environment.CurrentDirectory, "Data", "Content", "Importing", "Units", "Ee2", "ee2-model-scale-bounds.json");
        return JsonSerializer.Deserialize<Ee2ModelScaleManifest>(File.ReadAllText(path), ManifestOptions)
            ?? throw new InvalidOperationException($"Model scale manifest at '{path}' is empty or invalid.");
    }

    [Fact]
    public void AuthoredConstantsSatisfyTheTileInvariant()
    {
        // One tile is 50 decimeter model units, so the distance factor must be exactly the
        // model-unit factor times 50; the two profiles carry the same pair.
        Assert.Equal(
            Ee2ReferenceScale.GameUnitsPerSourceDistance,
            Ee2ReferenceScale.GameUnitsPerSourceModelUnit * Ee2ReferenceScale.SourceModelUnitsPerSourceDistance,
            precision: 10);

        var manifest = LoadManifest();
        Assert.Equal(Ee2ReferenceScale.GameUnitsPerSourceModelUnit, manifest.GameUnitsPerSourceModelUnit);

        var validationProfile = Ee2ValidationProfile.Load(FindShippedProfile("ee2-reference-validation.json"));
        Assert.Equal(Ee2ReferenceScale.GameUnitsPerSourceDistance, validationProfile.ContentImport.GameUnitsPerSourceDistance);
        Assert.Equal(Ee2ReferenceScale.GameUnitsPerSourceModelUnit, validationProfile.ContentImport.GameUnitsPerSourceModelUnit);
        Assert.Equivalent(Ee2ReferenceRps.DamageTypes, validationProfile.ContentImport.RpsDamageTypes);
    }

    [Fact]
    public void RootBindScaleBakesTheModelScaleIntoEveryBoneChain()
    {
        // The bake appends a trailing uniform scale to each bone's absolute chain (row-vector:
        // local points scale about the model origin before placement). That is exactly what the
        // former per-frame CreateScale * worldMatrix premultiply produced, so bones, skinned
        // meshes through unchanged inverse binds, bounds, and attachments all follow.
        var root = new ImportedBone("Root", -1, new Vector3(4f, -2f, 1f), Quaternion.CreateFromYawPitchRoll(0.3f, 0.1f, -0.2f), bindScale: 1.5f);
        var child = new ImportedBone("Turret", 0, new Vector3(10f, 0f, 3f), Quaternion.CreateFromYawPitchRoll(-0.5f, 0f, 0.4f));
        var skeleton = new ImportedSkeleton("WI-M3-01", [root, child]);

        var unscaled = new ModelBoneHierarchy(skeleton);
        var scaled = new ModelBoneHierarchy(skeleton, rootScale: 2f);

        Assert.Equal(2f * root.BindTranslation, scaled.Bones[0].Transform.Translation);
        Assert.Equal(2f * root.BindScale, scaled.Bones[0].Transform.Scale);
        Assert.Equal(root.InverseBindMatrix, scaled.Bones[0].InverseBindMatrix);
        Assert.Equal(child.BindTranslation, scaled.Bones[1].Transform.Translation);
        Assert.Equal(child.BindScale, scaled.Bones[1].Transform.Scale);
        Assert.Equal(child.InverseBindMatrix, scaled.Bones[1].InverseBindMatrix);

        var bake = Matrix4x4.CreateScale(2f);
        for (var i = 0; i < scaled.Bones.Length; i++)
        {
            var expected = CumulativeBind(unscaled, i) * bake;
            var actual = CumulativeBind(scaled, i);
            Assert.True(NearlyEqual(expected, actual), $"bone {i}: expected {expected}, actual {actual}");
        }

        return;

        static Matrix4x4 CumulativeBind(ModelBoneHierarchy hierarchy, int index)
        {
            var bone = hierarchy.Bones[index];
            return bone.Transform.Matrix * (bone.Parent is null ? Matrix4x4.Identity : CumulativeBind(hierarchy, bone.Parent.Index));
        }

        static bool NearlyEqual(in Matrix4x4 expected, in Matrix4x4 actual)
        {
            return Vector4.DistanceSquared(new(expected.M11, expected.M12, expected.M13, expected.M14), new(actual.M11, actual.M12, actual.M13, actual.M14)) < 1e-8f
                && Vector4.DistanceSquared(new(expected.M21, expected.M22, expected.M23, expected.M24), new(actual.M21, actual.M22, actual.M23, actual.M24)) < 1e-8f
                && Vector4.DistanceSquared(new(expected.M31, expected.M32, expected.M33, expected.M34), new(actual.M31, actual.M32, actual.M33, actual.M34)) < 1e-8f
                && Vector4.DistanceSquared(new(expected.M41, expected.M42, expected.M43, expected.M44), new(actual.M41, actual.M42, actual.M43, actual.M44)) < 1e-8f;
        }
    }

    internal static string FindShippedProfile(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        Assert.True(File.Exists(path), $"Shipped profile '{fileName}' was not found beside the test output.");
        return path;
    }
}

/// <summary>
/// Card WI-M3-01 against the installs: imported EE2 model heights and lengths land within the
/// committed bounds against Generals reference models, and the import bakes the authored model
/// scale into installed models' root binds once. Requires CNC_GENERALS_PATH and
/// EMPIRE_EARTH_II_PATH; no EE2 asset is committed.
/// </summary>
[Trait("Category", "Interactive")]
[Collection("SerialGameTests")]
public sealed class Ee2ModelScaleInstalledTests(ITestOutputHelper output)
{
    [Fact]
    public void ReferenceModelExtentsMatchGeneralsReferencesWithinBounds()
    {
        var root = Environment.GetEnvironmentVariable(UnitContentImportRunner.Ee2PathVariable);
        Assert.False(
            string.IsNullOrEmpty(root),
            $"Unmet prerequisite: {UnitContentImportRunner.Ee2PathVariable} must point at an Empire Earth II install.");

        var manifest = Ee2ModelScaleTests.LoadManifest();

        using var graphics = new ZipFileSystem(Path.Combine(root, "zips/graphics.zip"));
        using var textures = new ZipFileSystem(Path.Combine(root, "zips/textures.zip"));
        var sourceContext = new AssetImportContext(new CompositeFileSystem(graphics, textures), language: null);
        var ee2Importer = new NifModelImporter();

        using var game = new Game(InstalledFilesTestData.GetInstallation(SageGame.CncGenerals));
        using var fileSystem = game.ContentManager.FileSystem;
        var generalsImporter = new W3dModelImporter(PathResolvers.W3d);
        var importContext = new AssetImportContext(fileSystem, "english");

        foreach (var entry in manifest.Entries)
        {
            var ee2Model = ee2Importer.ImportModel(entry.ModelKey, sourceContext);
            Assert.NotNull(ee2Model);
            var (height, length) = Measure(ee2Model!);
            var ee2 = (Height: height * manifest.GameUnitsPerSourceModelUnit, Length: length * manifest.GameUnitsPerSourceModelUnit);

            var reference = game.AssetStore.ObjectDefinitions.GetByName(entry.GeneralsReference);
            Assert.NotNull(reference);
            var referenceModelName = reference!.Draws.Values.Select(c => c.Data).OfType<W3dModelDrawModuleData>()
                .SelectMany(m => m.ConditionStates)
                .First(s => !string.IsNullOrEmpty(s.Model?.Name))
                .Model!.Name;
            var generals = Measure(generalsImporter.ImportModel(referenceModelName, importContext)!);

            var heightRatio = ee2.Height / generals.Height;
            var lengthRatio = ee2.Length / generals.Length;
            output.WriteLine($"{entry.RowKey}: height {ee2.Height:0.00}/{generals.Height:0.00} = {heightRatio:0.000}, length {ee2.Length:0.00}/{generals.Length:0.00} = {lengthRatio:0.000}");

            Assert.InRange(heightRatio, entry.HeightRatioBounds[0], entry.HeightRatioBounds[1]);
            Assert.InRange(lengthRatio, entry.LengthRatioBounds[0], entry.LengthRatioBounds[1]);
        }
    }

    [Fact]
    public void InstalledModelsBakeTheAuthoredModelScaleIntoRootBinds()
    {
        var root = Environment.GetEnvironmentVariable(UnitContentImportRunner.Ee2PathVariable);
        Assert.False(
            string.IsNullOrEmpty(root),
            $"Unmet prerequisite: {UnitContentImportRunner.Ee2PathVariable} must point at an Empire Earth II install.");

        var entry = Ee2ModelScaleTests.LoadManifest().Entries[0];

        using var graphics = new ZipFileSystem(Path.Combine(root, "zips/graphics.zip"));
        using var textures = new ZipFileSystem(Path.Combine(root, "zips/textures.zip"));
        var sourceContext = new AssetImportContext(new CompositeFileSystem(graphics, textures), language: null);
        var importer = new NifModelImporter();
        var interchange = importer.ImportModel(entry.ModelKey, sourceContext)
            ?? throw new InvalidOperationException($"Model '{entry.ModelKey}' was not found in the EE2 install.");

        using var game = new Game(InstalledFilesTestData.GetInstallation(SageGame.CncGenerals));
        var runtimeName = UnitVisualMaterializer.RuntimeName(ContentSource.Ee2, entry.ModelKey);
        var installer = new ImportedVisualInstaller(
            importer,
            new NifAnimationImporter(),
            sourceContext,
            game.AssetStore.LoadContext,
            (float)Ee2ReferenceScale.GameUnitsPerSourceModelUnit);
        installer.Install(
            [new VisualAssetRequest(new ContentId(ContentSource.Ee2, ContentKind.Unit, entry.RowKey), VisualAssetKind.Model, entry.ModelKey, runtimeName)],
            game.AssetStore);

        var installed = game.AssetStore.Models.GetByName(runtimeName);
        var scale = (float)Ee2ReferenceScale.GameUnitsPerSourceModelUnit;

        // Applied once, on import: root binds carry game units; child binds, vertices, and
        // inverse binds stay authored because the trailing root scale flows down every chain.
        var installedRoot = installed.BoneHierarchy.Bones[0];
        Assert.Equal(interchange.Skeleton.Bones[0].BindTranslation * scale, installedRoot.Transform.Translation);
        Assert.Equal(interchange.Skeleton.Bones[0].BindScale * scale, installedRoot.Transform.Scale);

        var childIndex = Array.FindIndex(interchange.Skeleton.Bones.ToArray(), bone => bone.ParentIndex != -1);
        if (childIndex > 0)
        {
            Assert.Equal(interchange.Skeleton.Bones[childIndex].BindTranslation, installed.BoneHierarchy.Bones[childIndex].Transform.Translation);
            Assert.Equal(interchange.Skeleton.Bones[childIndex].BindScale, installed.BoneHierarchy.Bones[childIndex].Transform.Scale);
        }
    }

    private static (double Height, double Length) Measure(ImportedModel model)
    {
        var bones = model.Skeleton.Bones;
        var cumulative = new Matrix4x4[bones.Count];
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            var local = Matrix4x4.Identity;
            local *= Matrix4x4.CreateScale(bone.BindScale);
            local *= Matrix4x4.CreateFromQuaternion(bone.BindRotation);
            local *= Matrix4x4.CreateTranslation(bone.BindTranslation);
            cumulative[i] = bone.ParentIndex < 0 ? local : local * cumulative[bone.ParentIndex];
        }

        AxisAlignedBoundingBox? merged = null;
        foreach (var subObject in model.SubObjects)
        {
            if (subObject.RenderObject is not ImportedMesh mesh)
            {
                continue;
            }

            // FX sub-objects are presentation-only flashes (the G10 measurement convention).
            if (subObject.FullName.Contains("FX", StringComparison.OrdinalIgnoreCase)
                || subObject.DisplayName.Contains("FX", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var world = subObject.ParentBoneIndex >= 0 ? cumulative[subObject.ParentBoneIndex] : Matrix4x4.Identity;
            var box = AxisAlignedBoundingBox.Transform(mesh.BoundingBox, world);
            merged = merged == null ? box : AxisAlignedBoundingBox.CreateMerged(merged.Value, box);
        }

        Assert.True(merged.HasValue, $"model '{model.Name}' must have at least one non-FX mesh");
        var bounds = merged!.Value;
        return (bounds.Max.Z - bounds.Min.Z, bounds.Max.X - bounds.Min.X);
    }
}

/// <summary>The committed WI-M3-01 bounds manifest (see ee2-model-scale-bounds.json).</summary>
internal sealed record Ee2ModelScaleManifest(double GameUnitsPerSourceModelUnit, List<Ee2ModelScaleEntry> Entries);

internal sealed record Ee2ModelScaleEntry(
    string RowKey,
    string ModelKey,
    string GeneralsReference,
    double MeasuredHeightRatio,
    double[] HeightRatioBounds,
    double MeasuredLengthRatio,
    double[] LengthRatioBounds);
