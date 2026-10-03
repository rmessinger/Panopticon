#nullable enable

using System;

using System.Collections.Generic;

namespace Panopticon.Content.Importing;

/// <summary>
/// CPU-only skeleton: bones ordered parent before child, addressed by index.
/// </summary>
public sealed class ImportedSkeleton
{
    public string Name { get; }

    public IReadOnlyList<ImportedBone> Bones { get; }

    public ImportedSkeleton(string name, IReadOnlyList<ImportedBone> bones)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Skeleton name must not be null or empty.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(bones);

        if (bones.Count == 0)
        {
            throw new ArgumentException("A skeleton must have at least one bone.", nameof(bones));
        }

        var copiedBones = new ImportedBone[bones.Count];
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (bone is null)
            {
                throw new ArgumentException($"Bone at index {i} must not be null.", nameof(bones));
            }

            if (bone.ParentIndex >= i)
            {
                throw new ArgumentException(
                    $"Bone '{bone.Name}' at index {i} references parent index {bone.ParentIndex}; " +
                    "parents must appear before children.",
                    nameof(bones));
            }

            copiedBones[i] = bone;
        }

        Name = name;
        Bones = copiedBones;
    }
}
