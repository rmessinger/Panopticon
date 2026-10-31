using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Granny;
using Panopticon.FileFormats.Granny;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Aoe3;

public class GrannyModelImportTests
{
    private const string MusketeerPath = @"Art\units\infantry_ranged\musketeer\musketeer_4age.gr2";

    private const string HorsePath = @"Art\units\cavalry\light_cavalry\light_cavalry_age4_horse.gr2";

    private const string FalconetPath = @"Art\units\artillery\falconet\falconet.gr2";

    private static AssetImportContext CreateContext(Panopticon.IO.FileSystem fileSystem)
    {
        return new AssetImportContext(fileSystem, language: null);
    }

    [Fact]
    public void MissingKeyReturnsNull()
    {
        var importer = new GrannyModelImporter();
        using var fileSystem = new Panopticon.Tests.Content.MemoryFileSystem();

        Assert.Null(importer.ImportModel(@"Art\nothere.gr2", CreateContext(fileSystem)));
        Assert.Null(importer.ImportSkeleton(@"Art\nothere.gr2", CreateContext(fileSystem)));
    }

    [Fact]
    public void EmptyKeyReturnsNull()
    {
        var importer = new GrannyModelImporter();
        using var fileSystem = new Panopticon.Tests.Content.MemoryFileSystem();

        Assert.Null(importer.ImportModel(string.Empty, CreateContext(fileSystem)));
    }

    [Fact]
    public void UnsupportedCompressionFailsWithKeySectionAndType()
    {
        var importer = new GrannyModelImporter();
        using var fileSystem = new Panopticon.Tests.Content.MemoryFileSystem();
        fileSystem.AddFile("unit.gr2", SyntheticGrannyBuilder.BuildWithCompressedSection(99));

        var exception = Assert.Throws<InvalidDataException>(
            () => importer.ImportModel("unit.gr2", CreateContext(fileSystem)));

        Assert.Contains("unit.gr2", exception.Message);
        Assert.Contains("section 0", exception.Message);
        Assert.Contains("compression 99", exception.Message);
        Assert.IsType<GrannyCompressedSectionException>(exception.InnerException);
    }

    [Fact]
    public void ImporterIdIsGr2()
    {
        Assert.Equal("GR2", new GrannyModelImporter().Id);
    }

