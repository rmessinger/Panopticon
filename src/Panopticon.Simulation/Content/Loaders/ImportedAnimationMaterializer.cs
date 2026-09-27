#nullable enable

using System;
using System.Linq;
using Panopticon.Content.Importing;
using Panopticon.Graphics.Animation;

namespace Panopticon.Content.Loaders;

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
            ImportedAnimationChannelType.Scale => AnimationClipType.Scale,
            ImportedAnimationChannelType.Visibility => AnimationClipType.Visibility,
            ImportedAnimationChannelType.TextureTranslateU => AnimationClipType.TextureTranslateU,
            ImportedAnimationChannelType.TextureTranslateV => AnimationClipType.TextureTranslateV,
            ImportedAnimationChannelType.TextureScaleU => AnimationClipType.TextureScaleU,
            ImportedAnimationChannelType.TextureScaleV => AnimationClipType.TextureScaleV,
            ImportedAnimationChannelType.TextureRotate => AnimationClipType.TextureRotate,
            ImportedAnimationChannelType.MaterialAmbientColor => AnimationClipType.MaterialAmbientColor,
            ImportedAnimationChannelType.MaterialDiffuseColor => AnimationClipType.MaterialDiffuseColor,
            ImportedAnimationChannelType.MaterialSpecularColor => AnimationClipType.MaterialSpecularColor,
            ImportedAnimationChannelType.MaterialEmissiveColor => AnimationClipType.MaterialEmissiveColor,
            ImportedAnimationChannelType.MaterialAlpha => AnimationClipType.MaterialAlpha,
            _ => throw new ArgumentOutOfRangeException(nameof(channel)),
        };

        var target = channel.Target.Kind switch
        {
            ImportedAnimationTargetKind.DirectIndex => ModelAnimationTarget.CreateDirectIndex(channel.Target.DirectIndex),
            ImportedAnimationTargetKind.BoneName => ModelAnimationTarget.CreateBoneName(channel.Target.BoneName),
            ImportedAnimationTargetKind.SubObjectName => ModelAnimationTarget.CreateSubObjectName(channel.Target.SubObjectName),
            ImportedAnimationTargetKind.MaterialSlot => ModelAnimationTarget.CreateMaterialSlot(
                channel.Target.SubObjectName, channel.Target.MaterialIndex),
            _ => throw new ArgumentOutOfRangeException(nameof(channel)),
        };

        var interpolation = channel.Interpolation switch
        {
            ImportedAnimationInterpolation.Linear => AnimationInterpolation.Linear,
            ImportedAnimationInterpolation.Step => AnimationInterpolation.Step,
            _ => throw new ArgumentOutOfRangeException(nameof(channel)),
        };

        var keyframes = channel.Keyframes
            .Select(k => new Keyframe(k.Time, ToRuntimeValue(k)))
            .ToArray();

        return new ModelAnimationChannel(target, clipType, keyframes, interpolation);
    }

    private static KeyframeValue ToRuntimeValue(ImportedKeyframe keyframe)
    {
        return keyframe.Kind switch
        {
            ImportedKeyframeKind.Float => new KeyframeValue { FloatValue = keyframe.FloatValue },
            ImportedKeyframeKind.Quaternion => new KeyframeValue { Quaternion = keyframe.QuaternionValue },
            ImportedKeyframeKind.Bool => new KeyframeValue { BoolValue = keyframe.BoolValue },
            ImportedKeyframeKind.Vector3 => new KeyframeValue { Vector3Value = keyframe.Vector3Value },
            _ => throw new ArgumentOutOfRangeException(nameof(keyframe)),
        };
    }
}
