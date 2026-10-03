#nullable enable

using System;
using Panopticon;

namespace Panopticon.Content.Importing;

public enum ImportedAnimationTargetKind
{
    DirectIndex,
    BoneName,
    SubObjectName,
    MaterialSlot,
}

/// <summary>
/// Addressing mode for one animation channel: an index into the source skeleton's bone list, an
/// exact bone name resolved against the destination hierarchy at binding time, a sub-object name
/// for visibility, or a material slot (sub-object plus material index) for UV and material color
/// animation. Named targets preserve the decoded name verbatim; normalization and fuzzy matching
/// are out of scope.
/// </summary>
public sealed class ImportedAnimationTarget
{
    private readonly int _directIndex;
    private readonly string? _boneName;
    private readonly string? _subObjectName;
    private readonly int _materialIndex;

    public ImportedAnimationTargetKind Kind { get; }

    public int DirectIndex => Kind == ImportedAnimationTargetKind.DirectIndex
        ? _directIndex
        : throw new InvalidOperationException("Target is not a direct-index target.");

    public string BoneName => Kind == ImportedAnimationTargetKind.BoneName
        ? _boneName!
        : throw new InvalidOperationException("Target is not a bone-name target.");

    /// <summary>Sub-object name; valid for sub-object and material-slot targets.</summary>
    public string SubObjectName => Kind is ImportedAnimationTargetKind.SubObjectName
        or ImportedAnimationTargetKind.MaterialSlot
        ? _subObjectName!
        : throw new InvalidOperationException("Target is not a sub-object target.");

    public int MaterialIndex => Kind == ImportedAnimationTargetKind.MaterialSlot
        ? _materialIndex
        : throw new InvalidOperationException("Target is not a material-slot target.");

    private ImportedAnimationTarget(
        ImportedAnimationTargetKind kind,
        int directIndex,
        string? boneName,
        string? subObjectName,
        int materialIndex)
    {
        Kind = kind;
        _directIndex = directIndex;
        _boneName = boneName;
        _subObjectName = subObjectName;
        _materialIndex = materialIndex;
    }

    public static ImportedAnimationTarget CreateDirectIndex(int directIndex)
    {
        Ensure.AtLeast(directIndex, 0, "Direct index must not be negative.");

        return new ImportedAnimationTarget(ImportedAnimationTargetKind.DirectIndex, directIndex, null, null, -1);
    }

    public static ImportedAnimationTarget CreateBoneName(string boneName)
    {
        Ensure.NotNullOrEmpty(boneName, "Bone name must not be null or empty.");

        return new ImportedAnimationTarget(ImportedAnimationTargetKind.BoneName, -1, boneName, null, -1);
    }

    public static ImportedAnimationTarget CreateSubObjectName(string subObjectName)
    {
        Ensure.NotNullOrEmpty(subObjectName, "Sub-object name must not be null or empty.");

        return new ImportedAnimationTarget(ImportedAnimationTargetKind.SubObjectName, -1, null, subObjectName, -1);
    }

    public static ImportedAnimationTarget CreateMaterialSlot(string subObjectFullName, int materialIndex)
    {
        Ensure.NotNullOrEmpty(subObjectFullName, "Sub-object name must not be null or empty.");

        if (materialIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(materialIndex), "Material index must not be negative.");

        return new ImportedAnimationTarget(
            ImportedAnimationTargetKind.MaterialSlot, -1, null, subObjectFullName, materialIndex);
    }
}
