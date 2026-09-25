#nullable enable

using System;
using System.Collections.Generic;

namespace Panopticon.Content.Importing;

public enum ImportedAnimationChannelType
{
    TranslationX,
    TranslationY,
    TranslationZ,
    Quaternion,
    Visibility,
}

/// <summary>
/// One animation channel: target bone in the source skeleton, channel type, and keyframes with
/// monotonically non-decreasing times whose value kind matches the channel type.
/// </summary>
public sealed class ImportedAnimationChannel
{
    public ImportedAnimationTarget Target { get; }

    public ImportedAnimationChannelType ChannelType { get; }

    public IReadOnlyList<ImportedKeyframe> Keyframes { get; }

    public ImportedAnimationInterpolation Interpolation { get; }

    /// <summary>
    /// Compatibility view of direct-index targets for pre-retargeting consumers. Throws for
    /// bone-name targets, which have no meaningful index until bound to a hierarchy.
    /// </summary>
    public int BoneIndex => Target.Kind == ImportedAnimationTargetKind.DirectIndex
        ? Target.DirectIndex
        : throw new InvalidOperationException("Channel does not use a direct-index target.");

    public ImportedAnimationChannel(
        int boneIndex,
        ImportedAnimationChannelType channelType,
        IReadOnlyList<ImportedKeyframe> keyframes)
        : this(ImportedAnimationTarget.CreateDirectIndex(boneIndex), channelType, keyframes)
    {
    }

    public ImportedAnimationChannel(
        ImportedAnimationTarget target,
        ImportedAnimationChannelType channelType,
        IReadOnlyList<ImportedKeyframe> keyframes)
        : this(target, channelType, keyframes, ImportedAnimationInterpolation.Linear)
    {
    }

    public ImportedAnimationChannel(
        ImportedAnimationTarget target,
        ImportedAnimationChannelType channelType,
        IReadOnlyList<ImportedKeyframe> keyframes,
        ImportedAnimationInterpolation interpolation)
    {
        if (!Enum.IsDefined(interpolation))
        {
            throw new ArgumentOutOfRangeException(nameof(interpolation));
        }

        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
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

        Target = target;
        ChannelType = channelType;
        Keyframes = copiedKeyframes;
        Interpolation = interpolation;
    }
}
