using System;
using System.Numerics;
using OpenSage.Content.Importing;
using OpenSage.Content.Loaders;
using OpenSage.Graphics.Animation;
using Xunit;

namespace OpenSage.Tests.Graphics.Animation;

public sealed class ModelAnimationTests
{
    [Fact]
    public void Materialize_PreservesNameDurationAndClips()
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4);
        var imported = new ImportedAnimation(
            "HV.Run",
            TimeSpan.FromSeconds(2.0),
            new[]
            {
                new ImportedAnimationChannel(
                    boneIndex: 3,
                    ImportedAnimationChannelType.Quaternion,
                    new[]
                    {
                        ImportedKeyframe.FromQuaternion(TimeSpan.Zero, Quaternion.Identity),
                        ImportedKeyframe.FromQuaternion(TimeSpan.FromSeconds(0.2), rotation),
                    }),
                new ImportedAnimationChannel(
                    boneIndex: 1,
                    ImportedAnimationChannelType.TranslationX,
                    new[]
                    {
                        ImportedKeyframe.FromFloat(TimeSpan.Zero, 1.0f),
                    }),
                new ImportedAnimationChannel(
                    boneIndex: 2,
                    ImportedAnimationChannelType.Visibility,
                    new[]
                    {
                        ImportedKeyframe.FromBool(TimeSpan.Zero, true),
                    }),
            });

        var animation = ImportedAnimationMaterializer.Materialize(imported);

        Assert.Equal("HV.Run", animation.Name);
        Assert.Equal(TimeSpan.FromSeconds(2.0), animation.Duration);
        Assert.Equal(3, animation.Clips.Length);

        Assert.Equal(AnimationClipType.Quaternion, animation.Clips[0].ClipType);
        Assert.Equal(3, animation.Clips[0].Bone);
        Assert.Equal(rotation, animation.Clips[0].Keyframes[1].Value.Quaternion);

        Assert.Equal(AnimationClipType.TranslationX, animation.Clips[1].ClipType);
        Assert.Equal(1.0f, animation.Clips[1].Keyframes[0].Value.FloatValue);

        Assert.Equal(AnimationClipType.Visibility, animation.Clips[2].ClipType);
        Assert.True(animation.Clips[2].Keyframes[0].Value.BoolValue);
    }
}
