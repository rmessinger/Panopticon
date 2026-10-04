#nullable enable

using System;
using System.Collections.Generic;
using Panopticon;

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
        Ensure.NotNullOrEmpty(name, "Skeleton name must not be null or empty.");

        ArgumentNullException.ThrowIfNull(bones);

        Ensure.NotEmpty(bones, "A skeleton must have at least one bone.");

        var copiedBones = new ImportedBone[bones.Count];
        Ensure.NoNullElements(bones, "Bone");

        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];

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
