using System;
using System.Collections.Generic;

namespace Panopticon.Graphics.Animation;

/// <summary>
/// One morph weight keyframe: the time plus one weight per channel target, in
/// channel target order.
/// </summary>
internal readonly struct MorphWeightKeyframe
{
    public TimeSpan Time { get; }

    public float[] Weights { get; }

    public MorphWeightKeyframe(TimeSpan time, float[] weights)
    {
        Time = time;
        Weights = weights;
    }
}

/// <summary>
/// One morph weight track bound against a model: the sub-object whose mesh owns
/// the morph target set, the slot each channel target writes into, and the
/// sampled weight keyframes.
/// </summary>
internal sealed class MorphWeightTrack
{
    public int SubObjectIndex { get; }

    public string PrimitiveId { get; }

    public int[] TargetSlots { get; }

    public MorphWeightKeyframe[] Keyframes { get; }

    public AnimationInterpolation Interpolation { get; }

    public MorphWeightTrack(
        int subObjectIndex,
        string primitiveId,
        int[] targetSlots,
        MorphWeightKeyframe[] keyframes,
        AnimationInterpolation interpolation)
    {
        SubObjectIndex = subObjectIndex;
        PrimitiveId = primitiveId;
        TargetSlots = targetSlots;
        Keyframes = keyframes;
        Interpolation = interpolation;
    }
}
