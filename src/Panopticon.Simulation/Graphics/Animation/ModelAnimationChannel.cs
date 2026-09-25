using System;

namespace Panopticon.Graphics.Animation;

/// <summary>
/// Unbound runtime channel: target plus clip type and keyframes. Binding against a hierarchy
/// turns channels into playback clips.
/// </summary>
public sealed class ModelAnimationChannel
{
    public ModelAnimationTarget Target { get; }

    public AnimationClipType ClipType { get; }

    public AnimationInterpolation Interpolation { get; }

    public Keyframe[] Keyframes { get; }

    internal ModelAnimationChannel(
        ModelAnimationTarget target,
        AnimationClipType clipType,
        Keyframe[] keyframes,
        AnimationInterpolation interpolation)
    {
        Target = target;
        ClipType = clipType;
        Keyframes = keyframes;
        Interpolation = interpolation;
    }
}
