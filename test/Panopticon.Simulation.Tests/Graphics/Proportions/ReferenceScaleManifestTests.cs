using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Panopticon.Content;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.W3d;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Tests.Data;
using Xunit;

namespace Panopticon.Tests.Graphics.Proportions;

/// <summary>
/// WI-G10-01 audit guard: re-derives the reference manifest measurements from the installed
/// game assets. Any drift (asset change, importer change, exclusion change) fails explicitly
/// with the measured values; the manifest is never regenerated automatically.
/// </summary>
public class ReferenceScaleManifestTests
{
    private const decimal ExtentTolerance = 0.01m;

    private static readonly ReferenceScaleManifest Manifest = ReferenceScaleManifest.Load();

    [GameFact(SageGame.CncGenerals)]
    public void TestInstallAssetsMatchReferenceManifest()
    {
        using var game = new Game(InstalledFilesTestData.GetInstallation(SageGame.CncGenerals));
        using var fileSystem = game.ContentManager.FileSystem;
        var assetStore = game.AssetStore;
        var importer = new W3dModelImporter(PathResolvers.W3d);
        var importContext = new AssetImportContext(fileSystem, "english");

        foreach (var expected in Manifest.Objects)
        {
            var definition = assetStore.ObjectDefinitions.GetByName(expected.ObjectName);
            Assert.NotNull(definition);
            Assert.Equal(expected.IniScale, (decimal)definition!.Scale);

            var shape = definition.Geometry.Shapes.Single();
            Assert.Equal(expected.Geometry.Shape, shape.Type.ToString(), ignoreCase: true);
            Assert.Equal(expected.Geometry.MajorRadius, (decimal)shape.MajorRadius);
            Assert.Equal(expected.Geometry.MinorRadius, (decimal)shape.MinorRadius);
            Assert.Equal(expected.Geometry.Height, (decimal)shape.Height);

            var model = definition.Draws.Values.Select(c => c.Data).OfType<W3dModelDrawModuleData>()
                .SelectMany(m => m.ConditionStates)
                .First(s => !string.IsNullOrEmpty(s.Model?.Name))
                .Model!.Name;
            Assert.Equal(expected.ModelName, model, ignoreCase: true);

            var entry = fileSystem.GetFile($"art/w3d/{model}.w3d");
            string hash;
            using (var stream = entry.Open())
            {
                hash = Convert.ToHexString(SHA256.HashData(stream));
            }

            Assert.Equal(expected.W3dSha256, hash);

            var imported = importer.ImportModel(model, importContext);
            Assert.NotNull(imported);

            var body = MeasureMergedBox(imported!, expected.Exclusions);
            AssertExtents(expected.ObjectName, "body", expected.Extents, body);

            if (expected.HullLengthUnits is not null && expected.HullExclusions is not null)
            {
                var hull = MeasureMergedBox(imported!, expected.HullExclusions);
                var hullLength = (decimal)(hull.Max.X - hull.Min.X);
                Assert.InRange(hullLength, expected.HullLengthUnits.Value - ExtentTolerance, expected.HullLengthUnits.Value + ExtentTolerance);
                Assert.Equal(expected.HullLengthUnits, Manifest.Baseline.HullLengthUnits);
            }
        }
    }

    private static AxisAlignedBoundingBox MeasureMergedBox(ImportedModel imported, IReadOnlyList<string> exclusions)
    {
        var bones = imported.Skeleton.Bones;
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
        foreach (var subObject in imported.SubObjects)
        {
            if (subObject.RenderObject is not ImportedMesh mesh)
            {
                continue;
            }

            var excluded = exclusions.Any(e =>
                subObject.FullName.Contains(e, StringComparison.OrdinalIgnoreCase)
                || subObject.DisplayName.Contains(e, StringComparison.OrdinalIgnoreCase));
            if (excluded)
            {
                continue;
            }

            var world = subObject.ParentBoneIndex >= 0 ? cumulative[subObject.ParentBoneIndex] : Matrix4x4.Identity;
            var box = AxisAlignedBoundingBox.Transform(mesh.BoundingBox, world);
            merged = merged == null ? box : AxisAlignedBoundingBox.CreateMerged(merged.Value, box);
        }

        Assert.True(merged.HasValue, "at least one mesh must survive the exclusion list");
        return merged!.Value;
    }

    private static void AssertExtents(string objectName, string label, ReferenceScaleExtents expected, AxisAlignedBoundingBox measured)
    {
        var x = (decimal)(measured.Max.X - measured.Min.X);
        var y = (decimal)(measured.Max.Y - measured.Min.Y);
        var z = (decimal)(measured.Max.Z - measured.Min.Z);

        Assert.InRange(x, expected.X - ExtentTolerance, expected.X + ExtentTolerance);
        Assert.InRange(y, expected.Y - ExtentTolerance, expected.Y + ExtentTolerance);
        Assert.InRange(z, expected.Z - ExtentTolerance, expected.Z + ExtentTolerance);
    }
}
