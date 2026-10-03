#nullable enable

using System;
using Panopticon;

namespace Panopticon.Graphics.Animation;

public enum ModelAnimationTargetKind
{
    DirectIndex,
    BoneName,
    SubObjectName,
    MaterialSlot,
}

/// <summary>
/// Runtime addressing mode for one animation channel, converted from the imported target by the
/// engine-owned materializer. No importer or parser types appear here. Sub-object and
/// material-slot targets bind against a model, not a bone hierarchy.
/// </summary>
public sealed class ModelAnimationTarget
{
    private readonly int _directIndex;
    private readonly string? _boneName;
    private readonly string? _subObjectName;
    private readonly int _materialIndex;

    public ModelAnimationTargetKind Kind { get; }

    public int DirectIndex => Kind == ModelAnimationTargetKind.DirectIndex
        ? _directIndex
        : throw new InvalidOperationException("Target is not a direct-index target.");

    public string BoneName => Kind == ModelAnimationTargetKind.BoneName
        ? _boneName!
        : throw new InvalidOperationException("Target is not a bone-name target.");

    /// <summary>Sub-object name; valid for sub-object and material-slot targets.</summary>
    public string SubObjectName => Kind is ModelAnimationTargetKind.SubObjectName
        or ModelAnimationTargetKind.MaterialSlot
        ? _subObjectName!
        : throw new InvalidOperationException("Target is not a sub-object target.");

    public int MaterialIndex => Kind == ModelAnimationTargetKind.MaterialSlot
        ? _materialIndex
        : throw new InvalidOperationException("Target is not a material-slot target.");

    private ModelAnimationTarget(
        ModelAnimationTargetKind kind,
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

    public static ModelAnimationTarget CreateDirectIndex(int directIndex)
    {
        if (directIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(directIndex), "Direct index must not be negative.");
        }

        return new ModelAnimationTarget(ModelAnimationTargetKind.DirectIndex, directIndex, null, null, -1);
    }

    public static ModelAnimationTarget CreateBoneName(string boneName)
    {
        Ensure.NotNullOrEmpty(boneName, "Bone name must not be null or empty.");

        return new ModelAnimationTarget(ModelAnimationTargetKind.BoneName, -1, boneName, null, -1);
    }

    public static ModelAnimationTarget CreateSubObjectName(string subObjectName)
    {
        Ensure.NotNullOrEmpty(subObjectName, "Sub-object name must not be null or empty.");

        return new ModelAnimationTarget(ModelAnimationTargetKind.SubObjectName, -1, null, subObjectName, -1);
    }

    public static ModelAnimationTarget CreateMaterialSlot(string subObjectFullName, int materialIndex)
    {
        Ensure.NotNullOrEmpty(subObjectFullName, "Sub-object name must not be null or empty.");

        if (materialIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(materialIndex), "Material index must not be negative.");
        }

        return new ModelAnimationTarget(ModelAnimationTargetKind.MaterialSlot, -1, null, subObjectFullName, materialIndex);
    }
}
