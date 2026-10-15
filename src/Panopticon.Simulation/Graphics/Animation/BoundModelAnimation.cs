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

    public AnimationClip[] Clips { get; }

    internal MorphWeightTrack[] MorphTracks { get; }

    internal BoundModelAnimation(
        string name,
        TimeSpan duration,
        AnimationClip[] clips,
        MorphWeightTrack[] morphTracks = null)
    {
        Name = name;
        Duration = duration;
        Clips = clips;
        MorphTracks = morphTracks ?? Array.Empty<MorphWeightTrack>();
    }
}
