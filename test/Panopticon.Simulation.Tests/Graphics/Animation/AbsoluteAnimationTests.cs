using System;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Graphics;
using Panopticon.Graphics.Animation;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Graphics.Animation;

public sealed class AbsoluteAnimationTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void AbsoluteLocalRotation_SeedsUntargetedComponentsFromBindPose()
    {
        var hierarchy = CreateHierarchy();
        var boneInstances = CreateBoneInstances(hierarchy);
        var endRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2.0f);
        var animation = new ModelAnimation(
            "absolute",
            TimeSpan.FromSeconds(2),
            new[]
            {
                new ModelAnimationChannel(
                    ModelAnimationTarget.CreateDirectIndex(1),
                    AnimationClipType.Quaternion,
                    new[]
                    {
                        new Keyframe(TimeSpan.Zero, new KeyframeValue { Quaternion = Quaternion.Identity }),
                        new Keyframe(TimeSpan.FromSeconds(1), new KeyframeValue { Quaternion = endRotation }),
                    },
                    AnimationInterpolation.Linear),
            },
            ModelAnimationComposition.AbsoluteLocal);

        var instance = CreateInstance(boneInstances, animation, hierarchy);
        instance.Play();

        instance.Update(Step(0));
        Assert.True(boneInstances[1].AbsoluteLocalActive);
        AssertMatrixEqual(
            hierarchy.Bones[1].RestMatrix * hierarchy.Bones[0].RestMatrix,
            boneInstances[1].Matrix * boneInstances[0].Matrix);

        instance.Update(Step(1));
        var expectedChild =
            Matrix4x4.CreateScale(new Vector3(2, 3, 4)) *
            Matrix4x4.CreateFromQuaternion(endRotation) *
            Matrix4x4.CreateTranslation(1, 2, 3);
        AssertMatrixEqual(
            expectedChild * hierarchy.Bones[0].RestMatrix,
            boneInstances[1].Matrix * boneInstances[0].Matrix);

        instance.Stop();
        instance.Play();

        Assert.False(boneInstances[1].AbsoluteLocalActive);
        AssertMatrixEqual(hierarchy.Bones[1].RestMatrix, boneInstances[1].Matrix);
    }

    [Fact]
    public void AbsoluteLocalPerAxisScale_WritesScaleComponents()
    {
        var hierarchy = CreateHierarchy();
        var boneInstances = CreateBoneInstances(hierarchy);
        var animation = new ModelAnimation(
            "scale",
            TimeSpan.FromSeconds(1),
            new[]
            {
                CreateFloatChannel(AnimationClipType.ScaleX, 5.0f),
                CreateFloatChannel(AnimationClipType.ScaleY, 6.0f),
                CreateFloatChannel(AnimationClipType.ScaleZ, 7.0f),
            },
            ModelAnimationComposition.AbsoluteLocal);

        var instance = CreateInstance(boneInstances, animation, hierarchy);
        instance.Play();
        instance.Update(Step(0));

        Assert.Equal(new Vector3(5, 6, 7), boneInstances[1].AbsoluteLocalScale);
        AssertMatrixEqual(
            Matrix4x4.CreateScale(5, 6, 7) * Matrix4x4.CreateTranslation(1, 2, 3),
            boneInstances[1].Matrix);
    }

    [Fact]
    public void RestRelativeClips_KeepAbsoluteLocalStateInactive()
    {
        var hierarchy = CreateHierarchy();
        var boneInstances = CreateBoneInstances(hierarchy);
        var animation = new ModelAnimation(
            "relative",
            TimeSpan.FromSeconds(1),
            new[]
            {
                CreateFloatChannel(AnimationClipType.TranslationX, 4.0f),
                CreateFloatChannel(AnimationClipType.Scale, 3.0f),
            });

        var instance = CreateInstance(boneInstances, animation, hierarchy);
        instance.Play();
        instance.Update(Step(0));

        Assert.False(boneInstances[1].AbsoluteLocalActive);
        AssertMatrixEqual(
            Matrix4x4.CreateScale(3.0f) *
            Matrix4x4.CreateTranslation(4, 0, 0) *
            hierarchy.Bones[1].RestMatrix,
            boneInstances[1].Matrix);
    }

    private static ModelBoneHierarchy CreateHierarchy()
    {
        return new ModelBoneHierarchy(new ImportedSkeleton(
            "absolute-test",
            new[]
            {
                new ImportedBone("root", -1, new Vector3(10, 0, 0), Quaternion.Identity),
                new ImportedBone("child", 0, new Vector3(1, 2, 3), Quaternion.Identity, new Vector3(2, 3, 4)),
            }));
    }

    private static ModelBoneInstance[] CreateBoneInstances(ModelBoneHierarchy hierarchy)
    {
        return hierarchy.Bones.Select(bone => new ModelBoneInstance(bone)).ToArray();
    }

    private static AnimationInstance CreateInstance(
        ModelBoneInstance[] boneInstances,
        ModelAnimation animation,
        ModelBoneHierarchy hierarchy)
    {
        return new AnimationInstance(
            boneInstances,
            animation.Bind(hierarchy),
            AnimationMode.Once,
            AnimationFlags.StartFrameFirst,
            null,
            new QuoteUnquoteRandom());
    }

    private static ModelAnimationChannel CreateFloatChannel(AnimationClipType clipType, float value)
    {
        return new ModelAnimationChannel(
            ModelAnimationTarget.CreateDirectIndex(1),
            clipType,
            new[]
            {
                new Keyframe(TimeSpan.Zero, new KeyframeValue { FloatValue = value }),
            },
            AnimationInterpolation.Linear);
    }

    private static TimeInterval Step(float deltaSeconds)
    {
        return new TimeInterval(TimeSpan.Zero, TimeSpan.FromSeconds(deltaSeconds));
    }

    private static void AssertMatrixEqual(Matrix4x4 expected, Matrix4x4 actual)
    {
        Assert.Equal(expected.M11, actual.M11, Tolerance);
        Assert.Equal(expected.M12, actual.M12, Tolerance);
        Assert.Equal(expected.M13, actual.M13, Tolerance);
        Assert.Equal(expected.M14, actual.M14, Tolerance);
        Assert.Equal(expected.M21, actual.M21, Tolerance);
        Assert.Equal(expected.M22, actual.M22, Tolerance);
        Assert.Equal(expected.M23, actual.M23, Tolerance);
        Assert.Equal(expected.M24, actual.M24, Tolerance);
        Assert.Equal(expected.M31, actual.M31, Tolerance);
        Assert.Equal(expected.M32, actual.M32, Tolerance);
        Assert.Equal(expected.M33, actual.M33, Tolerance);
        Assert.Equal(expected.M34, actual.M34, Tolerance);
        Assert.Equal(expected.M41, actual.M41, Tolerance);
        Assert.Equal(expected.M42, actual.M42, Tolerance);
        Assert.Equal(expected.M43, actual.M43, Tolerance);
        Assert.Equal(expected.M44, actual.M44, Tolerance);
    }
}
