using System;

namespace OpenSage.Graphics.Animation;

/// <summary>
/// Animation bound to a specific hierarchy: every channel resolved to a bone index and frozen
/// into an immutable playback clip array.
/// </summary>
public sealed class BoundModelAnimation
{
    public string Name { get; }

    public TimeSpan Duration { get; }

    public AnimationClip[] Clips { get; }

    internal BoundModelAnimation(string name, TimeSpan duration, AnimationClip[] clips)
    {
        Name = name;
        Duration = duration;
        Clips = clips;
    }
}
