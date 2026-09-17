#nullable enable

using System;
using System.Linq;
using OpenSage.Content.Importing;
using OpenSage.Graphics.Animation;

namespace OpenSage.Content.Loaders;

/// <summary>
/// Converts CPU-only imported animations into the format-neutral runtime asset.
/// </summary>
internal static class ImportedAnimationMaterializer
{
    public static ModelAnimation Materialize(ImportedAnimation imported)
    {
        var channels = imported.Channels
            .Select(ToRuntimeChannel)
            .ToArray();

        return new ModelAnimation(imported.Name, imported.Duration, channels);
    }

    private static ModelAnimationChannel ToRuntimeChannel(ImportedAnimationChannel channel)
    {
        var clipType = channel.ChannelType switch
        {
            ImportedAnimationChannelType.TranslationX => AnimationClipType.TranslationX,
            ImportedAnimationChannelType.TranslationY => AnimationClipType.TranslationY,
            ImportedAnimationChannelType.TranslationZ => AnimationClipType.TranslationZ,
            ImportedAnimationChannelType.Quaternion => AnimationClipType.Quaternion,
            ImportedAnimationChannelType.Visibility => AnimationClipType.Visibility,
            _ => throw new ArgumentOutOfRangeException(nameof(channel)),
        };

        var target = channel.Target.Kind switch
        {
            ImportedAnimationTargetKind.DirectIndex => ModelAnimationTarget.CreateDirectIndex(channel.Target.DirectIndex),
            ImportedAnimationTargetKind.BoneName => ModelAnimationTarget.CreateBoneName(channel.Target.BoneName),
            _ => throw new ArgumentOutOfRangeException(nameof(channel)),
        };

        var keyframes = channel.Keyframes
            .Select(k => new Keyframe(k.Time, ToRuntimeValue(k)))
            .ToArray();

        return new ModelAnimationChannel(target, clipType, keyframes);
    }

    private static KeyframeValue ToRuntimeValue(ImportedKeyframe keyframe)
    {
        return keyframe.Kind switch
        {
            ImportedKeyframeKind.Float => new KeyframeValue { FloatValue = keyframe.FloatValue },
            ImportedKeyframeKind.Quaternion => new KeyframeValue { Quaternion = keyframe.QuaternionValue },
            ImportedKeyframeKind.Bool => new KeyframeValue { BoolValue = keyframe.BoolValue },
            _ => throw new ArgumentOutOfRangeException(nameof(keyframe)),
        };
    }
}
