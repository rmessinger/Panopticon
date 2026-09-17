using System;
using System.Collections.Generic;
using System.IO;
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

        Dictionary<string, int>? boneNameIndex = null;
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
                boneNameIndex ??= BuildBoneNameIndex(hierarchy);
                var boneName = channel.Target.BoneName;
                if (!boneNameIndex.TryGetValue(boneName, out boneIndex))
                {
                    throw new InvalidDataException(
                        $"Animation '{Name}' targets bone '{boneName}', which does not exist in hierarchy '{hierarchy.Name}'.");
                }
            }

            clips[i] = new AnimationClip(channel.ClipType, boneIndex, channel.Keyframes);
        }

        return new BoundModelAnimation(Name, Duration, clips);
    }

    private static Dictionary<string, int> BuildBoneNameIndex(ModelBoneHierarchy hierarchy)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < hierarchy.Bones.Length; i++)
        {
            var bone = hierarchy.Bones[i];
            if (bone.Name is null)
            {
                continue;
            }

            if (index.TryGetValue(bone.Name, out var existingIndex))
            {
                throw new InvalidDataException(
                    $"Hierarchy '{hierarchy.Name}' contains bones '{hierarchy.Bones[existingIndex].Name}' at index {existingIndex} and '{bone.Name}' at index {i} differing only by case; named animation targets would be ambiguous.");
            }

            index.Add(bone.Name, i);
        }

        return index;
    }
}
