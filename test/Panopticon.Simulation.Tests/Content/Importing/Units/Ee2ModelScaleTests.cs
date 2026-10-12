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
using Panopticon.Content.Importing.W3d;
using Panopticon.Data;
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
/// stay consistent, the reference-roster profiles record them, and the runtime folds the model
/// scale into the imported draw's world matrix. Install-dependent bounds checks live in
/// <see cref="Ee2ModelScaleInstalledTests"/>.
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
    public void WorldMatrixFoldsTheModelScaleWithoutTouchingPlacement()
    {
        // The scale multiplies before placement (local frame): the object's position is
        // unchanged, a local offset scales about it, and a unit scale keeps the matrix identical.
        var placement = Matrix4x4.CreateTranslation(100f, -20f, 5f) * Matrix4x4.CreateRotationZ(MathF.PI / 2f);
        var scaled = W3dModelDraw.ApplyImportedModelScale(placement, 2f);

        Assert.Equal(placement, W3dModelDraw.ApplyImportedModelScale(placement, 1f));
        Assert.Equal(Vector3.Transform(Vector3.Zero, placement), Vector3.Transform(Vector3.Zero, scaled));

        var local = new Vector3(10f, 0f, 0f);
        Assert.Equal(Vector3.Transform(local * 2f, placement), Vector3.Transform(local, scaled));
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
/// committed bounds against Generals reference models, and materialized reference draws carry
/// the authored model scale. Requires CNC_GENERALS_PATH and EMPIRE_EARTH_II_PATH; no EE2 asset
/// is committed.
/// </summary>
[Trait("Category", "Interactive")]
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
    public void MaterializedDrawModulesCarryTheAuthoredModelScale()
    {
        var root = Environment.GetEnvironmentVariable(UnitContentImportRunner.Ee2PathVariable);
        Assert.False(
            string.IsNullOrEmpty(root),
            $"Unmet prerequisite: {UnitContentImportRunner.Ee2PathVariable} must point at an Empire Earth II install.");

        var content = Ee2RosterPublicationTests.MaterializeReferenceRoster(
            Ee2ReferenceScale.GameUnitsPerSourceDistance,
            Ee2ReferenceScale.GameUnitsPerSourceModelUnit);

        foreach (var rowKey in Ee2RosterPublicationTests.AllRowKeys)
        {
            var definition = content.Get<ObjectDefinition>(new ContentId(ContentSource.Ee2, ContentKind.Unit, rowKey));
            var draw = definition.Draws.Values.Select(c => c.Data).OfType<W3dModelDrawModuleData>().Single();
            Assert.Equal((float)Ee2ReferenceScale.GameUnitsPerSourceModelUnit, draw.ImportedModelScale);
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
