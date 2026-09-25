using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Nif;
using Panopticon.Content.Loaders;
using Panopticon.Graphics;
using Panopticon.Graphics.Animation;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

/// <summary>
/// WI-C4d-07: locks the installed EE2 skinned proof unit to the descriptors recorded
/// in docs/roadmap/deep-dives/c4d-ee2-skinned-animation.md sections 2 and 11, plus
/// the two negative fixtures from section 2. Interactive: requires
/// EMPIRE_EARTH_II_PATH with zips/graphics.zip. No EE2 asset is copied or committed.
/// </summary>
[Trait("Category", "Interactive")]
public sealed class Ee2SkinnedImportTests
{
    private const string ArchiveRelativePath = "zips/graphics.zip";
    private const string DruidKey = "CIV1_Druid_british";

    private static readonly (string KfPath, double DurationSeconds)[] Clips =
    [
        ("Graphics/Humans/CIV1_Druid_british_idle01.kf", 1.0),
        ("Graphics/Humans/CIV1_Druid_british_walk01.kf", 0.8),
        ("Graphics/Humans/CIV1_Druid_british_jog01.kf", 0.6667),
        ("Graphics/Humans/CIV1_Druid_british_attack01.kf", 1.8667),
        ("Graphics/Humans/CIV1_Druid_british_death01.kf", 1.2333),
    ];

    private static (FileSystem FileSystem, NifModelImporter Importer) OpenArchive()
    {
        var root = Environment.GetEnvironmentVariable("EMPIRE_EARTH_II_PATH");
        if (string.IsNullOrEmpty(root))
        {
            throw new InvalidOperationException(
                "Interactive test requires EMPIRE_EARTH_II_PATH pointing at an Empire Earth II install.");
        }

        var archivePath = Path.Combine(root, ArchiveRelativePath);
        if (!File.Exists(archivePath))
        {
            throw new InvalidOperationException(
                $"Interactive test requires the EE2 graphics archive at '{archivePath}'.");
        }

        return (new ZipFileSystem(archivePath), new NifModelImporter());
    }

    private static ImportedModel Import(string key)
    {
        var (fileSystem, importer) = OpenArchive();
        using var _ = fileSystem;
        var context = new AssetImportContext(fileSystem, language: null);
        return importer.ImportModel(key, context)
            ?? throw new InvalidOperationException($"Import returned null for '{key}'.");
    }

    private static ImportedAnimation ImportAnimation(string modelKey, string kfPath, FileSystem? fileSystem = null)
    {
        var (archive, _) = OpenArchive();
        using var _ = fileSystem is null ? archive : null;
        var effective = fileSystem ?? archive;
        var context = new AssetImportContext(effective, language: null);
        var importer = new NifAnimationImporter();
        return importer.ImportAnimation($"{modelKey}#kf={kfPath}", context)
            ?? throw new InvalidOperationException($"Animation import returned null for '{modelKey}#kf={kfPath}'.");
    }

    private static ImportedMesh MeshOf(ImportedModel model, int index = 0)
    {
        return (ImportedMesh)model.SubObjects[index].RenderObject;
    }

