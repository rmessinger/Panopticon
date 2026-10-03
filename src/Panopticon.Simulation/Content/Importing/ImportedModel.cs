#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon;

namespace Panopticon.Content.Importing;

/// <summary>
/// CPU-only interchange root for one imported model asset. Validated at construction; safe to
/// inspect without a graphics device. See docs/roadmap/deep-dives/c1-import-abstraction.md.
/// </summary>
public enum ImportedSkeletonOrigin
{
    Embedded,
    Referenced,
    Default,
}

public sealed class ImportedModel
{
    public string Name { get; }

    public ImportedSkeleton Skeleton { get; }

    /// <summary>
    /// How the skeleton was selected. Referenced skeletons let the on-demand loader delegate to
    /// the shared ModelBoneHierarchies cache instead of building a private hierarchy.
    /// </summary>
    public ImportedSkeletonOrigin SkeletonOrigin { get; }

    public IReadOnlyList<ImportedSubObject> SubObjects { get; }

    /// <summary>Feature gates the importing context had enabled; morph sets only materialize under theirs.</summary>
    public IReadOnlySet<string> EnabledFeatureGates { get; }

    public bool IsFeatureGateEnabled(string name)
    {
        return EnabledFeatureGates.Contains(name);
    }

    public ImportedModel(
        string name,
        ImportedSkeleton skeleton,
        IReadOnlyList<ImportedSubObject> subObjects,
        ImportedSkeletonOrigin skeletonOrigin,
        IReadOnlyCollection<string>? enabledFeatureGates = null)
    {
        Ensure.NotNullOrEmpty(name, "Model name must not be null or empty.");

        ArgumentNullException.ThrowIfNull(skeleton);

        ArgumentNullException.ThrowIfNull(subObjects);

        EnabledFeatureGates = enabledFeatureGates is null
            ? new HashSet<string>()
            : enabledFeatureGates.ToHashSet(StringComparer.Ordinal);

        var boneCount = skeleton.Bones.Count;

        var copiedSubObjects = new ImportedSubObject[subObjects.Count];
        Ensure.NoNullElements(subObjects, "Sub-object");

        for (var i = 0; i < subObjects.Count; i++)
        {
            var subObject = subObjects[i];

            if (subObject.ParentBoneIndex >= boneCount)
            {
                throw new ArgumentException(
                    $"Sub-object '{subObject.FullName}' references bone {subObject.ParentBoneIndex}, " +
                    $"but the skeleton has {boneCount} bones.",
                    nameof(subObjects));
            }

            if (subObject.RenderObject is ImportedMesh mesh && mesh.IsSkinned)
            {
                foreach (var vertex in mesh.Vertices)
                {
                    if (vertex.BoneIndex0 >= boneCount || vertex.BoneIndex1 >= boneCount ||
                        vertex.BoneIndex2 >= boneCount || vertex.BoneIndex3 >= boneCount)
                    {
                        throw new ArgumentException(
                            $"Skinned mesh '{subObject.FullName}' has a vertex referencing a bone outside the " +
                            $"skeleton's {boneCount} bones.",
                            nameof(subObjects));
                    }
                }
            }

            copiedSubObjects[i] = subObject;
        }

        Name = name;
        SkeletonOrigin = skeletonOrigin;
        Skeleton = skeleton;
        SubObjects = copiedSubObjects;
    }
}
