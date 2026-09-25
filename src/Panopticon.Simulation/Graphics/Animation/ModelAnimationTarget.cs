#nullable enable

using System;

namespace Panopticon.Graphics.Animation;

public enum ModelAnimationTargetKind
{
    DirectIndex,
    BoneName,
}

/// <summary>
/// Runtime addressing mode for one animation channel, converted from the imported target by the
/// engine-owned materializer. No importer or parser types appear here.
/// </summary>
public sealed class ModelAnimationTarget
{
    private readonly int _directIndex;
    private readonly string? _boneName;

    public ModelAnimationTargetKind Kind { get; }

    public int DirectIndex => Kind == ModelAnimationTargetKind.DirectIndex
        ? _directIndex
        : throw new InvalidOperationException("Target is not a direct-index target.");

    public string BoneName => Kind == ModelAnimationTargetKind.BoneName
        ? _boneName!
        : throw new InvalidOperationException("Target is not a bone-name target.");

    private ModelAnimationTarget(ModelAnimationTargetKind kind, int directIndex, string? boneName)
    {
        Kind = kind;
        _directIndex = directIndex;
        _boneName = boneName;
    }

    public static ModelAnimationTarget CreateDirectIndex(int directIndex)
    {
        if (directIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(directIndex), "Direct index must not be negative.");
        }

        return new ModelAnimationTarget(ModelAnimationTargetKind.DirectIndex, directIndex, null);
    }

    public static ModelAnimationTarget CreateBoneName(string boneName)
    {
        if (string.IsNullOrEmpty(boneName))
        {
            throw new ArgumentException("Bone name must not be null or empty.", nameof(boneName));
        }

        return new ModelAnimationTarget(ModelAnimationTargetKind.BoneName, -1, boneName);
    }
}