    [Fact]
    public void DruidMatchesDeepDiveSkinDescriptor()
    {
        var model = Import(DruidKey);

        Assert.Equal(DruidKey, model.Name);
        Assert.Equal(32, model.Skeleton.Bones.Count);
        Assert.Equal(-1, model.Skeleton.Bones[0].ParentIndex);
        Assert.Equal("CIV1_Druid_british.nif", model.Skeleton.Bones[0].Name);
        Assert.Contains(model.Skeleton.Bones, b => b.Name == "Bip01");

        // The four body_LODn shapes collapse to the single highest-detail LOD0.
        Assert.Single(model.SubObjects);
        Assert.Equal("body_LOD0", model.SubObjects[0].DisplayName);
        Assert.Equal(0, model.SubObjects[0].ParentBoneIndex);

        var mesh = MeshOf(model);
        Assert.True(mesh.IsSkinned);
        Assert.Equal(648, mesh.Vertices.Count);
        Assert.Equal(842, mesh.Indices.Count / 3);

        // Influence histogram from the deep dive: 486/158/2/2 across one to four slots.
        var histogram = new int[5];
        foreach (var vertex in mesh.Vertices)
        {
            var slots = CountWeightedSlots(vertex);
            Assert.InRange(slots, 1, 4);
            histogram[slots]++;

            var sum = vertex.BoneWeight0 + vertex.BoneWeight1 + vertex.BoneWeight2 + vertex.BoneWeight3;
            Assert.True(
                MathF.Abs(sum - 1.0f) < 1e-4f,
                $"Vertex weights must sum to 1, got {sum}.");
        }

        Assert.Equal(486, histogram[1]);
        Assert.Equal(158, histogram[2]);
        Assert.Equal(2, histogram[3]);
        Assert.Equal(2, histogram[4]);

        // The only two four-influence vertices keep their beyond-two weights
        // (deep dive: 0.5 combined past the primary pair on both).
        for (var i = 0; i < mesh.Vertices.Count; i++)
        {
            var vertex = mesh.Vertices[i];
            if (vertex.BoneWeight3 > 0.0f)
            {
                Assert.True(i == 151 || i == 153, $"Unexpected four-slot vertex at {i}.");
                Assert.True(
                    MathF.Abs(vertex.BoneWeight2 + vertex.BoneWeight3 - 0.5f) < 1e-3f,
                    $"Vertex {i} carries {vertex.BoneWeight2 + vertex.BoneWeight3} beyond its primary influences, expected 0.5.");
            }
        }

        Assert.True(mesh.Vertices[151].BoneWeight2 > 0.0f && mesh.Vertices[151].BoneWeight3 > 0.0f);
        Assert.True(mesh.Vertices[153].BoneWeight2 > 0.0f && mesh.Vertices[153].BoneWeight3 > 0.0f);
    }

    [Fact]
    public void DruidBindPoseParityHoldsAtInverseBinds()
    {
        var model = Import(DruidKey);
        var bones = model.Skeleton.Bones;

        // Deep dive section 7.3: posing every bone at the inverse of its
        // inverse bind makes each skinning matrix identity, so the weighted
        // deformation reproduces the imported model-space positions.
        var targets = new Matrix4x4[bones.Count];
        for (var i = 0; i < bones.Count; i++)
        {
            Assert.True(
                Matrix4x4.Invert(bones[i].InverseBindMatrix, out var target),
                $"Inverse bind of bone {i} ('{bones[i].Name}') must be invertible.");
            targets[i] = target;
        }

        var composed = new Matrix4x4[bones.Count];
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (bone.ParentIndex < 0)
            {
                composed[i] = targets[i];
                continue;
            }

            Assert.True(Matrix4x4.Invert(targets[bone.ParentIndex], out var inverseParentWorld));
            composed[i] = targets[i] * inverseParentWorld * composed[bone.ParentIndex];
        }

        var skinning = new Matrix4x4[bones.Count];
        for (var i = 0; i < bones.Count; i++)
        {
            skinning[i] = bones[i].InverseBindMatrix * composed[i];
        }

