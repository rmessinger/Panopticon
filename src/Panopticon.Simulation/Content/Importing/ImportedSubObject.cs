#nullable enable

using System;

namespace Panopticon.Content.Importing;

/// <summary>
/// One named renderable attached to a skeleton bone.
/// </summary>
public sealed class ImportedSubObject
{
    public string FullName { get; }

    public string DisplayName { get; }

    public int ParentBoneIndex { get; }

    public ImportedRenderObject RenderObject { get; }

    public ImportedSubObject(
        string fullName,
        string displayName,
        int parentBoneIndex,
        ImportedRenderObject renderObject)
    {
        if (fullName is null)
        {
            throw new ArgumentNullException(nameof(fullName));
        }

        if (displayName is null)
        {
            throw new ArgumentNullException(nameof(displayName));
        }

        if (parentBoneIndex < 0)
        {
            throw new ArgumentException("Parent bone index must not be negative.", nameof(parentBoneIndex));
        }

        if (renderObject is null)
        {
            throw new ArgumentNullException(nameof(renderObject));
        }

        FullName = fullName;
        DisplayName = displayName;
        ParentBoneIndex = parentBoneIndex;
        RenderObject = renderObject;
    }
}
