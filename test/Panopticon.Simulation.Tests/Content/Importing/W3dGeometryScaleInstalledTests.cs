using System;
using System.Linq;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.W3d;
using Panopticon.Data;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Tests.Data;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing;

/// <summary>
/// AQ-5 compatibility fixture against the Generals install. The converted W3D chassis half-extent
/// of each ground vehicle stays within a factor of 1.5 of the INI GeometryMajorRadius. This proves
/// the 0.3048 compatibility scale, not a physical fact about the W3D file unit.
/// Limitations: the measure uses only the CHASSIS mesh, because turrets, barrels, decals, and tires
/// protrude past the hull and inflate full-mesh bounds to 1.4 to 2.3 times the radius. Skinned
/// infantry bounds are not measured, since the skinned-mesh measurement is not validated here.
/// Observed chassis ratios: Humvee 0.868, Crusader 0.965, BattleMaster 1.016.
/// </summary>
public sealed class W3dGeometryScaleInstalledTests(ITestOutputHelper output)
{
    private const double MaxRatio = 1.5;
    private const double MinRatio = 1.0 / MaxRatio;

    private static readonly string[] GroundVehicles =
    [
        "AmericaVehicleHumvee",
        "AmericaTankCrusader",
        "ChinaTankBattleMaster",
    ];

    [GameFact(SageGame.CncGenerals)]
    public void ChassisHullRadiiMatchGeometryMajorRadiusWithinFactor()
    {
        using var game = new Game(InstalledFilesTestData.GetInstallation(SageGame.CncGenerals));
        using var fileSystem = game.ContentManager.FileSystem;
        var importer = new W3dModelImporter(PathResolvers.W3d);
        var context = new AssetImportContext(fileSystem, "english");

        foreach (var name in GroundVehicles)
        {
            var definition = game.AssetStore.ObjectDefinitions.GetByName(name);
            Assert.NotNull(definition);

            var modelName = definition!.Draws.Values.Select(c => c.Data).OfType<W3dModelDrawModuleData>()
                .SelectMany(m => m.ConditionStates)
                .First(s => !string.IsNullOrEmpty(s.Model?.Name))
                .Model!.Name;
            var model = importer.ImportModel(modelName, context);
            Assert.NotNull(model);

            var (halfX, halfY) = ChassisHalfExtents(model!);
            var majorRadius = definition.Geometry.MajorRadius;
            var ratio = Math.Max(halfX, halfY) / majorRadius;
            output.WriteLine($"{name}: chassis halfX {halfX:0.000} halfY {halfY:0.000} majorRadius {majorRadius:0.000} ratio {ratio:0.000}");

            Assert.InRange(ratio, MinRatio, MaxRatio);
        }
    }

    private static (float HalfX, float HalfY) ChassisHalfExtents(ImportedModel model)
    {
        var bones = model.Skeleton.Bones;
        var cumulative = new Matrix4x4[bones.Count];
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            var local = Matrix4x4.CreateScale(bone.BindScale)
                * Matrix4x4.CreateFromQuaternion(bone.BindRotation)
                * Matrix4x4.CreateTranslation(bone.BindTranslation);
            cumulative[i] = bone.ParentIndex < 0 ? local : local * cumulative[bone.ParentIndex];
        }

        var chassis = model.SubObjects.SingleOrDefault(s => s.RenderObject is ImportedMesh && s.DisplayName.Equals("CHASSIS", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(chassis);

        var mesh = (ImportedMesh)chassis!.RenderObject;
        var world = chassis.ParentBoneIndex >= 0 ? cumulative[chassis.ParentBoneIndex] : Matrix4x4.Identity;
        var bounds = AxisAlignedBoundingBox.Transform(mesh.BoundingBox, world);
        return ((bounds.Max.X - bounds.Min.X) / 2f, (bounds.Max.Y - bounds.Min.Y) / 2f);
    }
}
