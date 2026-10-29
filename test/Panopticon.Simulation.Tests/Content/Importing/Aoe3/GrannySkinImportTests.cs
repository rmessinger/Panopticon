using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Granny;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.Aoe3;

public class GrannySkinImportTests
{
    private const string MusketeerPath = "Art\\units\\infantry_ranged\\musketeer\\musketeer_4age.gr2";
    private const string HorsePath = "Art\\units\\cavalry\\light_cavalry\\light_cavalry_age4_horse.gr2";

    private readonly ITestOutputHelper _output;

    public GrannySkinImportTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DuplicateBindingsAggregateOntoOneBone()
    {
        // Two slots reference binding 1 (bone 5); the weights add and, as the only
        // influence, renormalize to a full weight on the aggregated bone.
        var influences = GrannyMeshConverter.AggregateInfluences(
            [2, 5, 9, 12],
            boneIndices: [1, 1, 0, 0],
            boneWeights: [144, 80, 0, 0],
            fallbackBoneIndex: 2);

        Assert.Equal(5, influences.BoneIndex0);
        Assert.Equal(1.0f, influences.Weight0, 3);
        Assert.Equal(0, influences.BoneIndex1);
        Assert.Equal(0f, influences.Weight1);
    }

    [Fact]
    public void FourBonesRenormalizeToOne()
    {
        var influences = GrannyMeshConverter.AggregateInfluences(
            [10, 11, 12, 13],
            boneIndices: [0, 1, 2, 3],
            boneWeights: [100, 60, 50, 20],
            fallbackBoneIndex: 10);

        Assert.Equal(10, influences.BoneIndex0);
        Assert.Equal(11, influences.BoneIndex1);
        Assert.Equal(12, influences.BoneIndex2);
        Assert.Equal(13, influences.BoneIndex3);
        Assert.Equal(1.0f,
            influences.Weight0 + influences.Weight1 + influences.Weight2 + influences.Weight3,
            3);
        // Raw bytes sum to 230, so renormalization lifts every kept weight above its raw share.
        Assert.True(influences.Weight0 > 100f / 255f, "renormalized largest weight exceeds its raw share");
    }

    [Fact]
    public void AllZeroWeightsFallBackToFirstBoundBone()
    {
        var influences = GrannyMeshConverter.AggregateInfluences(
            [3, 7],
            boneIndices: [0, 1, 0, 0],
            boneWeights: [0, 0, 0, 0],
            fallbackBoneIndex: 3);

        Assert.Equal(3, influences.BoneIndex0);
        Assert.Equal(1.0f, influences.Weight0, 3);
    }

