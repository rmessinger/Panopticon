#nullable enable

using System;
using System.Linq;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.W3d;
using OpenSage.FileFormats.W3d;
using OpenSage.Graphics.Animation;

namespace OpenSage.Content.Loaders;

/// <summary>
/// Converts CPU-only imported animations into the format-neutral runtime asset.
/// </summary>
internal static class ImportedAnimationMaterializer
{
    public static ModelAnimation Materialize(ImportedAnimation imported)
    {
        var clips = imported.Channels
            .Select(ToRuntimeClip)
            .ToArray();

        return new ModelAnimation(imported.Name, imported.Duration, clips);
    }

    /// <summary>
    /// Legacy entry point used by the pre-seam W3D loader; WI-C1-12 compares this path with
    /// direct importer plus materializer output.
    /// </summary>
    public static ModelAnimation? FromW3dFile(W3dFile w3dFile)
    {
        var imported = W3dAnimationImporter.Convert(w3dFile);

        return imported is null
            ? null
            : Materialize(imported);
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
