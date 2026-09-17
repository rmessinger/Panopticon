using System;
using System.Linq;

namespace OpenSage.Graphics.Animation;

/// <summary>
/// Format-neutral runtime animation asset: duration plus unbound target-indexed channels.
/// Conversion from imported values lives in the engine-owned materializer; no W3D parsing here.
/// </summary>
public sealed class ModelAnimation : BaseAsset
{
    public TimeSpan Duration { get; }

    public ModelAnimationChannel[] Channels { get; }

    /// <summary>
    /// Compatibility clip surface for callers not yet binding through
    /// <see cref="Bind(ModelBoneHierarchy)"/>; removed by WI-C2-05. Derived from direct-index
    /// channels only, preserving their order; bone-name channels are not playable here.
    /// </summary>
    public AnimationClip[] Clips { get; }

    internal ModelAnimation(string name, TimeSpan duration, ModelAnimationChannel[] channels)
    {
        SetNameAndInstanceId("ModelAnimation", name);

        var directChannels = channels
            .Where(c => c.Target.Kind == ModelAnimationTargetKind.DirectIndex)
            .ToArray();
        var clips = new AnimationClip[directChannels.Length];
        for (var i = 0; i < directChannels.Length; i++)
        {
            var channel = directChannels[i];
            clips[i] = new AnimationClip(channel.ClipType, channel.Target.DirectIndex, channel.Keyframes);
        }

        Duration = duration;
        Channels = channels;
        Clips = clips;
    }

    public BoundModelAnimation Bind(ModelBoneHierarchy hierarchy)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);

        var clips = new AnimationClip[Channels.Length];
        for (var i = 0; i < Channels.Length; i++)
        {
            var channel = Channels[i];
            int boneIndex;
            if (channel.Target.Kind == ModelAnimationTargetKind.DirectIndex)
            {
                boneIndex = channel.Target.DirectIndex;
                if (boneIndex >= hierarchy.Bones.Length)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(hierarchy),
                        $"Animation '{Name}' channel {i} targets bone index {boneIndex}, but hierarchy '{hierarchy.Name}' has {hierarchy.Bones.Length} bones.");
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"Animation '{Name}' channel {i} uses a bone-name target, which cannot be bound yet.");
            }

            clips[i] = new AnimationClip(channel.ClipType, boneIndex, channel.Keyframes);
        }

        return new BoundModelAnimation(Name, Duration, clips);
    }
}