    [Fact]
    public void TiesBreakDeterministicallyByBoneIndex()
    {
        var influences = GrannyMeshConverter.AggregateInfluences(
            [4, 2, 9, 1],
            boneIndices: [0, 1, 2, 3],
            boneWeights: [128, 128, 0, 0],
            fallbackBoneIndex: 4);

        Assert.Equal(2, influences.BoneIndex0);
        Assert.Equal(4, influences.BoneIndex1);
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void MusketeerSkinWeightsSumToOneAndBindPoseIsIdentity()
    {
        var mesh = ImportSkinnedMesh(MusketeerPath, out var model);

        Assert.Equal(23, model.Skeleton.Bones.Count);
        var (maxWeightSumError, maxSkinError, histogram) = VerifySkinning(mesh, model);
        _output.WriteLine($"musketeer max weight-sum error {maxWeightSumError:F6}");
        _output.WriteLine($"musketeer max bind-pose skin error {maxSkinError:F6} m");
        _output.WriteLine("musketeer influence histogram: " + string.Join(
            ", ", histogram.OrderBy(p => p.Key).Select(p => $"{p.Key} influences: {p.Value} vertices")));

        Assert.True(maxWeightSumError < 1e-3f, $"weight sums deviate by {maxWeightSumError}");
        Assert.True(maxSkinError < 1e-3f, $"bind-pose skinning deviates by {maxSkinError} m");
        Assert.Contains(3, histogram.Keys);
        Assert.Contains("Bip01_Head", DominantBones(mesh, model));
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void HussarHorseSkinWeightsSumToOneAndBindPoseIsIdentity()
    {
        var mesh = ImportSkinnedMesh(HorsePath, out var model);

        Assert.Equal(32, model.Skeleton.Bones.Count);
        var (maxWeightSumError, maxSkinError, histogram) = VerifySkinning(mesh, model);
        _output.WriteLine($"horse max weight-sum error {maxWeightSumError:F6}");
        _output.WriteLine($"horse max bind-pose skin error {maxSkinError:F6} m");
        _output.WriteLine("horse influence histogram: " + string.Join(
            ", ", histogram.OrderBy(p => p.Key).Select(p => $"{p.Key} influences: {p.Value} vertices")));

        Assert.True(maxWeightSumError < 1e-3f, $"weight sums deviate by {maxWeightSumError}");
        Assert.True(maxSkinError < 1e-3f, $"bind-pose skinning deviates by {maxSkinError} m");
        Assert.Contains(4, histogram.Keys);
    }

    private static ImportedMesh ImportSkinnedMesh(string path, out ImportedModel model)
    {
        var importer = new GrannyModelImporter();
        using var fileSystem = Aoe3Install.CreateFileSystem();
        model = importer.ImportModel(path, new AssetImportContext(fileSystem, language: null))!;
        var mesh = Assert.IsType<ImportedMesh>(model.SubObjects[0].RenderObject);
        Assert.True(mesh.IsSkinned);
        Assert.Equal(0, model.SubObjects[0].ParentBoneIndex);
        return mesh;
    }

    private static (float MaxWeightSumError, float MaxSkinError, Dictionary<int, int> Histogram) VerifySkinning(
        ImportedMesh mesh,
        ImportedModel model)
    {
        var bones = model.Skeleton.Bones;
        var worlds = new Matrix4x4[bones.Count];
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            var local = Matrix4x4.CreateScale(bone.BindScale) *
                Matrix4x4.CreateFromQuaternion(bone.BindRotation) *
                Matrix4x4.CreateTranslation(bone.BindTranslation);
            worlds[i] = bone.ParentIndex >= 0 ? local * worlds[bone.ParentIndex] : local;
        }

        float maxWeightSumError = 0f;
        float maxSkinError = 0f;
        var histogram = new Dictionary<int, int>();

        foreach (var vertex in mesh.Vertices)
        {
            var slots = new[]
            {
                (Bone: vertex.BoneIndex0, Weight: vertex.BoneWeight0),
                (Bone: vertex.BoneIndex1, Weight: vertex.BoneWeight1),
                (Bone: vertex.BoneIndex2, Weight: vertex.BoneWeight2),
                (Bone: vertex.BoneIndex3, Weight: vertex.BoneWeight3),
            };

            var weightSum = slots.Sum(s => s.Weight);
            maxWeightSumError = MathF.Max(maxWeightSumError, MathF.Abs(weightSum - 1f));

            var influences = slots.Count(s => s.Weight > 0f);
            histogram.TryGetValue(influences, out var count);
            histogram[influences] = count + 1;

            var skinned = Vector3.Zero;
            foreach (var slot in slots)
            {
                if (slot.Weight == 0f)
                {
                    continue;
                }

                // Bind pose: world * inverseBind is identity per bone, so the weighted sum
                // reproduces the vertex position exactly when the import is consistent.
                var restore = worlds[(int)slot.Bone] * bones[(int)slot.Bone].InverseBindMatrix;
                skinned += Vector3.Transform(vertex.Position, restore) * slot.Weight;
            }

            maxSkinError = MathF.Max(maxSkinError, Vector3.Distance(skinned, vertex.Position));
        }

        return (maxWeightSumError, maxSkinError, histogram);
    }

    private static IReadOnlyCollection<string> DominantBones(ImportedMesh mesh, ImportedModel model)
    {
        // AggregateInfluences sorts descending, so slot 0 carries each vertex's dominant bone.
        return mesh.Vertices
            .Where(v => v.BoneWeight0 > 0f)
            .Select(v => model.Skeleton.Bones[(int)v.BoneIndex0].Name)
            .Distinct()
            .ToList();
    }
}
