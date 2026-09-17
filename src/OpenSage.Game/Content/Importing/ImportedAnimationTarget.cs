#nullable enable

using System;

namespace OpenSage.Content.Importing;

public enum ImportedAnimationTargetKind
{
    DirectIndex,
    BoneName,
}

/// <summary>
/// Addressing mode for one animation channel: an index into the source skeleton's bone list, or
/// an exact bone name resolved against the destination hierarchy at binding time. Named targets
/// preserve the decoded name verbatim; normalization and fuzzy matching are out of scope.
/// </summary>
public sealed class ImportedAnimationTarget
{
    private readonly int _directIndex;
    private readonly string? _boneName;

    public ImportedAnimationTargetKind Kind { get; }

    public int DirectIndex => Kind == ImportedAnimationTargetKind.DirectIndex
        ? _directIndex
        : throw new InvalidOperationException("Target is not a direct-index target.");

    public string BoneName => Kind == ImportedAnimationTargetKind.BoneName
        ? _boneName!
        : throw new InvalidOperationException("Target is not a bone-name target.");

    private ImportedAnimationTarget(ImportedAnimationTargetKind kind, int directIndex, string? boneName)
    {
        Kind = kind;
        _directIndex = directIndex;
        _boneName = boneName;
    }

    public static ImportedAnimationTarget CreateDirectIndex(int directIndex)
    {
        if (directIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(directIndex), "Direct index must not be negative.");
        }

        return new ImportedAnimationTarget(ImportedAnimationTargetKind.DirectIndex, directIndex, null);
    }

    public static ImportedAnimationTarget CreateBoneName(string boneName)
    {
        if (string.IsNullOrEmpty(boneName))
        {
            throw new ArgumentException("Bone name must not be null or empty.", nameof(boneName));
        }

        return new ImportedAnimationTarget(ImportedAnimationTargetKind.BoneName, -1, boneName);
    }
}
