#nullable enable

using System;
using System.Collections.Generic;
using Panopticon;

namespace Panopticon.Content.Importing;

public enum ImportedAnimationChannelType
{
    TranslationX,
    TranslationY,
    TranslationZ,
    Quaternion,
    Scale,
    Visibility,
    TextureTranslateU,
    TextureTranslateV,
    TextureScaleU,
    TextureScaleV,
    TextureRotate,
    MaterialAmbientColor,
    MaterialDiffuseColor,
    MaterialSpecularColor,
    MaterialEmissiveColor,
    MaterialAlpha,
}

/// <summary>
/// One animation channel: target bone in the source skeleton, channel type, and keyframes with
/// monotonically non-decreasing times whose value kind matches the channel type. Direct-index and
/// bone-name targets address bone channels (translation, rotation, visibility on a bone);
/// sub-object names address visibility on a sub-object; material slots address UV and material
/// color channels.
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
            throw new ArgumentOutOfRangeException(nameof(interpolation));

        ArgumentNullException.ThrowIfNull(target);

        ArgumentNullException.ThrowIfNull(keyframes);

        Ensure.NotEmpty(keyframes, "A channel must have at least one keyframe.");

        ValidateTargetKind(target, channelType);

        var expectedKind = ImportedKeyframe.GetExpectedKind(channelType);

        var copiedKeyframes = new ImportedKeyframe[keyframes.Count];
        Ensure.NoNullElements(keyframes, "Keyframe");

        for (var i = 0; i < keyframes.Count; i++)
        {
            var keyframe = keyframes[i];

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

    private static void ValidateTargetKind(ImportedAnimationTarget target, ImportedAnimationChannelType channelType)
    {
        var wrongForKind = target.Kind switch
        {
            ImportedAnimationTargetKind.DirectIndex
                when !IsBoneChannel(channelType) =>
                $"Direct-index targets address bones; channel type {channelType} is a property channel.",

            ImportedAnimationTargetKind.BoneName
                when !IsBoneChannel(channelType) =>
                $"Bone-name targets address bones; channel type {channelType} is a property channel.",

            ImportedAnimationTargetKind.SubObjectName
                when channelType != ImportedAnimationChannelType.Visibility =>
                $"Sub-object targets address visibility; channel type {channelType} is not a visibility channel.",

            ImportedAnimationTargetKind.MaterialSlot
                when !IsMaterialChannel(channelType) =>
                $"Material-slot targets address UV and material color channels; channel type {channelType} is a bone channel.",

            _ => null,
        };

        if (wrongForKind is not null)
            throw new ArgumentException(
                $"Target kind {target.Kind} does not accept channel type {channelType}. {wrongForKind}",
                nameof(target));
    }

    internal static bool IsBoneChannel(ImportedAnimationChannelType channelType)
    {
        return channelType is ImportedAnimationChannelType.TranslationX
            or ImportedAnimationChannelType.TranslationY
            or ImportedAnimationChannelType.TranslationZ
            or ImportedAnimationChannelType.Quaternion
            or ImportedAnimationChannelType.Scale
            or ImportedAnimationChannelType.Visibility;
    }

    internal static bool IsMaterialChannel(ImportedAnimationChannelType channelType)
    {
        return channelType is ImportedAnimationChannelType.TextureTranslateU
            or ImportedAnimationChannelType.TextureTranslateV
            or ImportedAnimationChannelType.TextureScaleU
            or ImportedAnimationChannelType.TextureScaleV
            or ImportedAnimationChannelType.TextureRotate
            or ImportedAnimationChannelType.MaterialAmbientColor
            or ImportedAnimationChannelType.MaterialDiffuseColor
            or ImportedAnimationChannelType.MaterialSpecularColor
            or ImportedAnimationChannelType.MaterialEmissiveColor
            or ImportedAnimationChannelType.MaterialAlpha;
    }
}
