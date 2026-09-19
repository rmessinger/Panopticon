using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenSage.Content.Importing;
using OpenSage.Graphics;
using OpenSage.Graphics.Animation;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using Xunit;

namespace OpenSage.Tests.Graphics.Animation;

public class AnimationInstanceTests
{
    [Fact]
    public void OnceForwardsTest()
    {
        var modelBoneInstances = NewDefaultModelBoneInstances();
        var animation = NewBasicAnimationInstance();

        var instance = CreateInstance(modelBoneInstances, animation, AnimationMode.Once, AnimationFlags.StartFrameFirst);

        instance.Play();
        var time = TimeInterval.Zero;
        for (var i = 0; i < 5; i++)
        {
            instance.Update(time);
            time = new TimeInterval(time.TotalTime + TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1));

            Assert.Equal(i, modelBoneInstances[0].AnimatedOffset.Translation.X);
        }
    }

    [Fact]
    public void OnceBackwardsTest()
    {
        var modelBoneInstances = NewDefaultModelBoneInstances();
        var animation = NewBasicAnimationInstance();

        var instance = CreateInstance(modelBoneInstances, animation, AnimationMode.OnceBackwards, AnimationFlags.StartFrameLast);

        instance.Play();
        var time = TimeInterval.Zero;
        for (var i = 4; i >= 0; i--)
        {
            instance.Update(time);
            time = new TimeInterval(time.TotalTime + TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1));

            Assert.Equal(i, modelBoneInstances[0].AnimatedOffset.Translation.X);
        }
    }

    [Fact]
    public void StartAtEndForwardsTest()
    {
        var modelBoneInstances = NewDefaultModelBoneInstances();
        var animation = NewBasicAnimationInstance();

        var instance = CreateInstance(modelBoneInstances, animation, AnimationMode.Once, AnimationFlags.StartFrameLast);

        instance.Play();
        var time = TimeInterval.Zero;
        for (var i = 4; i >= 0; i--)
        {
            instance.Update(time);
            time = new TimeInterval(time.TotalTime + TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1));

            // we should always show the last frame
            Assert.Equal(4, modelBoneInstances[0].AnimatedOffset.Translation.X);
        }
    }

    [Fact]
    public void StartAtBeginningBackwardsTest()
    {
        var modelBoneInstances = NewDefaultModelBoneInstances();
        var animation = NewBasicAnimationInstance();

        var instance = CreateInstance(modelBoneInstances, animation, AnimationMode.OnceBackwards, AnimationFlags.StartFrameFirst);

        instance.Play();
        var time = TimeInterval.Zero;
        for (var i = 4; i >= 0; i--)
        {
            instance.Update(time);
            time = new TimeInterval(time.TotalTime + TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1));

            // we should always show the first frame
            Assert.Equal(0, modelBoneInstances[0].AnimatedOffset.Translation.X);
        }
    }

    [Fact]
    public void StartAtBeginningManualTest()
    {
        var modelBoneInstances = NewDefaultModelBoneInstances();
        var animation = NewBasicAnimationInstance();

        var instance = CreateInstance(modelBoneInstances, animation, AnimationMode.Manual, AnimationFlags.StartFrameFirst);

        instance.Play();
        var time = TimeInterval.Zero;
        for (var i = 0; i < 5; i++)
        {
            instance.Update(time);
            time = new TimeInterval(time.TotalTime + TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1));

            // always show the first frame, since the mode is manual
            Assert.Equal(0, modelBoneInstances[0].AnimatedOffset.Translation.X);
        }
    }

    [Fact]
    public void StartAtEndManualTest()
    {
        var modelBoneInstances = NewDefaultModelBoneInstances();
        var animation = NewBasicAnimationInstance();

        var instance = CreateInstance(modelBoneInstances, animation, AnimationMode.Manual, AnimationFlags.StartFrameLast);

        instance.Play();
        var time = TimeInterval.Zero;
        for (var i = 0; i < 5; i++)
        {
            instance.Update(time);
            time = new TimeInterval(time.TotalTime + TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1));

            // always show the last frame
            Assert.Equal(4, modelBoneInstances[0].AnimatedOffset.Translation.X);
        }
    }

    [Fact]
    public void LoopForwardsTest()
    {
        var modelBoneInstances = NewDefaultModelBoneInstances();
        var animation = NewBasicAnimationInstance();

        var instance = CreateInstance(modelBoneInstances, animation, AnimationMode.Loop, AnimationFlags.StartFrameFirst);

        instance.Play();
        var time = TimeInterval.Zero;
        for (var i = 0; i < 15; i++)
        {
            instance.Update(time);
            time = new TimeInterval(time.TotalTime + TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1));

            Assert.Equal(i % 5, modelBoneInstances[0].AnimatedOffset.Translation.X);
        }
    }

    [Fact]
    public void LoopBackwardsTest()
    {
        var modelBoneInstances = NewDefaultModelBoneInstances();
        var animation = NewBasicAnimationInstance();

        var instance = CreateInstance(modelBoneInstances, animation, AnimationMode.LoopBackwards, AnimationFlags.StartFrameLast);

        instance.Play();
        var time = TimeInterval.Zero;
        for (var i = 14; i >= 0; i--)
        {
            instance.Update(time);
            time = new TimeInterval(time.TotalTime + TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1));

            Assert.Equal(i % 5, modelBoneInstances[0].AnimatedOffset.Translation.X);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1111)]
    [InlineData(2222)]
    [InlineData(3333)]
    [InlineData(4000)]
    [InlineData(4001)]
    [InlineData(5000)]
    [InlineData(5001)]
    public void OnceForwardsRandomStartTest(int rngSeed)
    {
        var modelBoneInstances = NewDefaultModelBoneInstances();
        var animation = NewBasicAnimationInstance();
        var random = new QuoteUnquoteRandom(rngSeed);

        var instance = CreateInstance(modelBoneInstances, animation, AnimationMode.Once,
            AnimationFlags.RandomStart, random);

        instance.Play();
        var time = TimeInterval.Zero;
        float? previous = null;
        for (var i = 0; i < 5; i++)
        {
            instance.Update(time);
            time = new TimeInterval(time.TotalTime + TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1));

            if (previous is null)
            {
                previous = modelBoneInstances[0].AnimatedOffset.Translation.X;
                continue;
            }

            var target = previous.Value + 1;
            if (target > animation.Channels[0].Keyframes.Last().Value.FloatValue)
            {
                target = animation.Channels[0].Keyframes.Last().Value.FloatValue;
            }

            Assert.Equal(target, modelBoneInstances[0].AnimatedOffset.Translation.X, new ToleranceEqualityComparer(0.001f));
            previous = modelBoneInstances[0].AnimatedOffset.Translation.X;
        }
        instance.Stop();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1111)]
    [InlineData(2222)]
    [InlineData(3333)]
    [InlineData(4000)]
    [InlineData(4001)]
    [InlineData(5000)]
    [InlineData(5001)]
    public void OnceBackwardsRandomStartTest(int rngSeed)
    {
        var modelBoneInstances = NewDefaultModelBoneInstances();
        var animation = NewBasicAnimationInstance();
        var random = new QuoteUnquoteRandom(rngSeed);

        var instance = CreateInstance(modelBoneInstances, animation, AnimationMode.OnceBackwards,
            AnimationFlags.RandomStart, random);

        instance.Play();

        var time = TimeInterval.Zero;
        float? previous = null;
        for (var i = 5; i > 0; i--)
        {
            instance.Update(time);
            time = new TimeInterval(time.TotalTime + TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1));
            if (previous is null)
            {
                previous = modelBoneInstances[0].AnimatedOffset.Translation.X;
                continue;
            }

            var target = previous.Value - 1;
            if (target < 0)
            {
                target = 0;
            }

            Assert.Equal(target, modelBoneInstances[0].AnimatedOffset.Translation.X, new ToleranceEqualityComparer(0.001f));
            previous = modelBoneInstances[0].AnimatedOffset.Translation.X;
        }
        instance.Stop();
    }

    [Fact]
    public void StepChannel_HoldsPreviousValueBetweenKeys_Forward()
    {
        var modelBoneInstances = NewDefaultModelBoneInstances();
        var animation = NewStepAnimation(AnimationClipType.TranslationX);

        var instance = CreateInstance(modelBoneInstances, animation, AnimationMode.Once, AnimationFlags.StartFrameFirst);
        instance.Play();

        instance.Update(Step(0.5f));
        Assert.Equal(0, modelBoneInstances[0].AnimatedOffset.Translation.X);

        instance.Update(Step(0.5f));
        Assert.Equal(1, modelBoneInstances[0].AnimatedOffset.Translation.X);

        instance.Update(Step(0.75f));
        Assert.Equal(1, modelBoneInstances[0].AnimatedOffset.Translation.X);
    }

    [Fact]
    public void StepChannel_HoldsValueBetweenKeys_Reverse()
    {
        var modelBoneInstances = NewDefaultModelBoneInstances();
        var animation = NewStepAnimation(AnimationClipType.TranslationX);

        var instance = CreateInstance(modelBoneInstances, animation, AnimationMode.OnceBackwards, AnimationFlags.StartFrameLast);
        instance.Play();

        // Starts at the last key (t=2); stepping back crosses keys at 1.75, then exactly 1.0.
        instance.Update(Step(0.25f));
        Assert.Equal(1, modelBoneInstances[0].AnimatedOffset.Translation.X);

        instance.Update(Step(0.75f));
        Assert.Equal(1, modelBoneInstances[0].AnimatedOffset.Translation.X);

        instance.Update(Step(0.5f));
        Assert.Equal(0, modelBoneInstances[0].AnimatedOffset.Translation.X);
    }

    [Fact]
    public void StepQuaternionChannel_HoldsPreviousRotationBetweenKeys()
    {
        var modelBoneInstances = NewDefaultModelBoneInstances();
        var first = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 8);
        var second = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4);
        var animation = new ModelAnimation(string.Empty, TimeSpan.FromSeconds(2),
        [
            new ModelAnimationChannel(
                ModelAnimationTarget.CreateDirectIndex(0),
                AnimationClipType.Quaternion,
                new[]
                {
                    new Keyframe(TimeSpan.FromSeconds(0), new KeyframeValue { Quaternion = first }),
                    new Keyframe(TimeSpan.FromSeconds(1), new KeyframeValue { Quaternion = second }),
                },
                AnimationInterpolation.Step),
        ]);

        var instance = CreateInstance(modelBoneInstances, animation, AnimationMode.Once, AnimationFlags.StartFrameFirst);
        instance.Play();

        instance.Update(Step(0.5f));
        Assert.Equal(first, modelBoneInstances[0].AnimatedOffset.Rotation);

        instance.Update(Step(0.5f));
        Assert.Equal(second, modelBoneInstances[0].AnimatedOffset.Rotation);
    }

    [Fact]
    public void LinearChannel_InterpolatesMidpoint()
    {
        var modelBoneInstances = NewDefaultModelBoneInstances();
        var animation = NewStepAnimation(AnimationClipType.TranslationX, AnimationInterpolation.Linear);

        var instance = CreateInstance(modelBoneInstances, animation, AnimationMode.Once, AnimationFlags.StartFrameFirst);
        instance.Play();

        instance.Update(Step(0.5f));
        Assert.Equal(0.5f, modelBoneInstances[0].AnimatedOffset.Translation.X, 3);

        instance.Update(Step(1.0f));
        Assert.Equal(1.5f, modelBoneInstances[0].AnimatedOffset.Translation.X, 3);
    }

    private static TimeInterval Step(float deltaSeconds)
    {
        return new TimeInterval(TimeSpan.Zero, TimeSpan.FromSeconds(deltaSeconds));
    }

    private static ModelAnimation NewStepAnimation(
        AnimationClipType clipType,
        AnimationInterpolation interpolation = AnimationInterpolation.Step)
    {
        return new ModelAnimation(string.Empty, TimeSpan.FromSeconds(2),
        [
            new ModelAnimationChannel(
                ModelAnimationTarget.CreateDirectIndex(0),
                clipType,
                new[]
                {
                    new Keyframe(TimeSpan.FromSeconds(0), new KeyframeValue { FloatValue = 0 }),
                    new Keyframe(TimeSpan.FromSeconds(1), new KeyframeValue { FloatValue = 1 }),
                    new Keyframe(TimeSpan.FromSeconds(2), new KeyframeValue { FloatValue = 2 }),
                },
                interpolation),
        ]);
    }

    private static AnimationInstance CreateInstance(
        ModelBoneInstance[] modelBoneInstances,
        ModelAnimation animation,
        AnimationMode mode,
        AnimationFlags flags,
        IRandom? random = null)
    {
        var hierarchy = new ModelBoneHierarchy(new ImportedSkeleton(
            "TestSkeleton",
            new[] { new ImportedBone("myBone", -1, Vector3.Zero, Quaternion.Identity) }));

        return new AnimationInstance(
            modelBoneInstances, animation.Bind(hierarchy), mode, flags, null, random ?? new QuoteUnquoteRandom());
    }

    private static ModelBoneInstance[] NewDefaultModelBoneInstances()
    {
        var testBone = new ModelBone(0, "myBone", null, Vector3.Zero, Quaternion.Identity, Matrix4x4.Identity);
        return new[]
        {
            new ModelBoneInstance(testBone)
        };
    }

    private static ModelAnimation NewBasicAnimationInstance()
    {
        var animationChannels = new[]
        {
            new ModelAnimationChannel(
                ModelAnimationTarget.CreateDirectIndex(0),
                AnimationClipType.TranslationX,
                new[]
            {
                new Keyframe(TimeSpan.FromSeconds(0), new KeyframeValue
                {
                    FloatValue = 0,
                }),
                new Keyframe(TimeSpan.FromSeconds(1), new KeyframeValue
                {
                    FloatValue = 1,
                }),
                new Keyframe(TimeSpan.FromSeconds(2), new KeyframeValue
                {
                    FloatValue = 2,
                }),
                new Keyframe(TimeSpan.FromSeconds(3), new KeyframeValue
                {
                    FloatValue = 3,
                }),
                new Keyframe(TimeSpan.FromSeconds(4), new KeyframeValue
                {
                    FloatValue = 4,
                }),
            },
            AnimationInterpolation.Linear)
        };

        return new ModelAnimation(string.Empty, TimeSpan.FromSeconds(5), animationChannels);
    }
}

/// <summary>
/// Guaranteed random!
/// </summary>
internal class QuoteUnquoteRandom : IRandom
{
    private readonly int _random;

    public uint Seed => 0;

    /// <summary>
    /// Constructs a new "random" number generator which will always return 0.
    /// </summary>
    public QuoteUnquoteRandom() : this(0)
    {
    }

    public void Initialize(uint seed) { }

    /// <summary>
    /// Constructs a new "random" number generator.
    /// </summary>
    /// <param name="random">The "random" value which should always be returned.</param>
    public QuoteUnquoteRandom(int random)
    {
        _random = random;
    }

    public int Next(int lo, int hi)
    {
        return _random;
    }

    public float NextSingle(float lo, float hi)
    {
        return _random;
    }
}

internal class ToleranceEqualityComparer : IEqualityComparer<float>
{
    private readonly float _tolerance;

    public ToleranceEqualityComparer(float tolerance)
    {
        _tolerance = tolerance;
    }

    public bool Equals(float x, float y)
    {
        return Math.Abs(x - y) <= _tolerance;
    }

    public int GetHashCode(float obj)
    {
        return obj.GetHashCode();
    }
}
