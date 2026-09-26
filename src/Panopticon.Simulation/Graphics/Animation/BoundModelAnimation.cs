using System;

namespace Panopticon.Graphics.Animation;

/// <summary>
/// Animation bound to a specific hierarchy: every channel resolved to a bone index and frozen
/// into an immutable playback clip array.
/// </summary>
public sealed class BoundModelAnimation
{
    public string Name { get; }

    public TimeSpan Duration { get; }

    public ModelAnimationComposition Composition { get; }

    public AnimationClip[] Clips { get; }

    internal BoundModelAnimation(string name, TimeSpan duration, AnimationClip[] clips)
        : this(name, duration, clips, ModelAnimationComposition.RestRelative)
    {
    }

    internal BoundModelAnimation(
        string name,
        TimeSpan duration,
        AnimationClip[] clips,
        ModelAnimationComposition composition)
    {
        Name = name;
        Duration = duration;
        Composition = composition;
        Clips = clips;
    }
}
