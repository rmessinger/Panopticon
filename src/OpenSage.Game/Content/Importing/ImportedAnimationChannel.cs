#nullable enable

using System;
using System.Collections.Generic;

namespace OpenSage.Content.Importing;

public enum ImportedAnimationChannelType
{
    TranslationX,
    TranslationY,
    TranslationZ,
    Quaternion,
    Visibility,
}

/// <summary>
/// One animation channel: bone index into the target skeleton, channel type, and keyframes with
/// monotonically non-decreasing times whose value kind matches the channel type.
/// </summary>
public sealed class ImportedAnimationChannel
{
    public int BoneIndex { get; }

    public ImportedAnimationChannelType ChannelType { get; }

    public IReadOnlyList<ImportedKeyframe> Keyframes { get; }

    public ImportedAnimationChannel(
        int boneIndex,
        ImportedAnimationChannelType channelType,
        IReadOnlyList<ImportedKeyframe> keyframes)
    {
        if (boneIndex < 0)
        {
            throw new ArgumentException("Bone index must not be negative.", nameof(boneIndex));
        }

        if (keyframes is null)
        {
            throw new ArgumentNullException(nameof(keyframes));
        }

        if (keyframes.Count == 0)
        {
            throw new ArgumentException("A channel must have at least one keyframe.", nameof(keyframes));
        }

        var expectedKind = ImportedKeyframe.GetExpectedKind(channelType);

        var copiedKeyframes = new ImportedKeyframe[keyframes.Count];
        for (var i = 0; i < keyframes.Count; i++)
        {
            var keyframe = keyframes[i];
            if (keyframe is null)
            {
                throw new ArgumentException($"Keyframe at index {i} must not be null.", nameof(keyframes));
            }

            if (keyframe.Kind != expectedKind)
            {
                throw new ArgumentException(
                    $"Keyframe {i} holds a {keyframe.Kind} value, but channel type {channelType} requires {expectedKind}.",
                    nameof(keyframes));
            }

            if (i > 0 && keyframe.Time < keyframes[i - 1].Time)
            {
                throw new ArgumentException(
                    $"Keyframe {i} time {keyframe.Time} precedes keyframe {i - 1} time {keyframes[i - 1].Time}.",
                    nameof(keyframes));
            }

            copiedKeyframes[i] = keyframe;
        }

        BoneIndex = boneIndex;
        ChannelType = channelType;
        Keyframes = copiedKeyframes;
    }
}
