using System;

namespace OpenSage.Graphics.Animation;

/// <summary>
/// Unbound runtime channel: target plus clip type and keyframes. Binding against a hierarchy
/// turns channels into playback clips.
/// </summary>
public sealed class ModelAnimationChannel
{
    public ModelAnimationTarget Target { get; }

    public AnimationClipType ClipType { get; }

    public Keyframe[] Keyframes { get; }

    internal ModelAnimationChannel(ModelAnimationTarget target, AnimationClipType clipType, Keyframe[] keyframes)
    {
        Target = target;
        ClipType = clipType;
        Keyframes = keyframes;
    }
}
