using System;

namespace OpenSage.Graphics.Animation;

/// <summary>
/// Format-neutral runtime animation asset: duration plus bone-indexed clips. Conversion from
/// imported values lives in the engine-owned materializer; no W3D parsing here.
/// </summary>
public sealed class ModelAnimation : BaseAsset
{
    public TimeSpan Duration { get; }

    public AnimationClip[] Clips { get; }

    internal ModelAnimation(string name, TimeSpan duration, AnimationClip[] clips)
    {
        SetNameAndInstanceId("ModelAnimation", name);
        Duration = duration;
        Clips = clips;
    }
}