    [Fact]
    public void TestHelperBuildsValidGrannyFile()
    {
        // The synthetic builder is the install-free fixture source; a file that never parses
        // would make every importer error test meaningless.
        using var stream = new MemoryStream(SyntheticGrannyBuilder.BuildValid());
        var file = GrannyFile.Read(stream, "synthetic.gr2");

        Assert.NotNull(file.Root);
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void MusketeerImportsWithCountsAndUprightBounds()
    {
        var importer = new GrannyModelImporter();
        using var fileSystem = Aoe3Install.CreateFileSystem();

        var model = importer.ImportModel(MusketeerPath, CreateContext(fileSystem));

        Assert.NotNull(model);
        Assert.Equal(23, model!.Skeleton.Bones.Count);
        Assert.Equal("Bip01", model.Skeleton.Name);
        Assert.Single(model.SubObjects);

        var mesh = Assert.IsType<ImportedMesh>(model.SubObjects[0].RenderObject);
        Assert.Equal(1197, mesh.Vertices.Count);
        Assert.Equal(4032, mesh.Indices.Count);
        Assert.Single(mesh.MeshParts);
        Assert.Equal(1344 * 3, (int)mesh.MeshParts[0].IndexCount);
        // 18 bone bindings: the mesh imports skinned (WI-C10c-01), attached to the root bone.
        Assert.True(mesh.IsSkinned);
        Assert.Equal(0, model.SubObjects[0].ParentBoneIndex);

        var material = mesh.Materials[0];
        Assert.Equal("mata", material.Properties.Single(p => p.Name == "GrannyMaterialName").TextureName);

        AssertBindPoseIsConsistent(model);
        var (min, max) = ModelSpaceBounds(model);
        // 1.8 m standing soldier corrected from the misreported 1.5625 units per meter.
        AssertUprightPlausibleMeters(min, max, minHeight: 1.7f, maxHeight: 1.95f);
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void HussarHorseImportsWithTwoGroups()
    {
        var importer = new GrannyModelImporter();
        using var fileSystem = Aoe3Install.CreateFileSystem();

        var model = importer.ImportModel(HorsePath, CreateContext(fileSystem));

        Assert.NotNull(model);
        Assert.Equal(32, model!.Skeleton.Bones.Count);

        var mesh = Assert.IsType<ImportedMesh>(model.SubObjects[0].RenderObject);
        Assert.Equal(3978, mesh.Vertices.Count);
        Assert.Equal(2, mesh.MeshParts.Count);
        Assert.Equal(1332 * 3, (int)mesh.MeshParts[0].IndexCount);
        Assert.Equal(72 * 3, (int)mesh.MeshParts[1].IndexCount);
        Assert.Equal(2, mesh.Materials.Count);
        Assert.True(mesh.IsSkinned);

        AssertBindPoseIsConsistent(model);
        var (min, max) = ModelSpaceBounds(model);
        // Hoofs-to-ears ~2.28 m at the corrected 1.15 units per meter.
        AssertUprightPlausibleMeters(min, max, minHeight: 2.0f, maxHeight: 2.5f);
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void FalconetImportsWithCompressedSections()
    {
        var importer = new GrannyModelImporter();
        using var fileSystem = Aoe3Install.CreateFileSystem();

        var model = importer.ImportModel(FalconetPath, CreateContext(fileSystem));

        Assert.NotNull(model);
        Assert.NotEmpty(model.Skeleton.Bones);
        Assert.NotEmpty(model.SubObjects);
        Assert.All(model.SubObjects, subObject => Assert.IsType<ImportedMesh>(subObject.RenderObject));
        AssertBindPoseIsConsistent(model);
    }

    [Theory]
    [InlineData(@"Art\units\infantry_ranged\musketeer\redcoats_age4.gr2")]
    [InlineData(FalconetPath)]
    [Trait("Category", "Interactive")]
    public void NarrowTopologyIndicesPreserveTriangleVertices(string path)
    {
        using var fileSystem = Aoe3Install.CreateFileSystem();
        var model = new GrannyModelImporter().ImportModel(path, CreateContext(fileSystem));
        Assert.NotNull(model);
        foreach (var subObject in model.SubObjects)
        {
            var mesh = Assert.IsType<ImportedMesh>(subObject.RenderObject);
            Assert.True(mesh.Indices.Distinct().Count() > 3);
            Assert.NotEqual(mesh.Indices[0], mesh.Indices[1]);
            Assert.NotEqual(mesh.Indices[1], mesh.Indices[2]);
        }
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void ImportSkeletonReturnsSkeletonAlone()
    {
        var importer = new GrannyModelImporter();
        using var fileSystem = Aoe3Install.CreateFileSystem();

        var skeleton = importer.ImportSkeleton(MusketeerPath, CreateContext(fileSystem));

        Assert.NotNull(skeleton);
        Assert.Equal(23, skeleton!.Bones.Count);
        Assert.Equal("Bip01", skeleton.Bones[0].Name);
        Assert.Equal("Bip01_Pelvis", skeleton.Bones[2].Name);
        Assert.Equal(0, skeleton.Bones[2].ParentIndex);
        Assert.Equal(-1, skeleton.Bones[0].ParentIndex);
    }

    /// <summary>
    /// Every bone's inverse bind matrix times its recomputed bind world matrix must be the
    /// identity within 1e-4 (card acceptance).
    /// </summary>
    private static void AssertBindPoseIsConsistent(ImportedModel model)
    {
        var bones = model.Skeleton.Bones;
        var worlds = new Matrix4x4[bones.Count];

        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            var local = bone.BindLocalMatrix;
            worlds[i] = bone.ParentIndex >= 0 ? local * worlds[bone.ParentIndex] : local;

            var product = bone.InverseBindMatrix * worlds[i];
            AssertIdentity(product, $"bone '{bone.Name}'");
        }
    }

    private static void AssertIdentity(Matrix4x4 matrix, string what)
    {
        var values = new[]
        {
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44,
        };

        for (var i = 0; i < values.Length; i++)
        {
            Assert.True(
                MathF.Abs(values[i] - (i % 5 == 0 ? 1.0f : 0.0f)) < 1e-4f,
                $"{what}: element {i} deviates from identity by more than 1e-4 (matrix\n{matrix}).");
        }
    }

    /// <summary>
    /// Rigid geometry is baked into the first bound bone's local space; transforming it back by
    /// that bone's bind world matrix recovers engine-space model bounds.
    /// </summary>
    private static (Vector3 Min, Vector3 Max) ModelSpaceBounds(ImportedModel model)
    {
        var bones = model.Skeleton.Bones;
        var worlds = new Matrix4x4[bones.Count];
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            var local = bone.BindLocalMatrix;
            worlds[i] = bone.ParentIndex >= 0 ? local * worlds[bone.ParentIndex] : local;
        }

        Vector3? min = null;
        Vector3? max = null;

        foreach (var subObject in model.SubObjects)
        {
            // Rigid sub-objects carry parent-bone-local vertices; skinned sub-objects already
            // live in skeleton-root bind (model) space.
            var world = subObject.RenderObject is ImportedMesh { IsSkinned: true }
                ? Matrix4x4.Identity
                : worlds[subObject.ParentBoneIndex];
            if (subObject.RenderObject is not ImportedMesh mesh)
            {
                continue;
            }

            var currentMin = min;
            var currentMax = max;

            foreach (var vertex in mesh.Vertices)
            {
                var position = Vector3.Transform(vertex.Position, world);
                currentMin = currentMin is { } lo ? Vector3.Min(lo, position) : position;
                currentMax = currentMax is { } hi ? Vector3.Max(hi, position) : position;
            }

            min = currentMin;
            max = currentMax;
        }

        Assert.NotNull(min);
        Assert.NotNull(max);
        return (min!.Value, max!.Value);
    }

    private static void AssertUprightPlausibleMeters(Vector3 min, Vector3 max, float minHeight, float maxHeight)
    {
        Assert.True(min.Z > -0.5f && min.Z < 0.5f, $"grounded: min Z {min.Z}");
        Assert.True(max.Z > minHeight && max.Z < maxHeight, $"plausible height: max Z {max.Z} m");
        Assert.True(max.X - min.X < 4.0f && max.Y - min.Y < 4.0f, $"plausible footprint");
    }
}
