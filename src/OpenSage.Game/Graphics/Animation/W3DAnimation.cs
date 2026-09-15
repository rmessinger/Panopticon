using System;
using System.Linq;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.W3d;
using OpenSage.FileFormats.W3d;

namespace OpenSage.Graphics.Animation;

/// <summary>
/// Runtime animation asset. Conversion now runs through the W3D importer's interchange values;
/// WI-C1-07 replaces this compatibility mapping with a format-neutral materializer.
/// </summary>
public sealed class W3DAnimation : BaseAsset
{
    internal static W3DAnimation? FromW3dFile(W3dFile w3dFile)
    {
        var imported = W3dAnimationImporter.Convert(w3dFile);

        return imported is null
            ? null
            : new W3DAnimation(imported);
    }

    public TimeSpan Duration { get; }

    public AnimationClip[] Clips { get; }

    internal W3DAnimation(string name, TimeSpan duration, AnimationClip[] clips)
    {
        SetNameAndInstanceId("W3DAnimation", name);
        Duration = duration;
        Clips = clips;
    }

    private W3DAnimation(ImportedAnimation imported)
    {
        SetNameAndInstanceId("W3DAnimation", imported.Name);
        Duration = imported.Duration;

        Clips = imported.Channels
            .Select(ToRuntimeClip)
            .ToArray();
    }

    private static AnimationClip ToRuntimeClip(ImportedAnimationChannel channel)
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

        var keyframes = channel.Keyframes
            .Select(k => new Keyframe(k.Time, ToRuntimeValue(k)))
            .ToArray();

        return new AnimationClip(clipType, channel.BoneIndex, keyframes);
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