        var mesh = MeshOf(model);
        for (var v = 0; v < mesh.Vertices.Count; v++)
        {
            var vertex = mesh.Vertices[v];
            var deformed =
                vertex.BoneWeight0 * Vector3.Transform(vertex.Position, skinning[vertex.BoneIndex0])
                + vertex.BoneWeight1 * Vector3.Transform(vertex.Position, skinning[vertex.BoneIndex1])
                + vertex.BoneWeight2 * Vector3.Transform(vertex.Position, skinning[vertex.BoneIndex2])
                + vertex.BoneWeight3 * Vector3.Transform(vertex.Position, skinning[vertex.BoneIndex3]);

            Assert.True(
                (deformed - vertex.Position).Length() < 1e-4f,
                $"Vertex {v} deformed to {deformed}, expected {vertex.Position}.");
        }
    }

    [Fact]
    public void DruidFiveClipsBindAndSampleAtTimeZero()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        var (archive, importer) = OpenArchive();
        using var archiveLease = archive;
        using var graphics = new TestGraphicsContext(archive);
        var importContext = new AssetImportContext(archive, language: null);

        var importedModel = importer.ImportModel(DruidKey, importContext)
            ?? throw new InvalidOperationException($"Model import returned null for '{DruidKey}'.");
        var boneNames = importedModel.Skeleton.Bones.Select(b => b.Name).ToHashSet();

        var model = ImportedModelMaterializer.MaterializeModel(importedModel, graphics.Context);
        var instance = new ModelInstance(model, graphics.Context);
        instance.Update(new TimeInterval(TimeSpan.Zero, TimeSpan.Zero));

        foreach (var (kfPath, durationSeconds) in Clips)
        {
            var imported = ImportAnimation(DruidKey, kfPath, archive);

            Assert.Equal(
                Path.GetFileNameWithoutExtension(kfPath),
                imported.Name);
            Assert.Equal(durationSeconds, imported.Duration.TotalSeconds, precision: 3);

            foreach (var channel in imported.Channels)
            {
                Assert.True(
                    boneNames.Contains(channel.Target.BoneName),
                    $"{kfPath}: channel target '{channel.Target.BoneName}' is not a skeleton bone.");
            }

            PlayTo(instance, ImportedAnimationMaterializer.Materialize(imported), 0f);
        }
    }

    [Fact]
    public void Walk01RestKeyedChannelsHaveIdentityOffsetsAtTimeZero()
    {
        // Deep dive section 9: after the rotation-convention fix, the walk01
        // quaternion channels whose time-0 key equals the rest pose import as
        // identity offsets. The imported key value IS the offset.
        var walkPath = Clips[1].KfPath;
        var animation = ImportAnimation(DruidKey, walkPath);

        var restKeyed = animation.Channels
            .Where(c => c.ChannelType == ImportedAnimationChannelType.Quaternion
                && c.Keyframes[0].Time == TimeSpan.Zero
                && MathF.Abs(c.Keyframes[0].QuaternionValue.W) > 0.9999f)
            .Select(c => c.Target.BoneName)
            .ToList();

        Assert.Equal(17, restKeyed.Count);

        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        var (archive, importer) = OpenArchive();
        using var archiveLease = archive;
        using var graphics = new TestGraphicsContext(archive);
        var importContext = new AssetImportContext(archive, language: null);

        var importedModel = importer.ImportModel(DruidKey, importContext)
            ?? throw new InvalidOperationException($"Model import returned null for '{DruidKey}'.");
        var model = ImportedModelMaterializer.MaterializeModel(importedModel, graphics.Context);
        var instance = new ModelInstance(model, graphics.Context);
        instance.Update(new TimeInterval(TimeSpan.Zero, TimeSpan.Zero));

        PlayTo(instance, ImportedAnimationMaterializer.Materialize(animation), 0f);

        foreach (var boneName in restKeyed)
        {
            var bone = Assert.Single(model.BoneHierarchy.Bones, b => b.Name == boneName);
            var offset = instance.ModelBoneInstances[bone.Index].AnimatedOffset;
            Assert.True(
                Quaternion.Dot(offset.Rotation, Quaternion.Identity) > 0.9999995f,
                $"Bone '{boneName}' must carry an identity rotation offset at walk01 time 0.");
        }
    }

    [Fact]
    public void PriestFiveInfluencesFailNamingShapeAndCount()
    {
        var ex = Record.Exception(() => Import("lsf7_priest_fe"));

        var notSupported = Assert.IsType<NotSupportedException>(ex);
        Assert.Contains("body_LOD0", notSupported.Message);
        Assert.Contains("5 meaningful skin influences", notSupported.Message);
    }

    [Fact]
    public void MountedSlingerWeightSumFailsNamingShape()
    {
        var ex = Record.Exception(() => Import("llm2_mountedslinger_fe"));

        var invalidData = Assert.IsType<InvalidDataException>(ex);
        Assert.Contains("horse_LOD0", invalidData.Message);
    }

    private static int CountWeightedSlots(ImportedVertex vertex)
    {
        var count = 0;
        if (vertex.BoneWeight0 > 0.0f)
        {
            count++;
        }

        if (vertex.BoneWeight1 > 0.0f)
        {
            count++;
        }

        if (vertex.BoneWeight2 > 0.0f)
        {
            count++;
        }

        if (vertex.BoneWeight3 > 0.0f)
        {
            count++;
        }

        return count;
    }

    private static void PlayTo(ModelInstance instance, ModelAnimation animation, float seconds)
    {
        var animationInstance = instance.CreateAnimationInstance(
            animation,
            AnimationMode.Once,
            AnimationFlags.StartFrameFirst,
            gameObject: null!,
            random: new ConstantRandom());

        animationInstance.Play();

        const int steps = 8;
        var delta = TimeSpan.FromSeconds(seconds / steps);
        for (var i = 0; i < steps; i++)
        {
            animationInstance.Update(new TimeInterval(TimeSpan.Zero, delta));
        }

        instance.Update(new TimeInterval(TimeSpan.FromSeconds(seconds), delta));
    }

    private sealed class ConstantRandom : IRandom
    {
        public uint Seed => 0;

        public void Initialize(uint seed)
        {
        }

        public int Next(int lo, int hi) => lo;

        public float NextSingle(float lo, float hi) => lo;
    }
}
